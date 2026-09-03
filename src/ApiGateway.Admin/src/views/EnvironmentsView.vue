<template>
  <div class="page-container">
    <div class="flex items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ $t('nav.system') }}
        </p><h1>{{ t('title') }}</h1><p class="page-lead">
          {{ t('lead') }}
        </p>
      </div><Button @click="openCreate">
        <Plus />{{ t('create') }}
      </Button>
    </div>
    <Alert v-if="error" variant="destructive" class="my-4">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Card class="mt-6 py-0">
      <Table>
        <TableHeader><TableRow><TableHead>{{ t('name') }}</TableHead><TableHead>{{ t('slug') }}</TableHead><TableHead>{{ t('publishing') }}</TableHead><TableHead>{{ t('revision') }}</TableHead><TableHead>{{ t('status') }}</TableHead><TableHead /></TableRow></TableHeader><TableBody>
          <TableRow v-for="item in items" :key="item.id">
            <TableCell>
              {{ item.displayName }}<div v-if="item.description" class="text-xs text-muted-foreground">
                {{ item.description }}
              </div>
            </TableCell><TableCell><code>{{ item.slug }}</code></TableCell><TableCell>
              <Badge :variant="item.publishingMode === 'STAGED' ? 'default' : 'secondary'">
                {{ item.publishingMode === 'STAGED' ? t('staged') : t('immediate') }}
              </Badge>
            </TableCell><TableCell>
              <code>{{ item.activeRevisionId || t('none') }}</code><div v-if="item.pendingRevisionId" class="text-xs text-primary">
                {{ t('pending') }}
              </div>
            </TableCell><TableCell>
              <Badge :variant="statusBadgeVariant(item)">
                {{ item.archivedAtUtc ? t('archived') : item.activeRevisionId ? t('active') : t('unconfigured') }}
              </Badge>
            </TableCell><TableCell>
              <div class="flex justify-end gap-1">
                <IconButton :label="t('edit')" @click="openEdit(item)">
                  <Pencil />
                </IconButton><IconButton v-if="item.archivedAtUtc" :label="t('restore')" @click="setArchived(item, false)">
                  <ArchiveRestore />
                </IconButton><IconButton v-else :label="t('archive')" @click="setArchived(item, true)">
                  <Archive />
                </IconButton>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>
    <Dialog v-model:open="dialog">
      <DialogContent size="lg">
        <DialogHeader><DialogTitle>{{ editing ? t('edit') : t('create') }}</DialogTitle></DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="environment-name">
              {{ t('displayName') }}
            </FieldLabel><Input id="environment-name" v-model="name" maxlength="128" />
          </Field>
          <Field :data-invalid="slug.length > 0 && !slugIsValid || undefined">
            <FieldLabel for="environment-slug">
              {{ t('slug') }}
            </FieldLabel><Input id="environment-slug" v-model="slug" maxlength="64" :readonly="!!editing" :aria-invalid="slug.length > 0 && !slugIsValid || undefined" /><FieldDescription>{{ t('slugHint') }}</FieldDescription><FieldError v-if="slug.length > 0 && !slugIsValid">
              {{ slugError }}
            </FieldError>
          </Field>
          <Field>
            <FieldLabel for="environment-description">
              {{ t('description') }}
            </FieldLabel><Textarea id="environment-description" v-model="description" />
          </Field>
          <Field>
            <FieldLabel for="environment-publishing">
              {{ t('publishingMode') }}
            </FieldLabel><Select v-model="publishingMode">
              <SelectTrigger id="environment-publishing">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="mode in publishingModes" :key="mode.value" :value="mode.value">
                  {{ mode.title }}
                </SelectItem>
              </SelectContent>
            </Select><FieldDescription>{{ publishingMode === 'STAGED' ? t('stagedHint') : t('immediateHint') }}</FieldDescription>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="dialog = false">
            {{ t('cancel') }}
          </Button><Button :disabled="!canSave" @click="save">
            {{ t('save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Badge, Button, Card, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, Field, FieldDescription, FieldError, FieldGroup, FieldLabel, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Textarea } from '@aditify/ui';
import { Archive, ArchiveRestore, CircleAlert, Pencil, Plus } from '@lucide/vue';
import { computed, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql } from '../api';
import IconButton from '../components/IconButton.vue';
import { confirmAction } from '../composables/confirmDialog';
import { loadEnvironments as loadEnvironmentContext, persistEnvironment, selectedEnvironmentId } from '../composables/environmentContext';

interface Environment { id: string; slug: string; displayName: string; description?: string; activeRevisionId?: string; pendingRevisionId?: string; archivedAtUtc?: string; concurrencyVersion: string; publishingMode: 'IMMEDIATE' | 'STAGED' }

const items = ref<Environment[]>([]); const dialog = ref(false); const editing = ref<Environment | null>(null); const error = ref(''); const slug = ref(''); const name = ref(''); const description = ref(''); const publishingMode = ref<'IMMEDIATE' | 'STAGED'>('IMMEDIATE');
const { t } = useI18n();
const publishingModes = computed(() => [{ title: t('immediate'), value: 'IMMEDIATE' }, { title: t('staged'), value: 'STAGED' }]);
const lastGeneratedSlug = ref('');
const slugIsValid = computed(() => /^[a-z0-9-]{2,64}$/.test(slug.value) && (editing.value !== null || !items.value.some(item => item.slug === slug.value)));
const canSave = computed(() => name.value.trim().length > 0 && slugIsValid.value);
const slugRules = computed(() => [
  (value: string) => /^[a-z0-9-]{2,64}$/.test(value) || t('slugRule'),
  (value: string) => editing.value !== null || !items.value.some(item => item.slug === value) || t('slugInUse'),
]);
const slugError = computed(() => slugRules.value.map(rule => rule(slug.value)).find(result => result !== true) || '');

function statusBadgeVariant(item: Environment): 'secondary' | 'success' | 'warning' { return item.archivedAtUtc ? 'secondary' : item.activeRevisionId ? 'success' : 'warning'; }

async function load() { await run(async () => { items.value = (await graphql<{ environments: Environment[] }>(`query{environments{id slug displayName description activeRevisionId pendingRevisionId publishingMode archivedAtUtc concurrencyVersion}}`)).environments; }); }
function openCreate() { editing.value = null; lastGeneratedSlug.value = ''; slug.value = ''; name.value = ''; description.value = ''; publishingMode.value = 'IMMEDIATE'; dialog.value = true; }
function openEdit(item: Environment) { editing.value = item; lastGeneratedSlug.value = ''; slug.value = item.slug; name.value = item.displayName; description.value = item.description || ''; publishingMode.value = item.publishingMode; dialog.value = true; }
function generateUniqueSlug(value: string) {
  const base = value.normalize('NFKD').replace(/\p{M}/gu, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 64).replace(/-+$/g, '');

  if (!base)
    return '';

  let candidate = base;
  let suffix = 2;

  while (items.value.some(item => item.slug === candidate)) {
    const ending = `-${suffix++}`;

    candidate = `${base.slice(0, 64 - ending.length).replace(/-+$/g, '')}${ending}`;
  }

  return candidate;
}
async function save() {
  await run(async () => {
    let createdEnvironmentId = '';

    if (editing.value) {
      const updated = (await graphql<{ updateEnvironment: { id: string; concurrencyVersion: string } }>(`mutation($id:UUID!,$version:UUID!,$name:String!,$description:String){updateEnvironment(id:$id,expectedVersion:$version,displayName:$name,description:$description){id concurrencyVersion}}`, { id: editing.value.id, version: editing.value.concurrencyVersion, name: name.value, description: description.value || null })).updateEnvironment;

      if (publishingMode.value !== editing.value.publishingMode)
        await graphql(`mutation($id:UUID!,$version:UUID!,$mode:ConfigurationPublishingMode!){setEnvironmentPublishingMode(id:$id,expectedVersion:$version,mode:$mode){id}}`, { id: editing.value.id, version: updated.concurrencyVersion, mode: publishingMode.value });
    }
    else {
      const created = (await graphql<{ createEnvironment: { id: string; concurrencyVersion: string } }>(`mutation($slug:String!,$name:String!,$description:String){createEnvironment(slug:$slug,displayName:$name,description:$description){id concurrencyVersion}}`, { slug: slug.value, name: name.value, description: description.value || null })).createEnvironment;

      createdEnvironmentId = created.id;

      if (publishingMode.value === 'STAGED')
        await graphql(`mutation($id:UUID!,$version:UUID!,$mode:ConfigurationPublishingMode!){setEnvironmentPublishingMode(id:$id,expectedVersion:$version,mode:$mode){id}}`, { id: created.id, version: created.concurrencyVersion, mode: publishingMode.value });
    }

    dialog.value = false; await load(); await loadEnvironmentContext();

    if (createdEnvironmentId) {
      selectedEnvironmentId.value = createdEnvironmentId;
      persistEnvironment();
    }
  });
}
async function setArchived(item: Environment, archived: boolean) {
  const verb = archived ? t('archive') : t('restore');

  if (!await confirmAction(t('archiveMessage', { action: verb, name: item.displayName }), { title: t('archiveTitle', { action: verb }), confirmText: verb, color: archived ? 'error' : 'primary' }))
    return;

  await run(async () => { await graphql(`mutation($id:UUID!,$version:UUID!,$archived:Boolean!){setEnvironmentArchived(id:$id,expectedVersion:$version,archived:$archived){id}}`, { id: item.id, version: item.concurrencyVersion, archived }); await load(); });
}
async function run(action: () => Promise<void>) {
  error.value = '';

  try { await action(); }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}
onMounted(load);
watch(name, (value) => {
  if (editing.value)
    return;

  const generated = generateUniqueSlug(value);

  if (!slug.value || slug.value === lastGeneratedSlug.value)
    slug.value = generated;

  lastGeneratedSlug.value = generated;
});
</script>

<i18n lang="json">
{
  "en": { "title": "Environments", "lead": "Separate configuration and publication history by deployment environment.", "create": "Create environment", "name": "Name", "slug": "Slug", "publishing": "Publishing", "publishingMode": "Configuration publishing", "immediate": "Immediate", "staged": "Staged", "immediateHint": "Each configuration change takes effect as soon as it is saved.", "stagedHint": "Configuration changes are collected and activated together when published.", "pending": "Unpublished changes", "revision": "Revision", "status": "Status", "none": "None", "archived": "Archived", "active": "Active", "unconfigured": "Unconfigured", "edit": "Edit environment", "restore": "Restore", "archive": "Archive", "displayName": "Display name", "slugHint": "Generated from the display name. You can change it before creating the environment.", "description": "Description", "cancel": "Cancel", "save": "Save", "slugRule": "Use 2 to 64 lowercase letters, digits, or hyphens.", "slugInUse": "This slug is already in use.", "archiveMessage": "{action} {name}?", "archiveTitle": "{action} environment?" },
  "sv": { "title": "Miljöer", "lead": "Separera konfiguration och publiceringshistorik per driftsmiljö.", "create": "Skapa miljö", "name": "Namn", "slug": "Slug", "publishing": "Publicering", "publishingMode": "Konfigurationspublicering", "immediate": "Omedelbar", "staged": "Stegvis", "immediateHint": "Varje konfigurationsändring börjar gälla direkt när den sparas.", "stagedHint": "Konfigurationsändringar samlas och aktiveras tillsammans vid publicering.", "pending": "Opublicerade ändringar", "revision": "Revision", "status": "Status", "none": "Ingen", "archived": "Arkiverad", "active": "Aktiv", "unconfigured": "Inte konfigurerad", "edit": "Redigera miljö", "restore": "Återställ", "archive": "Arkivera", "displayName": "Visningsnamn", "slugHint": "Skapas från visningsnamnet. Du kan ändra den innan miljön skapas.", "description": "Beskrivning", "cancel": "Avbryt", "save": "Spara", "slugRule": "Använd 2 till 64 gemena bokstäver, siffror eller bindestreck.", "slugInUse": "Denna slug används redan.", "archiveMessage": "{action} {name}?", "archiveTitle": "{action} miljö?" }
}
</i18n>
