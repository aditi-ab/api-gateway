<template>
  <div class="page">
    <div class="flex items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ t('eyebrow') }}
        </p><h1>{{ t('title') }}</h1><p class="page-lead">
          {{ t('lead') }}
        </p>
      </div><Button variant="secondary" :disabled="loading" @click="load">
        <Spinner v-if="loading" /><RefreshCw v-else />{{ t('refresh') }}
      </Button>
    </div>

    <Alert v-if="error" variant="destructive" class="mt-5 mb-5">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription><AlertAction>
        <IconButton :label="t('close')" @click="error = ''">
          <X />
        </IconButton>
      </AlertAction>
    </Alert><EnvironmentRequiredAlert v-else-if="!selectedEnvironmentId" class="mb-5" />

    <div class="section-heading mb-3">
      {{ t('posture') }}
    </div>
    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <RouterLink to="/routes">
        <Card class="posture-card h-full p-5">
          <div class="flex items-center gap-2 text-muted-foreground">
            <Route />{{ t('routes') }}
          </div><div class="metric mt-3">
            {{ routes.length }}
          </div><div class="text-xs mt-2">
            {{ t('routesLead') }}
          </div>
        </Card>
      </RouterLink><RouterLink to="/routes">
        <Card class="posture-card h-full p-5">
          <div class="flex items-center gap-2 text-muted-foreground">
            <CircleCheck class="text-emerald-500" />{{ t('onlineRoutes') }}
          </div><div class="metric mt-3">
            {{ onlineRouteCount }}
          </div><div class="text-xs mt-2">
            {{ t('onlineRoutesLead') }}
          </div>
        </Card>
      </RouterLink><RouterLink to="/instances">
        <Card class="posture-card h-full p-5">
          <div class="flex items-center gap-2 text-muted-foreground">
            <Server />{{ t('instances') }}
          </div><div class="metric mt-3">
            {{ diagnostics?.instanceCount || 0 }}
          </div><div class="text-xs mt-2">
            {{ t('instanceLead', { healthy: diagnostics?.healthyCount || 0 }) }}
          </div>
        </Card>
      </RouterLink><RouterLink to="/instances">
        <Card class="posture-card h-full p-5">
          <div class="flex items-center gap-2 text-muted-foreground">
            <CircleAlert v-if="attentionCount" class="text-amber-500" /><CircleCheck v-else class="text-emerald-500" />{{ t('needsAttention') }}
          </div><div class="metric mt-3">
            {{ attentionCount }}
          </div><div class="text-xs mt-2">
            {{ t('attentionLead', { unhealthy: unhealthyCount, drifted: diagnostics?.driftedCount || 0, stale: diagnostics?.staleCount || 0 }) }}
          </div>
        </Card>
      </RouterLink>
    </div>

    <div class="section-heading mt-6 mb-3">
      {{ t('serviceHealth') }}
    </div>
    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <Card class="h-full p-5">
        <div class="text-muted-foreground">
          {{ t('configuration') }}
        </div><div class="health-value mt-3">
          {{ activeRevisionId ? t('active') : t('notConfigured') }}
        </div><Badge :variant="activeRevisionId ? 'success' : 'warning'" class="mt-2">
          {{ activeRevisionId ? t('published') : t('attention') }}
        </Badge>
      </Card><Card class="h-full p-5">
        <div class="text-muted-foreground">
          {{ t('instanceHealth') }}
        </div><div class="health-value mt-3">
          {{ diagnostics?.healthyCount || 0 }} / {{ diagnostics?.instanceCount || 0 }}
        </div><div class="text-xs mt-2">
          {{ allInstancesHealthy ? t('allHealthy') : t('healthAttention') }}
        </div>
      </Card><Card class="h-full p-5">
        <div class="text-muted-foreground">
          {{ t('convergence') }}
        </div><div class="health-value mt-3">
          {{ diagnostics?.driftedCount ? t('driftDetected') : t('converged') }}
        </div><div class="text-xs mt-2">
          {{ t('convergenceLead', { drifted: diagnostics?.driftedCount || 0, stale: diagnostics?.staleCount || 0 }) }}
        </div>
      </Card><Card class="h-full p-5">
        <div class="text-muted-foreground">
          {{ t('version') }}
        </div><div class="health-value mt-3">
          {{ systemStatus?.version || t('unavailable') }}
        </div><div class="text-xs mt-2">
          {{ t('checked') }} {{ systemStatus?.checkedAtUtc ? formatDateTime(systemStatus.checkedAtUtc) : t('unavailable') }}
        </div>
      </Card>
    </div>

    <Card class="data-panel mt-6 py-0">
      <div class="dashboard-card-header flex flex-wrap items-center gap-3 p-5">
        <div>
          <div class="section-heading">
            {{ t('recentActivity') }}
          </div><div class="text-xs text-muted-foreground">
            {{ t('recentActivityLead') }}
          </div>
        </div><Button as-child variant="secondary" class="ml-auto">
          <RouterLink to="/activity">
            {{ t('viewActivity') }}<ArrowRight />
          </RouterLink>
        </Button>
      </div><Table v-if="recentHistory.length" class="dashboard-activity-table">
        <TableHeader><TableRow><TableHead>{{ t('time') }}</TableHead><TableHead>{{ t('change') }}</TableHead><TableHead>{{ t('actor') }}</TableHead><TableHead>{{ t('resource') }}</TableHead></TableRow></TableHeader><TableBody>
          <TableRow v-for="entry in recentHistory" :key="entry.id">
            <TableCell class="activity-time">
              {{ formatDateTime(entry.publishedAtUtc || entry.createdAtUtc) }}
            </TableCell><TableCell><strong>{{ entry.changeSummary || entry.comment || t('configurationChange', { number: entry.number }) }}</strong></TableCell><TableCell>{{ entry.publishedBy || entry.createdBy }}</TableCell><TableCell>
              <div class="flex items-center gap-2">
                <Badge v-if="entry.changedResourceType" variant="secondary">
                  {{ entry.changedResourceType }}
                </Badge><span v-if="resourceName(entry)" class="font-medium">{{ resourceName(entry) }}</span>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table><Empty v-else-if="!loading">
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <History />
          </EmptyMedia><EmptyTitle>{{ t('emptyActivity') }}</EmptyTitle><EmptyDescription>{{ t('emptyActivityLead') }}</EmptyDescription>
        </EmptyHeader><EmptyContent>
          <Button v-if="!routes.length" as-child>
            <RouterLink to="/routes">
              <Plus />{{ t('addRoute') }}
            </RouterLink>
          </Button>
        </EmptyContent>
      </Empty>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertAction, AlertDescription, Badge, Button, Card, Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle, Spinner, Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@aditify/ui';
