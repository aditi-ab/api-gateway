<template>
  <div class="page-container">
    <div class="flex items-end justify-between gap-4">
      <div>
        <h1>Revision history and rollback</h1><p class="text-muted-foreground">
          Published revisions remain immutable. Rollback only changes the active revision.
        </p>
      </div><Field class="w-70">
        <FieldLabel for="revision-environment">
          Environment
        </FieldLabel><Select v-model="environmentId">
          <SelectTrigger id="revision-environment">
            <SelectValue />
          </SelectTrigger><SelectContent>
            <SelectItem v-for="environment in environments" :key="environment.id" :value="environment.id">
              {{ environment.displayName }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
    </div>
    <Alert v-if="error" variant="destructive" class="mt-4">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert><Alert v-if="message" class="mt-4 border-emerald-500/40 text-emerald-700 dark:text-emerald-300">
      <CircleCheck /><AlertDescription>{{ message }}</AlertDescription>
    </Alert>
    <Card class="mt-6 py-0">
      <CardContent>
        <Field>
          <FieldLabel for="diff-baseline">
            Diff baseline
          </FieldLabel><Select v-model="diffFrom">
            <SelectTrigger id="diff-baseline">
              <SelectValue placeholder="Choose a revision" />
            </SelectTrigger><SelectContent>
              <SelectItem v-for="revision in revisions" :key="revision.id" :value="revision.id">
                Compare from revision {{ revision.number }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field>
      </CardContent><Table>
        <TableHeader><TableRow><TableHead>Revision</TableHead><TableHead>State</TableHead><TableHead>Published</TableHead><TableHead>Hash</TableHead><TableHead>Comment</TableHead><TableHead /></TableRow></TableHeader><TableBody>
          <TableRow v-for="revision in revisions" :key="revision.id">
            <TableCell>
              {{ revision.number }} <Badge v-if="environments.find(x => x.id === environmentId)?.activeRevisionId === revision.id" variant="success">
                Active
              </Badge>
            </TableCell><TableCell>{{ revision.state }}</TableCell><TableCell>{{ revision.publishedAtUtc ? formatDateTime(revision.publishedAtUtc) : 'Not published' }}</TableCell><TableCell><code>{{ revision.contentHash.slice(0, 12) }}</code></TableCell><TableCell>{{ revision.comment || '' }}</TableCell><TableCell>
              <div class="flex justify-end gap-1">
                <IconButton label="View" @click="viewing = revision;diffPaths = [];diffChanges = []">
                  <Eye />
                </IconButton><IconButton v-if="diffFrom && diffFrom !== revision.id" label="Diff" @click="diff(revision)">
                  <GitCompareArrows />
                </IconButton><IconButton label="Export" @click="exportRevision(revision)">
                  <Download />
                </IconButton><IconButton v-if="revision.state === 'PUBLISHED'" label="Promote" @click="openPromotion(revision)">
                  <Upload />
                </IconButton><IconButton v-if="revision.state === 'PUBLISHED' && environments.find(x => x.id === environmentId)?.activeRevisionId !== revision.id" variant="secondary" label="Activate" @click="rollback(revision)">
                  <RotateCcw />
                </IconButton>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>
    <Dialog :open="!!viewing" @update:open="!$event && (viewing = null)">
      <DialogContent size="4xl">
        <template v-if="viewing">
          <DialogHeader><DialogTitle>Revision {{ viewing.number }}</DialogTitle></DialogHeader>
          <Alert v-if="diffPaths.length" class="mb-4">
            <Info /><AlertDescription>{{ diffPaths.length }} changed JSON paths</AlertDescription>
          </Alert><Table v-if="diffChanges.length" class="mb-4">
            <TableHeader><TableRow><TableHead>Path</TableHead><TableHead>Before</TableHead><TableHead>After</TableHead></TableRow></TableHeader><TableBody>
              <TableRow v-for="change in diffChanges" :key="change.path">
                <TableCell><code>{{ change.path }}</code></TableCell><TableCell><code>{{ change.beforeJson ?? '∅' }}</code></TableCell><TableCell><code>{{ change.afterJson ?? '∅' }}</code></TableCell>
              </TableRow>
            </TableBody>
          </Table><pre class="config-json">{{ JSON.stringify(JSON.parse(viewing.configJson), null, 2) }}</pre>
          <DialogFooter>
            <Button variant="outline" @click="viewing = null">
              Close
            </Button>
          </DialogFooter>
        </template>
      </DialogContent>
    </Dialog>
    <Dialog :open="!!promoting" @update:open="!$event && (promoting = null)">
      <DialogContent size="lg">
        <template v-if="promoting">
          <DialogHeader><DialogTitle>Promote revision {{ promoting.number }}</DialogTitle></DialogHeader>
          <p class="mb-4">
            Promotion creates an editable draft and never publishes automatically.
          </p><Field>
            <FieldLabel for="promotion-target">
              Target environment
            </FieldLabel><Select v-model="targetEnvironmentId">
              <SelectTrigger id="promotion-target">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="environment in environments.filter(x => x.id !== environmentId)" :key="environment.id" :value="environment.id">
                  {{ environment.displayName }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <DialogFooter>
            <Button variant="outline" @click="promoting = null">
              Cancel
            </Button><Button :disabled="!targetEnvironmentId" @click="promote">
              Create draft
            </Button>
          </DialogFooter>
        </template>
      </DialogContent>
    </Dialog>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Badge, Button, Card, CardContent, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, Field, FieldLabel, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@aditify/ui';
import { CircleAlert, CircleCheck, Download, Eye, GitCompareArrows, Info, RotateCcw, Upload } from '@lucide/vue';
import { onMounted, ref, watch } from 'vue';
import { graphql } from '../api';
import IconButton from '../components/IconButton.vue';
import { confirmAction } from '../composables/confirmDialog';
import { formatDateTime } from '../utils/dateTime';

interface Environment { id: string; displayName: string; activeRevisionId?: string }interface Revision { id: string; number: number; state: string; contentHash: string; createdBy: string; createdAtUtc: string; publishedBy?: string; publishedAtUtc?: string; comment?: string; configJson: string }
interface Change { path: string; beforeJson?: string; afterJson?: string }

const environments = ref<Environment[]>([]); const environmentId = ref(''); const revisions = ref<Revision[]>([]); const viewing = ref<Revision | null>(null); const diffFrom = ref(''); const diffPaths = ref<string[]>([]); const diffChanges = ref<Change[]>([]); const promoting = ref<Revision | null>(null); const targetEnvironmentId = ref(''); const error = ref(''); const message = ref('');

async function initial() { environments.value = (await graphql<{ environments: Environment[] }>(`query{environments{id displayName activeRevisionId}}`)).environments; environmentId.value = environments.value[0]?.id || ''; }
async function load() {
  if (!environmentId.value)
    return;

  await run(async () => { revisions.value = (await graphql<{ revisions: Revision[] }>(`query($id:UUID!){revisions(environmentId:$id){id number state contentHash createdBy createdAtUtc publishedBy publishedAtUtc comment configJson}}`, { id: environmentId.value })).revisions; diffFrom.value = revisions.value[1]?.id || ''; });
}
function openPromotion(revision: Revision) { targetEnvironmentId.value = ''; promoting.value = revision; }
async function rollback(revision: Revision) {
  const environment = environments.value.find(x => x.id === environmentId.value);

  if (!environment || !await confirmAction(`Activate published revision ${revision.number}?`, { title: 'Activate revision?', confirmText: 'Activate' }))
    return;

  await run(async () => { await graphql(`mutation($environment:UUID!,$revision:UUID!){rollbackEnvironment(environmentId:$environment,targetRevisionId:$revision)}`, { environment: environment.id, revision: revision.id }); await initial(); message.value = `Revision ${revision.number} is now desired.`; });
}
async function diff(to: Revision) {
  if (!diffFrom.value || diffFrom.value === to.id)
    return;

  await run(async () => {
    const result = (await graphql<{ revisionDiff: { changedPaths: string[]; changes: Change[] } }>(`query($from:UUID!,$to:UUID!){revisionDiff(fromRevisionId:$from,toRevisionId:$to){changedPaths changes{path beforeJson afterJson}}}`, { from: diffFrom.value, to: to.id })).revisionDiff;

    diffPaths.value = result.changedPaths; diffChanges.value = result.changes; viewing.value = to;
  });
}
async function exportRevision(revision: Revision) {
  await run(async () => {
    const result = await graphql<{ exportRevision: { json: string } }>(`mutation($id:UUID!){exportRevision(revisionId:$id){json}}`, { id: revision.id }); const blob = new Blob([result.exportRevision.json], { type: 'application/json' }); const url = URL.createObjectURL(blob); const anchor = document.createElement('a');

    anchor.href = url; anchor.download = `gateway-revision-${revision.number}.json`; anchor.click(); URL.revokeObjectURL(url);
  });
}
async function promote() {
  const sourceId = promoting.value?.id;

  if (!sourceId || !targetEnvironmentId.value)
    return;

  await run(async () => {
    const result = await graphql<{ promoteRevision: { revision: { number: number } } }>(`mutation($source:UUID!,$target:UUID!){promoteRevision(sourceRevisionId:$source,targetEnvironmentId:$target){revision{number}}}`, { source: sourceId, target: targetEnvironmentId.value });

    message.value = `Created draft revision ${result.promoteRevision.revision.number} in the target environment.`; promoting.value = null;
  });
}
async function run(action: () => Promise<void>) {
  error.value = ''; message.value = '';

  try { await action(); }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}watch(environmentId, load); onMounted(() => run(initial));
</script>

<style scoped>
.config-json {
  overflow: auto;
  max-height: 65vh;
  padding: 1rem;
  border-radius: 4px;
  background: var(--muted);
  font-size: 0.8rem;
}
</style>
