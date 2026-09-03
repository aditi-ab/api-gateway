<template>
  <aside v-if="environment?.publishingMode === 'STAGED' && environment.pendingRevisionId && pending" class="pending-drawer">
    <Collapsible v-model:open="reviewOpen">
      <Card class="overflow-hidden py-0 shadow-lg">
        <div class="pending-drawer-summary">
          <FilePenLine class="size-5 text-primary" />
          <div class="min-w-0">
            <div class="text-base font-medium">
              {{ t('title', { count: pending.changes.length }) }}
            </div>
            <div class="text-sm text-muted-foreground">
              {{ t('message') }}
            </div>
          </div>
          <div class="pending-drawer-actions">
            <Button variant="secondary" :disabled="loading" :aria-expanded="reviewOpen" @click="reviewOpen = !reviewOpen">
              <Spinner v-if="loading" /><ChevronDown v-else :class="{ 'rotate-180': reviewOpen }" class="transition-transform" />{{ t('review') }}
            </Button>
            <Button :disabled="publishing || !pending.validation.isValid" @click="publishChanges">
              <Spinner v-if="publishing" /><Upload v-else />{{ t('publish') }}
            </Button>
          </div>
        </div>
        <CollapsibleContent>
          <div class="pending-drawer-body border-t">
            <h2 class="text-lg font-semibold">
              {{ t('reviewTitle') }}
            </h2>
            <p class="mb-4 text-muted-foreground">
              {{ t('reviewLead') }}
            </p>
            <Alert v-if="error" variant="destructive" class="mb-4">
              <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
            </Alert>
            <ItemGroup v-if="pending?.changes.length" class="rounded-lg border">
              <Item v-for="(change, index) in pending.changes" :key="`${change.kind}-${change.resourceId}-${index}`" size="sm">
                <ItemMedia variant="icon">
                  <FilePenLine />
                </ItemMedia><ItemContent><ItemTitle>{{ change.summary }}</ItemTitle><ItemDescription>{{ change.kind }}</ItemDescription></ItemContent>
              </Item>
            </ItemGroup>
            <Alert v-else>
              <Info /><AlertDescription>{{ t('empty') }}</AlertDescription>
            </Alert>
            <Alert v-if="pending && !pending.validation.isValid" variant="destructive" class="mt-4">
              <CircleAlert /><div>
                <AlertTitle>{{ t('validationTitle') }}</AlertTitle><AlertDescription>
                  <div v-for="issue in pending.validation.issues" :key="`${issue.code}-${issue.jsonPath}`">
                    {{ issue.message }}
                  </div>
                </AlertDescription>
              </div>
            </Alert>
            <Field class="mt-4">
              <FieldLabel for="publication-comment">
                {{ t('comment') }}
              </FieldLabel><Textarea id="publication-comment" v-model="comment" rows="2" /><FieldDescription>{{ t('commentHint') }}</FieldDescription>
            </Field>
          </div>
          <div class="pending-drawer-footer border-t">
            <Button variant="destructive" :disabled="discarding" @click="discardChanges">
              <Spinner v-if="discarding" /><Trash2 v-else />{{ t('discard') }}
            </Button>
            <div class="flex justify-end gap-2">
              <Button variant="outline" @click="reviewOpen = false">
                {{ t('cancel') }}
              </Button><Button :disabled="publishing || !pending.validation.isValid" @click="publishChanges">
                <Spinner v-if="publishing" />{{ t('publish') }}
              </Button>
            </div>
          </div>
        </CollapsibleContent>
      </Card>
    </Collapsible>
  </aside>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, AlertTitle, Button, Card, Collapsible, CollapsibleContent, Field, FieldDescription, FieldLabel, Item, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle, Spinner, Textarea } from '@aditify/ui';
import { ChevronDown, CircleAlert, FilePenLine, Info, Trash2, Upload } from '@lucide/vue';
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql } from '../api';
import { confirmAction } from '../composables/confirmDialog';
import { selectedEnvironment as environment, loadEnvironments, refreshConfigurationViews, selectedEnvironmentId } from '../composables/environmentContext';

interface PendingChange { kind: string; resourceId?: string; summary: string }
interface PendingConfiguration { revisionId: string; version: string; changes: PendingChange[]; validation: { isValid: boolean; issues: Array<{ code: string; jsonPath: string; message: string }> } }

const { t } = useI18n();
const pending = ref<PendingConfiguration | null>(null); const loading = ref(false); const publishing = ref(false); const discarding = ref(false); const reviewOpen = ref(false); const comment = ref(''); const error = ref('');