import { ArrowRight, CircleAlert, CircleCheck, History, Plus, RefreshCw, Route, Server, X } from '@lucide/vue';
import { computed, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql } from '../api';
import EnvironmentRequiredAlert from '../components/EnvironmentRequiredAlert.vue';
import IconButton from '../components/IconButton.vue';
import { environments, loadEnvironments, selectedEnvironmentId } from '../composables/environmentContext';
import { formatDateTime } from '../utils/dateTime';

interface RouteSummary { id: string; name: string; enabled: boolean; operations: { state: string } }
interface Diagnostics { instanceCount: number; healthyCount: number; staleCount: number; driftedCount: number }
interface HistoryEntry { id: string; number: number; comment?: string; createdBy: string; createdAtUtc: string; publishedBy?: string; publishedAtUtc?: string; changeSummary?: string; changedResourceType?: string; changedResourceId?: string }
interface SystemStatus { version: string; checkedAtUtc: string }

const routes = ref<RouteSummary[]>([]);
const diagnostics = ref<Diagnostics>();
const history = ref<HistoryEntry[]>([]);
const systemStatus = ref<SystemStatus>();
const error = ref('');
const loading = ref(false);
const { t } = useI18n();
const activeRevisionId = computed(() => environments.value.find(x => x.id === selectedEnvironmentId.value)?.activeRevisionId);
const onlineRouteCount = computed(() => routes.value.filter(route => route.enabled && route.operations.state === 'ONLINE').length);
const unhealthyCount = computed(() => Math.max((diagnostics.value?.instanceCount || 0) - (diagnostics.value?.healthyCount || 0), 0));
const attentionCount = computed(() => unhealthyCount.value + (diagnostics.value?.driftedCount || 0) + (diagnostics.value?.staleCount || 0));
const allInstancesHealthy = computed(() => !!diagnostics.value?.instanceCount && diagnostics.value.healthyCount === diagnostics.value.instanceCount);
const recentHistory = computed(() => history.value.slice(0, 6));

function resourceName(entry: HistoryEntry) {
  if (entry.changedResourceType?.toLowerCase() !== 'route' || !entry.changedResourceId)
    return '';

  return routes.value.find(route => route.id === entry.changedResourceId)?.name || '';
}

