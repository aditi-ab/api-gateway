<template>
  <div class="page">
    <div class="flex items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ t('eyebrow') }}
        </p><h1>{{ t('title') }}</h1><p class="page-lead">
          {{ t('lead') }}
        </p>
      </div><Button :disabled="!selectedEnvironmentId" @click="openCreate">
        <Plus />{{ t('add') }}
      </Button>
    </div>
    <Alert v-if="error" variant="destructive" class="mt-5">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert><EnvironmentRequiredAlert v-else-if="!selectedEnvironmentId" />
    <Card v-else class="data-panel mt-6 py-0">
      <Table v-if="upstreams.length">
        <TableHeader><TableRow><TableHead>{{ t('name') }}</TableHead><TableHead>{{ t('servers') }}</TableHead><TableHead>{{ t('loadBalancing') }}</TableHead><TableHead>{{ t('healthChecks') }}</TableHead><TableHead class="w-24" /></TableRow></TableHeader><TableBody>
          <TableRow v-for="upstream in upstreams" :key="upstream.id">
            <TableCell>
              <strong>{{ upstream.name }}</strong>
            </TableCell><TableCell>
              <div v-for="destination in upstream.destinations" :key="destination.key" class="text-sm">
                <span class="font-medium">{{ destination.key }}</span> <span class="text-muted-foreground">{{ destination.value.address }}</span>
              </div>
            </TableCell><TableCell>{{ upstream.loadBalancingPolicy }}</TableCell><TableCell>{{ healthSummary(upstream) }}</TableCell><TableCell>
              <div class="flex justify-end gap-1">
                <IconButton variant="secondary" :label="t('editNamed', { name: upstream.name })" @click="openEdit(upstream)">
                  <Pencil />
                </IconButton><IconButton variant="destructive" :label="t('deleteNamed', { name: upstream.name })" @click="remove(upstream)">
                  <Trash2 />
                </IconButton>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table><Empty v-else>
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <Network />
          </EmptyMedia><EmptyTitle>{{ t('empty') }}</EmptyTitle><EmptyDescription>{{ t('emptyHelp') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>
    </Card>
    <Dialog v-model:open="dialogOpen">
      <DialogContent size="3xl" scrollable>
        <DialogHeader><DialogTitle>{{ editing ? t('edit') : t('add') }}</DialogTitle><DialogDescription>{{ t('dialogHelp') }}</DialogDescription></DialogHeader><div data-slot="dialog-body" class="grid min-w-0 gap-5 pb-4">
          <Alert v-if="dialogError" variant="destructive">
            <CircleAlert /><AlertDescription>{{ dialogError }}</AlertDescription>
          </Alert><Field>
            <FieldLabel for="upstream-name">
              {{ t('name') }}
            </FieldLabel><Input id="upstream-name" v-model="form.name" autofocus maxlength="128" />
          </Field><Field>
            <FieldLabel for="upstream-load-balancing">
              {{ t('loadBalancing') }}
            </FieldLabel><Select v-model="form.loadBalancingPolicy">
              <SelectTrigger id="upstream-load-balancing">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="policy in policies" :key="policy" :value="policy">
                  {{ policy }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <section class="min-w-0 rounded-lg border p-4">
            <div class="flex items-center justify-between gap-3">
              <div>
                <h3 class="font-semibold">
                  {{ t('servers') }}
                </h3><p class="text-sm text-muted-foreground">
                  {{ t('serversHelp') }}
                </p>
              </div><Button variant="outline" size="sm" @click="addServer">
                <Plus />{{ t('addServer') }}
              </Button>
            </div><div class="mt-4 grid gap-3">
              <div v-for="(server, index) in form.servers" :key="server.clientKey" class="grid min-w-0 gap-3 rounded-md border p-3 sm:grid-cols-2 xl:grid-cols-[minmax(8rem,0.7fr)_minmax(12rem,1.5fr)_minmax(10rem,1fr)_auto]">
                <Field class="min-w-0">
                  <FieldLabel :for="`server-name-${index}`">
                    {{ t('serverName') }}
                  </FieldLabel><Input :id="`server-name-${index}`" v-model="server.name" placeholder="server-1" />
                </Field><Field class="min-w-0">
                  <FieldLabel :for="`server-address-${index}`">
                    {{ t('address') }}
                  </FieldLabel><Input :id="`server-address-${index}`" v-model="server.address" placeholder="https://service.example/" />
                </Field><Field class="min-w-0">
                  <FieldLabel :for="`server-health-${index}`">
                    {{ t('healthAddress') }}
                  </FieldLabel><Input :id="`server-health-${index}`" v-model="server.healthAddress" placeholder="https://service.example/healthz" />
                </Field><IconButton class="self-end justify-self-end" :disabled="form.servers.length === 1" variant="destructive" :label="t('removeServer', { number: index + 1 })" @click="form.servers.splice(index, 1)">
                  <Trash2 />
                </IconButton>
              </div>
            </div>
          </section>
          <section class="min-w-0 rounded-lg border p-4">
            <h3 class="font-semibold">
              {{ t('healthChecks') }}
            </h3><div class="mt-4 grid gap-4 md:grid-cols-2">
              <Field orientation="horizontal">
                <Switch id="active-health" v-model="form.activeEnabled" /><FieldContent>
                  <FieldLabel for="active-health">
                    {{ t('activeHealth') }}
                  </FieldLabel><FieldDescription>{{ t('activeHealthHelp') }}</FieldDescription>
                </FieldContent>
              </Field><Field orientation="horizontal">
                <Switch id="passive-health" v-model="form.passiveEnabled" /><FieldContent>
                  <FieldLabel for="passive-health">
                    {{ t('passiveHealth') }}
                  </FieldLabel><FieldDescription>{{ t('passiveHealthHelp') }}</FieldDescription>
                </FieldContent>
              </Field><Field v-if="form.activeEnabled">
                <FieldLabel for="health-path">
                  {{ t('healthPath') }}
                </FieldLabel><Input id="health-path" v-model="form.healthPath" placeholder="/healthz" />
              </Field><Field v-if="form.activeEnabled">
                <FieldLabel for="health-interval">
                  {{ t('healthInterval') }}
                </FieldLabel><Input id="health-interval" v-model="form.healthInterval" placeholder="PT10S" />
              </Field><Field v-if="form.activeEnabled">
                <FieldLabel for="health-timeout">
                  {{ t('healthTimeout') }}
                </FieldLabel><Input id="health-timeout" v-model="form.healthTimeout" placeholder="PT5S" />
              </Field><Field v-if="form.passiveEnabled">
                <FieldLabel for="reactivation-period">
                  {{ t('reactivationPeriod') }}
                </FieldLabel><Input id="reactivation-period" v-model="form.reactivationPeriod" placeholder="PT1M" />
              </Field>
            </div>
          </section>
        </div><DialogFooter>
          <Button variant="outline" :disabled="saving" @click="dialogOpen = false">
            {{ t('cancel') }}
          </Button><Button :disabled="saving || !canSave" @click="save">
            <Spinner v-if="saving" />{{ t('save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Button, Card, Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle, Field, FieldContent, FieldDescription, FieldLabel, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Spinner, Switch, Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@aditify/ui';
import { CircleAlert, Network, Pencil, Plus, Trash2 } from '@lucide/vue';
import { computed, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql } from '../api';
import EnvironmentRequiredAlert from '../components/EnvironmentRequiredAlert.vue';
import IconButton from '../components/IconButton.vue';
import { confirmAction } from '../composables/confirmDialog';
import { configurationRefreshVersion, loadEnvironments, selectedEnvironmentId } from '../composables/environmentContext';

interface Destination { key: string; value: { address: string; healthAddress?: string | null; pool: string } }
interface Upstream { id: string; name: string; version: string; loadBalancingPolicy: string; destinations: Destination[]; health: { activeEnabled: boolean; path: string; interval?: string | null; timeout?: string | null; passiveEnabled: boolean; reactivationPeriod?: string | null } }
interface ServerForm { clientKey: string; name: string; address: string; healthAddress: string }

const policies = ['PowerOfTwoChoices', 'RoundRobin', 'LeastRequests', 'Random']; const upstreams = ref<Upstream[]>([]); const error = ref(''); const dialogError = ref(''); const dialogOpen = ref(false); const saving = ref(false); const editing = ref<Upstream | null>(null);
const emptyForm = () => ({ name: '', loadBalancingPolicy: 'PowerOfTwoChoices', activeEnabled: false, passiveEnabled: false, healthPath: '/healthz', healthInterval: 'PT10S', healthTimeout: 'PT5S', reactivationPeriod: 'PT1M', servers: [{ clientKey: crypto.randomUUID(), name: 'primary', address: 'https://', healthAddress: '' }] as ServerForm[] }); const form = ref(emptyForm()); const { t } = useI18n(); const canSave = computed(() => form.value.name.trim() && form.value.servers.length > 0 && form.value.servers.every(x => x.name.trim() && x.address.trim()));

function addServer() { form.value.servers.push({ clientKey: crypto.randomUUID(), name: `server-${form.value.servers.length + 1}`, address: 'https://', healthAddress: '' }); }
function healthSummary(upstream: Upstream) {
  const values = [];

  if (upstream.health.activeEnabled)
    values.push(t('active'));

  if (upstream.health.passiveEnabled)
    values.push(t('passive'));

  return values.length ? values.join(', ') : t('disabled');
}
function openCreate() { editing.value = null; form.value = emptyForm(); dialogError.value = ''; dialogOpen.value = true; }
function openEdit(upstream: Upstream) { editing.value = upstream; form.value = { name: upstream.name, loadBalancingPolicy: upstream.loadBalancingPolicy, activeEnabled: upstream.health.activeEnabled, passiveEnabled: upstream.health.passiveEnabled, healthPath: upstream.health.path, healthInterval: upstream.health.interval || 'PT10S', healthTimeout: upstream.health.timeout || 'PT5S', reactivationPeriod: upstream.health.reactivationPeriod || 'PT1M', servers: upstream.destinations.map(x => ({ clientKey: crypto.randomUUID(), name: x.key, address: x.value.address, healthAddress: x.value.healthAddress || '' })) }; dialogError.value = ''; dialogOpen.value = true; }
function input() { return { name: form.value.name.trim(), loadBalancingPolicy: form.value.loadBalancingPolicy, destinations: form.value.servers.map(x => ({ key: x.name.trim(), value: { address: x.address.trim(), healthAddress: x.healthAddress.trim() || null, pool: 'default' } })), health: { activeEnabled: form.value.activeEnabled, path: form.value.healthPath || '/healthz', interval: form.value.activeEnabled ? form.value.healthInterval || null : null, timeout: form.value.activeEnabled ? form.value.healthTimeout || null : null, passiveEnabled: form.value.passiveEnabled, reactivationPeriod: form.value.passiveEnabled ? form.value.reactivationPeriod || null : null } }; }
async function load() {
  if (!selectedEnvironmentId.value) { upstreams.value = []; return; }

  error.value = '';

  try {
    const data = await graphql<{ upstreams: Upstream[] }>(`query Upstreams($environmentId:UUID!){upstreams(environmentId:$environmentId){id name version loadBalancingPolicy destinations{key value{address healthAddress pool}} health{activeEnabled path interval timeout passiveEnabled reactivationPeriod}}}`, { environmentId: selectedEnvironmentId.value });

    upstreams.value = data.upstreams;
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}
async function save() {
  saving.value = true; dialogError.value = '';

  try {
    if (editing.value)
      await graphql(`mutation UpdateUpstream($environmentId:UUID!,$id:String!,$version:String!,$input:SaveNamedUpstreamInput!){updateUpstream(environmentId:$environmentId,upstreamId:$id,expectedUpstreamVersion:$version,input:$input){revision{id}}}`, { environmentId: selectedEnvironmentId.value, id: editing.value.id, version: editing.value.version, input: input() }); else await graphql(`mutation CreateUpstream($environmentId:UUID!,$input:SaveNamedUpstreamInput!){createUpstream(environmentId:$environmentId,input:$input){revision{id}}}`, { environmentId: selectedEnvironmentId.value, input: input() });

    dialogOpen.value = false; await loadEnvironments(); await load();
  }
  catch (e) { dialogError.value = e instanceof Error ? e.message : String(e); }
  finally { saving.value = false; }
}
async function remove(upstream: Upstream) {
  if (!await confirmAction(t('deleteHelp', { name: upstream.name }), { title: t('deleteTitle'), confirmText: t('delete'), color: 'error' }))
    return;

  try { await graphql(`mutation DeleteUpstream($environmentId:UUID!,$id:String!,$version:String!){deleteUpstream(environmentId:$environmentId,upstreamId:$id,expectedUpstreamVersion:$version){revision{id}}}`, { environmentId: selectedEnvironmentId.value, id: upstream.id, version: upstream.version }); await loadEnvironments(); await load(); }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}
watch([selectedEnvironmentId, configurationRefreshVersion], load); onMounted(async () => { await loadEnvironments(); await load(); });
</script>

<i18n lang="json">
{"en":{"eyebrow":"Traffic destinations","title":"Upstreams","lead":"Create reusable server groups, health checks, and load-balancing behavior for one or more routes.","add":"Add upstream","edit":"Edit upstream","dialogHelp":"Routes that select this upstream use these servers and settings together.","name":"Name","servers":"Servers","serversHelp":"Add every instance that can receive traffic.","addServer":"Add server","serverName":"Server name","address":"Server URL","healthAddress":"Health URL (optional)","removeServer":"Remove server {number}","loadBalancing":"Load balancing","healthChecks":"Health checks","activeHealth":"Active health checks","activeHealthHelp":"Probe servers on a schedule.","passiveHealth":"Passive health checks","passiveHealthHelp":"Observe failures from proxied requests.","healthPath":"Probe path","healthInterval":"Probe interval","healthTimeout":"Probe timeout","reactivationPeriod":"Reactivation period","active":"Active","passive":"Passive","disabled":"Disabled","empty":"No reusable upstreams","emptyHelp":"Routes can still use a directly entered URL, or you can create a reusable upstream here.","save":"Save","cancel":"Cancel","editNamed":"Edit {name}","deleteNamed":"Delete {name}","delete":"Delete","deleteTitle":"Delete upstream?","deleteHelp":"Delete {name}? An upstream in use by a route cannot be deleted."},"sv":{"eyebrow":"Trafikdestinationer","title":"Upstreams","lead":"Skapa återanvändbara servergrupper, hälsokontroller och lastbalansering för en eller flera routes.","add":"Lägg till upstream","edit":"Redigera upstream","dialogHelp":"Routes som väljer denna upstream använder dessa servrar och inställningar tillsammans.","name":"Namn","servers":"Servrar","serversHelp":"Lägg till varje instans som kan ta emot trafik.","addServer":"Lägg till server","serverName":"Servernamn","address":"Server-URL","healthAddress":"Hälso-URL (valfritt)","removeServer":"Ta bort server {number}","loadBalancing":"Lastbalansering","healthChecks":"Hälsokontroller","activeHealth":"Aktiva hälsokontroller","activeHealthHelp":"Kontrollera servrar enligt ett schema.","passiveHealth":"Passiva hälsokontroller","passiveHealthHelp":"Observera fel från proxade anrop.","healthPath":"Kontrollsökväg","healthInterval":"Kontrollintervall","healthTimeout":"Tidsgräns för kontroll","reactivationPeriod":"Återaktiveringsperiod","active":"Aktiv","passive":"Passiv","disabled":"Inaktiverad","empty":"Inga återanvändbara upstreams","emptyHelp":"Routes kan fortfarande använda en direkt angiven URL, eller så kan du skapa en återanvändbar upstream här.","save":"Spara","cancel":"Avbryt","editNamed":"Redigera {name}","deleteNamed":"Ta bort {name}","delete":"Ta bort","deleteTitle":"Ta bort upstream?","deleteHelp":"Ta bort {name}? En upstream som används av en route kan inte tas bort."}}
</i18n>