async function load() {
  pending.value = null; error.value = '';

  if (!selectedEnvironmentId.value || !environment.value?.pendingRevisionId)
    return;

  loading.value = true;

  try { pending.value = (await graphql<{ pendingConfiguration: PendingConfiguration | null }>(`query PendingConfiguration($environmentId:UUID!){pendingConfiguration(environmentId:$environmentId){revisionId version changes{kind resourceId summary} validation{isValid issues{code jsonPath message}}}}`, { environmentId: selectedEnvironmentId.value })).pendingConfiguration; }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
  finally { loading.value = false; }
}
async function publishChanges() {
  if (!pending.value)
    return;

  publishing.value = true; error.value = '';

  try {
    await graphql(`mutation PublishPending($environmentId:UUID!,$version:UUID!,$comment:String){publishPendingConfiguration(environmentId:$environmentId,expectedVersion:$version,comment:$comment){id}}`, { environmentId: selectedEnvironmentId.value, version: pending.value.version, comment: comment.value || null });
    reviewOpen.value = false; comment.value = ''; await loadEnvironments(); refreshConfigurationViews(); await load();
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); reviewOpen.value = true; }
  finally { publishing.value = false; }
}
async function discardChanges() {
  if (!pending.value || !await confirmAction(t('discardMessage'), { title: t('discardTitle'), confirmText: t('discard'), color: 'error' }))
    return;

  discarding.value = true; error.value = '';

  try {
    await graphql(`mutation DiscardPending($environmentId:UUID!,$version:UUID!){discardPendingConfiguration(environmentId:$environmentId,expectedVersion:$version)}`, { environmentId: selectedEnvironmentId.value, version: pending.value.version });
    reviewOpen.value = false; comment.value = ''; await loadEnvironments(); refreshConfigurationViews(); await load();
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
  finally { discarding.value = false; }
}
watch([selectedEnvironmentId, () => environment.value?.pendingRevisionId, () => environment.value?.concurrencyVersion], () => void load(), { immediate: true });
</script>

<i18n lang="json">
{
  "en": { "title": "{count} unpublished change | {count} unpublished changes", "message": "Configuration changes are saved together and take effect when you publish them.", "review": "Review", "publish": "Publish", "reviewTitle": "Review unpublished changes", "reviewLead": "Publishing activates all listed changes as one configuration revision.", "empty": "No differences were found.", "validationTitle": "Resolve these issues before publishing", "comment": "Publication note", "commentHint": "Optional. Describe why this configuration is being published.", "discard": "Discard changes", "discardTitle": "Discard unpublished changes?", "discardMessage": "This permanently removes every unpublished configuration change for this environment.", "cancel": "Cancel" },
  "sv": { "title": "{count} opublicerad ändring | {count} opublicerade ändringar", "message": "Konfigurationsändringar sparas tillsammans och börjar gälla när du publicerar dem.", "review": "Granska", "publish": "Publicera", "reviewTitle": "Granska opublicerade ändringar", "reviewLead": "Vid publicering aktiveras alla listade ändringar som en konfigurationsrevision.", "empty": "Inga skillnader hittades.", "validationTitle": "Lös problemen före publicering", "comment": "Publiceringsanteckning", "commentHint": "Valfritt. Beskriv varför konfigurationen publiceras.", "discard": "Kassera ändringar", "discardTitle": "Kassera opublicerade ändringar?", "discardMessage": "Detta tar permanent bort alla opublicerade konfigurationsändringar för miljön.", "cancel": "Avbryt" }
}
</i18n>

<style scoped>
.pending-drawer {
  position: fixed;
  right: clamp(1rem, 3vw, 3rem);
  bottom: 1rem;
  left: clamp(1rem, 3vw, 3rem);
  z-index: 40;
  max-width: 1600px;
  margin-inline: auto;
}

.pending-drawer-summary {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  gap: 12px;
  align-items: center;
  padding: 12px 16px;
}

.pending-drawer-body {
  max-height: min(55vh, 34rem);
  padding: 1rem;
  overflow-y: auto;
  overscroll-behavior: contain;
}

.pending-drawer-actions,
.pending-drawer-footer {
  display: flex;
  gap: 8px;
  align-items: center;
}

.pending-drawer-footer {
  justify-content: space-between;
  padding: 0.75rem 1rem;
  background: var(--muted);
}

@media (max-width: 700px) {
  .pending-drawer-summary {
    grid-template-columns: auto minmax(0, 1fr);
  }

  .pending-drawer-actions {
    grid-column: 2;
    flex-wrap: wrap;
  }
}

@media (max-width: 440px) {
  .pending-drawer-actions,
  .pending-drawer-footer {
    display: grid;
    grid-template-columns: 1fr;
  }
}
</style>