async function load() {
  if (!selectedEnvironmentId.value)
    return;

  loading.value = true;
  error.value = '';

  try {
    const data = await graphql<{ routes: RouteSummary[]; gatewayDiagnostics: Diagnostics; configurationHistory: HistoryEntry[]; systemStatus: SystemStatus }>(`query Overview($id:UUID!){routes(environmentId:$id){id name enabled operations{state}} gatewayDiagnostics(environmentId:$id){instanceCount healthyCount staleCount driftedCount} configurationHistory(environmentId:$id){id number comment createdBy createdAtUtc publishedBy publishedAtUtc changeSummary changedResourceType changedResourceId} systemStatus{version checkedAtUtc}}`, { id: selectedEnvironmentId.value });

    routes.value = data.routes;
    diagnostics.value = data.gatewayDiagnostics;
    history.value = data.configurationHistory;
    systemStatus.value = data.systemStatus;
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
  finally { loading.value = false; }
}

watch(selectedEnvironmentId, load);
onMounted(async () => {
  if (!selectedEnvironmentId.value)
    await loadEnvironments();

  await load();
});
</script>

<i18n lang="json">
{
  "en": {
    "eyebrow": "Management console", "title": "Overview", "lead": "Configuration and runtime status for the selected gateway environment.", "refresh": "Refresh", "posture": "Gateway posture",
    "routes": "Routes", "routesLead": "Configured route definitions", "onlineRoutes": "Online routes", "onlineRoutesLead": "Enabled and accepting traffic", "instances": "Gateway instances", "instanceLead": "{healthy} currently healthy", "needsAttention": "Needs attention", "attentionLead": "{unhealthy} unhealthy · {drifted} drifted · {stale} stale",
    "serviceHealth": "Service health", "configuration": "Configuration", "active": "Active", "notConfigured": "Not configured", "published": "Published", "attention": "Attention", "instanceHealth": "Instance health", "allHealthy": "All reporting instances are healthy.", "healthAttention": "One or more instances need attention.", "convergence": "Configuration convergence", "converged": "Converged", "driftDetected": "Drift detected", "convergenceLead": "{drifted} drifted and {stale} stale instances", "version": "Gateway version", "unavailable": "Not available", "checked": "Checked",
    "recentActivity": "Recent activity", "recentActivityLead": "The latest configuration changes for this environment.", "viewActivity": "View full activity", "time": "Time", "change": "Change", "actor": "Actor", "resource": "Resource", "configurationChange": "Configuration change {number}", "emptyActivity": "No configuration activity", "emptyActivityLead": "Create a route to begin the environment history.", "addRoute": "Add a route"
  },
  "sv": {
    "eyebrow": "Administrationskonsol", "title": "Översikt", "lead": "Konfigurations- och körstatus för den valda gatewaymiljön.", "refresh": "Uppdatera", "posture": "Gatewaystatus",
    "routes": "Routes", "routesLead": "Konfigurerade routedefinitioner", "onlineRoutes": "Online-routes", "onlineRoutesLead": "Aktiverade och tar emot trafik", "instances": "Gatewayinstanser", "instanceLead": "{healthy} fungerar för närvarande", "needsAttention": "Kräver åtgärd", "attentionLead": "{unhealthy} felaktiga · {drifted} avvikande · {stale} inaktuella",
    "serviceHealth": "Tjänstehälsa", "configuration": "Konfiguration", "active": "Aktiv", "notConfigured": "Inte konfigurerad", "published": "Publicerad", "attention": "Åtgärd krävs", "instanceHealth": "Instanshälsa", "allHealthy": "Alla rapporterande instanser fungerar.", "healthAttention": "En eller flera instanser kräver åtgärd.", "convergence": "Konfigurationskonvergens", "converged": "Konvergerad", "driftDetected": "Avvikelse upptäckt", "convergenceLead": "{drifted} avvikande och {stale} inaktuella instanser", "version": "Gatewayversion", "unavailable": "Inte tillgänglig", "checked": "Kontrollerad",
    "recentActivity": "Senaste aktivitet", "recentActivityLead": "De senaste konfigurationsändringarna för miljön.", "viewActivity": "Visa all aktivitet", "time": "Tid", "change": "Ändring", "actor": "Aktör", "resource": "Resurs", "configurationChange": "Konfigurationsändring {number}", "emptyActivity": "Ingen konfigurationsaktivitet", "emptyActivityLead": "Skapa en route för att påbörja miljöns historik.", "addRoute": "Lägg till en route"
  }
}
</i18n>

<style scoped>
.metric {
  font-size: 2rem;
  font-weight: 730;
  letter-spacing: -0.04em;
}

.health-value {
  font-size: 1.5rem;
  font-weight: 700;
}

.posture-card {
  cursor: pointer;
}

.posture-card:hover {
  border-color: var(--primary);
  background: var(--accent);
}

.dashboard-card-header {
  border-bottom: 1px solid var(--border);
}

.activity-time {
  white-space: nowrap;
}

.dashboard-activity-table :deep(tbody td) {
  padding-block: 0.625rem;
}

.dashboard-empty-state {
  min-height: 12rem;
  display: grid;
  padding: 2rem;
  color: var(--muted-foreground);
  text-align: center;
  place-items: center;
}
</style>
