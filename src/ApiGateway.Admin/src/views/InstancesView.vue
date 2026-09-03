<template>
  <div class="page-container">
    <div class="flex items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ $t('nav.system') }}
        </p><h1>{{ t('title') }}</h1><p class="page-lead">
          {{ t('lead') }}
        </p>
      </div>
      <div class="instances-toolbar">
        <Field class="instances-environment">
          <FieldLabel for="instances-environment">
            {{ t('environment') }}
          </FieldLabel><Select v-model="environmentId">
            <SelectTrigger id="instances-environment">
              <SelectValue :placeholder="t('environment')" />
            </SelectTrigger><SelectContent>
              <SelectItem v-for="environment in environments" :key="environment.id" :value="environment.id">
                {{ environment.displayName }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field>
        <IconButton variant="outline" size="icon" :label="t('refresh')" @click="load">
          <RefreshCw />
        </IconButton>
      </div>
    </div>
    <Alert v-if="error" variant="destructive" class="mt-4">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Card class="mt-6 py-0">
      <CardHeader class="border-b py-4">
        <CardTitle>{{ t('instances') }}</CardTitle>
      </CardHeader>
      <Table>
        <TableHeader><TableRow><TableHead>{{ t('instance') }}</TableHead><TableHead>{{ t('activatedRevision') }}</TableHead><TableHead>{{ t('heartbeat') }}</TableHead><TableHead>{{ t('status') }}</TableHead><TableHead /></TableRow></TableHeader><TableBody>
          <TableRow v-for="instance in instances" :key="instance.id">
            <TableCell>
              {{ instance.displayName }}<div class="text-xs">
                {{ instance.instanceId }}
              </div>
            </TableCell><TableCell><code>{{ instance.activatedRevisionId || t('none') }}</code></TableCell><TableCell>{{ formatDateTime(instance.lastHeartbeatAtUtc) }}</TableCell><TableCell>
              <Badge :variant="instanceBadgeVariant(instance)">
                {{ instance.stoppedAtUtc ? t('stopped') : isStale(instance) ? t('stale') : instance.lastActivationErrorCode || instance.lastActivationStatus || t('starting') }}
              </Badge>
            </TableCell><TableCell class="text-right">
              <IconButton v-if="instance.stoppedAtUtc || isStale(instance)" variant="destructive" :label="t('decommission')" @click="decommission(instance)">
                <Trash2 />
              </IconButton>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <Empty v-if="instances.length === 0">
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <WifiOff />
          </EmptyMedia><EmptyTitle>{{ t('noInstances') }}</EmptyTitle><EmptyDescription>{{ t('noInstancesText') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>
    </Card>
    <Card class="mt-6 py-0">
      <CardHeader class="border-b py-4">
        <CardTitle>{{ t('activationHistory') }}</CardTitle>
      </CardHeader>
      <Table>
        <TableHeader><TableRow><TableHead>{{ t('completed') }}</TableHead><TableHead>{{ t('instance') }}</TableHead><TableHead>{{ t('desiredRevision') }}</TableHead><TableHead>{{ t('outcome') }}</TableHead><TableHead>{{ t('diagnostic') }}</TableHead></TableRow></TableHeader><TableBody>
          <TableRow v-for="activation in activations" :key="activation.id">
            <TableCell>{{ formatDateTime(activation.completedAtUtc) }}</TableCell><TableCell>{{ activation.instanceId }}</TableCell><TableCell><code>{{ activation.desiredRevisionId || t('none') }}</code></TableCell><TableCell>
              <Badge :variant="activation.outcome === 'SUCCEEDED' ? 'success' : 'destructive'">
                {{ activation.outcome }}
              </Badge>
            </TableCell><TableCell><strong v-if="activation.errorCode">{{ activation.errorCode }}</strong><div>{{ activation.errorMessage }}</div></TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Badge, Card, CardHeader, CardTitle, Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle, Field, FieldLabel, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@aditify/ui';
import { CircleAlert, RefreshCw, Trash2, WifiOff } from '@lucide/vue';
import { onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql } from '../api';
import IconButton from '../components/IconButton.vue';
import { confirmAction } from '../composables/confirmDialog';
import { formatDateTime } from '../utils/dateTime';

interface Environment { id: string; displayName: string }interface Instance { id: string; instanceId: string; displayName: string; activatedRevisionId?: string; lastHeartbeatAtUtc: string; lastActivationStatus?: string; lastActivationErrorCode?: string; stoppedAtUtc?: string } interface Activation { id: number; instanceId: string; desiredRevisionId?: string; outcome: string; errorCode?: string; errorMessage?: string; completedAtUtc: string }

const environments = ref<Environment[]>([]); const environmentId = ref(''); const instances = ref<Instance[]>([]); const activations = ref<Activation[]>([]); const error = ref('');
const { t } = useI18n();

function isStale(instance: Instance) { return Date.now() - new Date(instance.lastHeartbeatAtUtc).getTime() > 5 * 60 * 1000; }
function instanceBadgeVariant(instance: Instance): 'secondary' | 'destructive' | 'success' { return instance.stoppedAtUtc || isStale(instance) ? 'secondary' : instance.lastActivationErrorCode ? 'destructive' : 'success'; }
async function load() {
  if (!environmentId.value)
    return;

  await run(async () => {
    const result = await graphql<{ instances: Instance[]; activationHistory: Activation[] }>(`query($id:UUID!){instances(environmentId:$id){id instanceId displayName activatedRevisionId lastHeartbeatAtUtc lastActivationStatus lastActivationErrorCode stoppedAtUtc} activationHistory(environmentId:$id,take:50){id instanceId desiredRevisionId outcome errorCode errorMessage completedAtUtc}}`, { id: environmentId.value });

    instances.value = result.instances; activations.value = result.activationHistory;
  });
}
async function decommission(instance: Instance) {
  if (!await confirmAction(t('decommissionText'), { title: t('decommissionTitle', { name: instance.displayName }), confirmText: t('decommission'), color: 'error' }))
    return;

  await run(async () => { await graphql(`mutation($id:UUID!){decommissionInstance(id:$id)}`, { id: instance.id }); await load(); });
}
async function run(action: () => Promise<void>) {
  error.value = '';

  try { await action(); }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}
onMounted(async () => { await run(async () => { environments.value = (await graphql<{ environments: Environment[] }>(`query{environments{id displayName}}`)).environments; environmentId.value = environments.value[0]?.id || ''; }); }); watch(environmentId, load);
</script>

<i18n lang="json">
{
  "en": {
    "title": "Instances and diagnostics", "lead": "Monitor revision convergence, heartbeat state, and activation outcomes.", "environment": "Environment", "refresh": "Refresh", "instances": "Gateway instances", "instance": "Instance", "activatedRevision": "Activated revision", "heartbeat": "Heartbeat", "status": "Status", "none": "None", "stopped": "Stopped", "stale": "Stale", "starting": "Starting", "decommission": "Decommission", "decommissionText": "Its activation history and audit records will remain.", "decommissionTitle": "Decommission {name}?", "noInstances": "No instances", "noInstancesText": "Instances appear after their first heartbeat.", "activationHistory": "Activation history", "completed": "Completed", "desiredRevision": "Desired revision", "outcome": "Outcome", "diagnostic": "Diagnostic"
  },
  "sv": {
    "title": "Instanser och diagnostik", "lead": "Övervaka revisionssynkronisering, hjärtslag och aktiveringsresultat.", "environment": "Miljö", "refresh": "Uppdatera", "instances": "Gatewayinstanser", "instance": "Instans", "activatedRevision": "Aktiverad revision", "heartbeat": "Hjärtslag", "status": "Status", "none": "Ingen", "stopped": "Stoppad", "stale": "Inaktuell", "starting": "Startar", "decommission": "Avregistrera", "decommissionText": "Aktiveringshistorik och granskningsposter behålls.", "decommissionTitle": "Avregistrera {name}?", "noInstances": "Inga instanser", "noInstancesText": "Instanser visas efter sitt första hjärtslag.", "activationHistory": "Aktiveringshistorik", "completed": "Slutförd", "desiredRevision": "Önskad revision", "outcome": "Resultat", "diagnostic": "Diagnostik"
  }
}
</i18n>

<style scoped>
.instances-toolbar {
  display: flex;
  align-items: end;
  gap: 0.5rem;
}
.instances-environment {
  flex: 0 1 280px;
  min-width: 0;
}

@media (max-width: 700px) {
  .instances-toolbar {
    width: 100% !important;
  }
  .instances-environment {
    flex: 1 1 auto;
    width: auto !important;
  }
}
</style>
