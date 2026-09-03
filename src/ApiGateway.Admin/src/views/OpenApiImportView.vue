<template>
  <div class="page-container">
    <div>
      <h1>Import and OpenAPI</h1><p class="text-muted-foreground">
        Preview route generation before changing a draft. Import never publishes automatically.
      </p>
    </div>
    <Alert v-if="error" variant="destructive" class="mt-4">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Alert v-if="message" class="mt-4 border-emerald-500/40 text-emerald-700 dark:text-emerald-300">
      <CircleCheck /><AlertDescription>{{ message }}</AlertDescription>
    </Alert>
    <div class="mt-4 grid grid-cols-1 gap-4 md:grid-cols-3">
      <Field>
        <FieldLabel for="import-environment">
          Environment
        </FieldLabel><Select v-model="environmentId">
          <SelectTrigger id="import-environment">
            <SelectValue />
          </SelectTrigger><SelectContent>
            <SelectItem v-for="environment in environments" :key="environment.id" :value="environment.id">
              {{ environment.displayName }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel for="import-draft">
          Draft
        </FieldLabel><Select v-model="draftId">
          <SelectTrigger id="import-draft">
            <SelectValue />
          </SelectTrigger><SelectContent>
            <SelectItem v-for="draft in drafts" :key="draft.id" :value="draft.id">
              Revision {{ draft.number }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel for="import-cluster">
          Target cluster ID
        </FieldLabel><Input id="import-cluster" v-model="clusterId" />
      </Field>
      <Field>
        <FieldLabel for="import-prefix">
          Route ID prefix (optional)
        </FieldLabel><Input id="import-prefix" v-model="prefix" />
      </Field>
      <Field class="md:col-span-3">
        <FieldLabel for="import-source">
          OpenAPI JSON or YAML
        </FieldLabel><Textarea id="import-source" v-model="source" rows="16" class="font-mono text-sm" />
      </Field>
    </div>
    <div class="mt-4 flex justify-end">
      <Button :disabled="!draftId || !clusterId || !source" @click="preview">
        Preview import
      </Button>
    </div>
    <Alert v-for="issue in issues" :key="issue.code + issue.jsonPath" :variant="issue.severity === 'ERROR' ? 'destructive' : 'default'" :class="issue.severity === 'ERROR' ? 'mt-3' : 'mt-3 border-amber-500/40 text-amber-700 dark:text-amber-300'">
      <CircleAlert /><AlertDescription>{{ issue.code }} at {{ issue.jsonPath }}: {{ issue.message }}</AlertDescription>
    </Alert>
    <Card v-if="conflicts.length" class="mt-6">
      <CardHeader><CardTitle>Route conflicts</CardTitle></CardHeader><CardContent>
        <ItemGroup>
          <Item v-for="conflict in conflicts" :key="conflict.routeId">
            <ItemContent><ItemTitle>{{ conflict.routeId }}</ItemTitle><ItemDescription>{{ conflict.existingPath }} → {{ conflict.proposedPath }}</ItemDescription></ItemContent><ItemActions>
              <Select v-model="resolutions[conflict.routeId]">
                <SelectTrigger class="w-48" aria-label="Conflict resolution">
                  <SelectValue />
                </SelectTrigger><SelectContent>
                  <SelectItem value="replace">
                    Replace existing
                  </SelectItem><SelectItem value="skip">
                    Keep existing
                  </SelectItem>
                </SelectContent>
              </Select>
            </ItemActions>
          </Item>
        </ItemGroup>
      </CardContent>
    </Card>
    <Card v-if="routes.length" class="mt-6 py-0">
      <CardHeader class="border-b py-4">
        <CardTitle>Generated routes</CardTitle>
      </CardHeader>
      <Table>
        <TableHeader><TableRow><TableHead>Route ID</TableHead><TableHead>Methods</TableHead><TableHead>Path</TableHead><TableHead>Cluster</TableHead></TableRow></TableHeader><TableBody>
          <TableRow v-for="route in routes" :key="route.id">
            <TableCell><code>{{ route.id }}</code></TableCell><TableCell>{{ route.match.methods.join(', ') }}</TableCell><TableCell><code>{{ route.match.path }}</code></TableCell><TableCell>{{ route.clusterId }}</TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <CardFooter class="justify-end">
        <Button :disabled="issues.some(x => x.severity === 'ERROR')" @click="apply">
          Apply to draft
        </Button>
      </CardFooter>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Button, Card, CardContent, CardFooter, CardHeader, CardTitle, Field, FieldLabel, Input, Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemTitle, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Textarea } from '@aditify/ui';
import { CircleAlert, CircleCheck } from '@lucide/vue';
import { onMounted, ref, watch } from 'vue';
import { graphql } from '../api';
import { confirmAction } from '../composables/confirmDialog';

interface Environment { id: string; displayName: string }interface Revision { id: string; number: number; concurrencyVersion: string } interface Route { id: string; clusterId: string; match: { path: string; methods: string[] } } interface Issue { severity: string; code: string; jsonPath: string; message: string } interface Conflict { routeId: string; existingPath: string; proposedPath: string }

const environments = ref<Environment[]>([]); const environmentId = ref(''); const drafts = ref<Revision[]>([]); const draftId = ref(''); const clusterId = ref(''); const prefix = ref(''); const source = ref(''); const routes = ref<Route[]>([]); const conflicts = ref<Conflict[]>([]); const resolutions = ref<Record<string, string>>({}); const issues = ref<Issue[]>([]); const token = ref(''); const previewVersion = ref(''); const error = ref(''); const message = ref('');

async function load() { environments.value = (await graphql<{ environments: Environment[] }>(`query{environments{id displayName}}`)).environments; environmentId.value = environments.value[0]?.id || ''; }
async function loadDrafts() {
  if (!environmentId.value)
    return;

  drafts.value = (await graphql<{ revisions: Revision[] }>(`query($id:UUID!){revisions(environmentId:$id,state:DRAFT){id number concurrencyVersion}}`, { id: environmentId.value })).revisions; draftId.value = drafts.value[0]?.id || ''; token.value = ''; routes.value = [];
}
async function preview() {
  const draft = drafts.value.find(x => x.id === draftId.value);

  if (!draft)
    return;

  await run(async () => {
    const result = await graphql<{ previewOpenApiImport: { token: string; routes: Route[]; conflicts: Conflict[]; issues: Issue[] } }>(`mutation($draft:UUID!,$version:UUID!,$source:String!,$cluster:String!,$prefix:String){previewOpenApiImport(draftId:$draft,expectedVersion:$version,source:$source,clusterId:$cluster,routeIdPrefix:$prefix){token routes{id clusterId match{path methods}} conflicts{routeId existingPath proposedPath} issues{severity code jsonPath message}}}`, { draft: draft.id, version: draft.concurrencyVersion, source: source.value, cluster: clusterId.value, prefix: prefix.value || null });

    token.value = result.previewOpenApiImport.token; previewVersion.value = draft.concurrencyVersion; routes.value = result.previewOpenApiImport.routes; conflicts.value = result.previewOpenApiImport.conflicts; resolutions.value = Object.fromEntries(conflicts.value.map(x => [x.routeId, 'replace'])); issues.value = result.previewOpenApiImport.issues;
  });
}
async function apply() {
  if (!await confirmAction(`Apply ${routes.value.length} reviewed routes to this draft?`, { title: 'Apply OpenAPI routes?', confirmText: 'Apply' }))
    return;

  await run(async () => { await graphql(`mutation($token:String!,$version:UUID!,$resolutions:[OpenApiConflictResolutionInput!]!){applyOpenApiImport(previewToken:$token,expectedVersion:$version,resolutions:$resolutions){id}}`, { token: token.value, version: previewVersion.value, resolutions: Object.entries(resolutions.value).map(([routeId, action]) => ({ routeId, action })) }); message.value = 'Reviewed routes were added to the draft.'; token.value = ''; routes.value = []; conflicts.value = []; await loadDrafts(); });
}
async function run(action: () => Promise<void>) {
  error.value = ''; message.value = '';

  try { await action(); }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}watch(environmentId, () => run(loadDrafts)); onMounted(() => run(load));
</script>
