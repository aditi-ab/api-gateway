<template>
  <div class="page">
    <div>
      <p class="eyebrow">
        {{ t('eyebrow') }}
      </p>
      <h1>{{ t('title') }}</h1>
      <p class="page-lead">
        {{ t('lead') }}
      </p>
    </div>
    <Alert v-if="error" variant="destructive" class="mt-5">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Alert v-if="message" class="mt-5 border-emerald-500/40 text-emerald-700 dark:text-emerald-300">
      <CircleCheck /><AlertDescription>{{ message }}</AlertDescription>
    </Alert>
    <EnvironmentRequiredAlert v-if="!selectedEnvironmentId" />
    <ol v-if="history.length" class="activity-timeline">
      <li v-for="entry in history" :key="entry.id" class="activity-entry">
        <span class="activity-dot" :class="entry.revertsRevisionId ? 'bg-amber-400' : 'bg-primary'" />
        <Card class="activity-card py-0">
          <CardContent class="p-3">
            <div class="activity-card-header flex justify-between gap-3">
              <div class="activity-summary flex flex-wrap gap-2 items-center">
                <strong>{{ entry.changeSummary || entry.comment || t('configurationChange', { number: entry.number }) }}</strong><Badge v-if="entry.id === activeRevisionId" variant="success">
                  {{ t('current') }}
                </Badge><Badge v-if="entry.revertsRevisionId" variant="warning">
                  <Undo2 />{{ t('revert') }}
                </Badge><Badge v-if="entry.changedResourceType" variant="secondary">
                  {{ entry.changedResourceType }}
                </Badge><span v-if="resourceName(entry)" class="text-sm font-medium">{{ resourceName(entry) }}</span><span class="text-xs text-muted-foreground">{{ t('byActor', { date: formatDateTime(entry.publishedAtUtc || entry.createdAtUtc), actor: entry.publishedBy || entry.createdBy }) }}</span>
              </div><div class="activity-actions flex items-center gap-2">
                <Popover>
                  <PopoverTrigger as-child>
                    <span class="inline-flex">
                      <Tooltip><TooltipTrigger as-child>
                        <Button variant="secondary" size="icon-sm" :aria-label="t('advancedDetails')">
                          <Info />
                        </Button>
                      </TooltipTrigger><TooltipContent>{{ t('advancedDetails') }}</TooltipContent></Tooltip>
                    </span>
                  </PopoverTrigger><PopoverContent align="end" class="w-[22rem] overflow-x-hidden">
                    <dl class="activity-details-menu">
                      <div>
                        <dt>{{ t('revision') }}</dt><dd>{{ entry.number }}</dd>
                      </div><div>
                        <dt>{{ t('configurationHash') }}</dt><dd class="mono activity-hash">
                          {{ entry.contentHash }}
                        </dd>
                      </div>
                    </dl>
                  </PopoverContent>
                </Popover><IconButton v-if="entry.changedResourceId && entry.id !== activeRevisionId" variant="secondary" :disabled="reverting === entry.id" :label="t('revert')" @click="revert(entry)">
                  <Spinner v-if="reverting === entry.id" /><Undo2 v-else />
                </IconButton>
              </div>
            </div>
          </CardContent>
        </Card>
      </li>
    </ol>
    <Empty v-else-if="selectedEnvironmentId" class="mt-8">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <History />
        </EmptyMedia><EmptyTitle>{{ t('emptyTitle') }}</EmptyTitle><EmptyDescription>{{ t('emptyText') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Badge, Button, Card, CardContent, Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle, Popover, PopoverContent, PopoverTrigger, Spinner, Tooltip, TooltipContent, TooltipTrigger } from '@aditify/ui';
import { CircleAlert, CircleCheck, History, Info, Undo2 } from '@lucide/vue';
import { computed, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql } from '../api';
import EnvironmentRequiredAlert from '../components/EnvironmentRequiredAlert.vue';
import IconButton from '../components/IconButton.vue';
import { confirmAction } from '../composables/confirmDialog';
import { editableRevisionId, environments, loadEnvironments, selectedEnvironmentId } from '../composables/environmentContext';
import { formatDateTime } from '../utils/dateTime';

interface Entry { id: string; number: number; contentHash: string; comment?: string; createdBy: string; createdAtUtc: string; publishedBy?: string; publishedAtUtc?: string; parentRevisionId?: string; changeSummary?: string; changedResourceType?: string; changedResourceId?: string; revertsRevisionId?: string }
interface RouteSummary { id: string; name: string }

const history = ref<Entry[]>([]); const routes = ref<RouteSummary[]>([]); const error = ref(''); const message = ref(''); const reverting = ref('');
const { t } = useI18n();
const activeRevisionId = computed(() => environments.value.find(x => x.id === selectedEnvironmentId.value)?.activeRevisionId || '');

async function load() {
  if (!selectedEnvironmentId.value)
    return;

  try {
    const data = await graphql<{ configurationHistory: Entry[]; routes: RouteSummary[] }>(`query Activity($id:UUID!){configurationHistory(environmentId:$id){id number contentHash comment createdBy createdAtUtc publishedBy publishedAtUtc parentRevisionId changeSummary changedResourceType changedResourceId revertsRevisionId} routes(environmentId:$id){id name}}`, { id: selectedEnvironmentId.value });

    history.value = data.configurationHistory;
    routes.value = data.routes;
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}
function resourceName(entry: Entry) {
  if (entry.changedResourceType?.toLowerCase() !== 'route' || !entry.changedResourceId)
    return '';

  return routes.value.find(route => route.id === entry.changedResourceId)?.name || '';
}
async function revert(entry: Entry) {
  if (!activeRevisionId.value || !await confirmAction(t('revertMessage', { change: entry.changeSummary || entry.comment }), { title: t('revertTitle'), confirmText: t('revert') }))
    return;

  reverting.value = entry.id; error.value = '';

  try { await graphql(`mutation Revert($environmentId:UUID!,$changeId:UUID!,$version:UUID!){revertConfigurationChange(environmentId:$environmentId,changeId:$changeId,expectedConfigurationVersion:$version){revision{id}}}`, { environmentId: selectedEnvironmentId.value, changeId: entry.id, version: editableRevisionId.value }); message.value = t('revertSuccess'); await loadEnvironments(); await load(); }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
  finally { reverting.value = ''; }
}
watch(selectedEnvironmentId, load); onMounted(async () => {
  await loadEnvironments();
  await load();
});
</script>

<i18n lang="json">
{
  "en": { "eyebrow": "Configuration history", "title": "Activity", "lead": "Every successful change is activated automatically and kept as an immutable history entry.", "configurationChange": "Configuration change {number}", "byActor": "{date} by {actor}", "current": "Current", "revert": "Revert", "advancedDetails": "Advanced details", "revision": "Revision", "configurationHash": "Configuration hash", "emptyTitle": "No configuration activity", "emptyText": "Create a route to start the history.", "revertMessage": "Revert “{change}” while preserving later unrelated changes?", "revertTitle": "Revert configuration change?", "revertSuccess": "The change was reverted and a new history entry was activated." },
  "sv": { "eyebrow": "Konfigurationshistorik", "title": "Aktivitet", "lead": "Varje lyckad ändring aktiveras automatiskt och sparas som en oföränderlig historikpost.", "configurationChange": "Konfigurationsändring {number}", "byActor": "{date} av {actor}", "current": "Aktuell", "revert": "Återställ", "advancedDetails": "Avancerade detaljer", "revision": "Revision", "configurationHash": "Konfigurationshash", "emptyTitle": "Ingen konfigurationsaktivitet", "emptyText": "Skapa en route för att påbörja historiken.", "revertMessage": "Återställ “{change}” och behåll senare orelaterade ändringar?", "revertTitle": "Återställ konfigurationsändring?", "revertSuccess": "Ändringen återställdes och en ny historikpost aktiverades." }
}
</i18n>

<style scoped>
.activity-timeline {
  position: relative;
  display: grid;
  width: 100%;
  margin-top: 2rem;
  gap: 0.625rem;
  padding: 0 0 0 2.5rem;
  list-style: none;
}

.activity-timeline::before {
  position: absolute;
  top: 0.5rem;
  bottom: 0.5rem;
  left: 0.75rem;
  width: 1px;
  content: '';
  background: var(--border);
}

.activity-entry {
  position: relative;
}

.activity-dot {
  position: absolute;
  top: 1.1rem;
  left: -2.05rem;
  width: 0.7rem;
  height: 0.7rem;
  border-radius: 999px;
}

.activity-card {
  width: 100%;
}

.activity-card-header {
  align-items: center;
}

.activity-summary {
  min-width: 0;
}

.activity-actions {
  flex: 0 0 auto;
}

.activity-details-menu {
  display: grid;
  gap: 0.875rem;
  width: 100%;
  min-width: 0;
  margin: 0;
  padding: 0.75rem;
}

.activity-details-menu dt {
  font-weight: 600;
}

.activity-details-menu dd {
  margin: 0.2rem 0 0;
  color: var(--muted-foreground);
  font-size: 0.875rem;
}

.activity-hash {
  min-width: 0;
  overflow-wrap: anywhere;
  word-break: break-word;
}

@media (max-width: 600px) {
  .activity-card-header {
    align-items: flex-start;
    flex-direction: column;
  }

  .activity-actions {
    align-self: stretch;
    justify-content: flex-end;
  }
}
</style>
