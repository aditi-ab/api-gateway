<template>
  <div class="page">
    <div>
      <p class="eyebrow">
        {{ $t('nav.system') }}
      </p><h1>{{ t('title') }}</h1><p class="page-lead">
        {{ t('lead') }}
      </p>
    </div>
    <Alert v-if="error" variant="destructive" class="mt-4">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert><Alert v-if="message" class="mt-4 border-emerald-500/40 text-emerald-700 dark:text-emerald-300">
      <CircleCheck /><AlertDescription>{{ message }}</AlertDescription>
    </Alert>
    <Tabs v-model="activeTab" class="mt-6">
      <TabsList>
        <TabsTrigger value="traffic">
          <Workflow />{{ t('routeTraffic') }}
        </TabsTrigger><TabsTrigger value="inbound-security">
          <ShieldCheck />{{ t('inboundSecurity') }}
        </TabsTrigger><TabsTrigger value="management">
          <Settings />{{ t('management') }}
        </TabsTrigger>
      </TabsList>
      <TabsContent value="traffic">
        <EnvironmentRequiredAlert v-if="!selectedEnvironmentId" /><Card v-else class="settings-panel mt-4">
          <CardHeader class="flex-row items-center border-b">
            <Workflow class="size-5" /><div>
              <div>{{ t('routeTrafficStates') }}</div><div class="card-subtitle">
                {{ t('routeTrafficSubtitle') }}
              </div>
            </div>
          </CardHeader>
          <CardContent>
            <Alert class="mb-4">
              <Info /><AlertDescription>{{ t('sharedHelp') }}</AlertDescription>
            </Alert>
            <div class="settings-subsection-header mb-4">
              <div>
                <h2 class="text-base font-semibold font-bold">
                  {{ t('environmentDefaults') }}
                </h2><p class="text-sm text-muted-foreground">
                  {{ t('defaultsHelp') }}
                </p>
              </div>
            </div>
            <div class="grid grid-cols-1 gap-4 md:grid-cols-3">
              <div v-for="state in defaultStates" :key="state.key">
                <div class="default-state-card">
                  <div class="flex items-center gap-2 mb-3">
                    <component :is="state.icon" :class="state.class" /><strong>{{ state.title }}</strong>
                  </div><Field>
                    <FieldLabel :for="`default-${state.key}`">
                      {{ t('responseProfile') }}
                    </FieldLabel><Select v-model="defaults[state.key]">
                      <SelectTrigger :id="`default-${state.key}`">
                        <SelectValue :placeholder="t('responseProfile')" />
                      </SelectTrigger><SelectContent>
                        <SelectItem :value="null">
                          {{ t('notConfigured') }}
                        </SelectItem>
                        <SelectItem v-for="profile in profileOptions" :key="profile.value" :value="profile.value">
                          {{ profile.title }}
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </Field><div class="text-xs text-muted-foreground mt-2">
                    {{ state.description }}
                  </div>
                </div>
              </div>
            </div>
            <div class="flex justify-end mt-4">
              <Button class="settings-action" :disabled="savingDefaults || !selectedEnvironmentId" @click="saveDefaults">
                <Spinner v-if="savingDefaults" />{{ t('saveDefaults') }}
              </Button>
            </div>
            <Separator class="my-6" />
            <div class="settings-subsection-header mb-4">
              <div>
                <h2 class="text-base font-semibold font-bold">
                  {{ t('sharedResponses') }}
                </h2><p class="text-sm text-muted-foreground">
                  {{ t('sharedResponsesHelp') }}
                </p>
              </div><Button class="settings-action" :disabled="!selectedEnvironmentId" @click="newProfile">
                <Plus />{{ t('addResponse') }}
              </Button>
            </div>
            <div class="table-frame">
              <Table v-if="profiles.length">
                <TableHeader>
                  <TableRow>
                    <TableHead>{{ t('name') }}</TableHead><TableHead>{{ t('type') }}</TableHead><TableHead>{{ t('response') }}</TableHead><TableHead class="text-right">
                      {{ t('actions') }}
                    </TableHead>
                  </TableRow>
                </TableHeader><TableBody>
                  <TableRow v-for="profile in profiles" :key="profile.id">
                    <TableCell>
                      <strong>{{ profile.name }}</strong><div class="mono text-xs text-muted-foreground">
                        {{ profile.id }}
                      </div>
                    </TableCell><TableCell>
                      <Badge variant="secondary">
                        <Server v-if="profile.response.upstreamUrl" /><PanelTop v-else />{{ profile.response.upstreamUrl ? t('dedicatedUpstream') : t('gatewayHosted') }}
                      </Badge>
                    </TableCell><TableCell>
                      <template v-if="profile.response.upstreamUrl">
                        <span class="mono">{{ profile.response.upstreamUrl }}</span>
                      </template><template v-else>
                        <strong>HTTP {{ profile.response.statusCode }}</strong><span class="text-muted-foreground"> · {{ profile.response.title || t('serviceUnavailable') }}</span>
                      </template>
                    </TableCell><TableCell class="text-right">
                      <IconButton variant="secondary" :label="t('edit')" @click="editProfile(profile)">
                        <Pencil />
                      </IconButton>
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table><Empty v-else>
                <EmptyHeader>
                  <EmptyMedia variant="icon">
                    <WifiOff />
                  </EmptyMedia><EmptyTitle>{{ t('noSharedResponses') }}</EmptyTitle><EmptyDescription>{{ t('noSharedResponsesText') }}</EmptyDescription>
                </EmptyHeader>
              </Empty>
            </div>
          </CardContent>
        </Card>
      </TabsContent><TabsContent value="inbound-security">
        <Card class="settings-panel mt-4">
          <CardHeader class="flex-row items-center border-b">
            <ShieldCheck class="size-5" /><div>
              <div>{{ t('hsts') }}</div><div class="card-subtitle">
                {{ t('hstsLead') }}
              </div>
            </div>
          </CardHeader><CardContent>
            <Alert class="mb-5">
              <Info /><AlertDescription>{{ t('hstsInfo') }}</AlertDescription>
            </Alert>
            <div class="setting-row border p-4 mb-5">
              <Field orientation="horizontal">
                <Switch id="hsts-enabled" v-model="hsts.enabled" /><FieldContent>
                  <FieldLabel for="hsts-enabled">
                    {{ t('hstsEnabled') }}
                  </FieldLabel><FieldDescription>{{ t('hstsEnabledHint') }}</FieldDescription>
                </FieldContent>
              </Field>
            </div>
            <div class="grid grid-cols-1 gap-4 md:grid-cols-3">
              <Field class="md:col-span-2">
                <FieldLabel for="hsts-hosts">
                  {{ t('hstsHosts') }}
                </FieldLabel><Input id="hsts-hosts" :model-value="hsts.hosts.join(', ')" :disabled="!hsts.enabled" @update:model-value="hsts.hosts = String($event).split(',').map(x => x.trim()).filter(Boolean)" /><FieldDescription>{{ t('hstsHostsHint') }}</FieldDescription>
              </Field><Field>
                <FieldLabel for="hsts-max-age">
                  {{ t('maxAge') }}
                </FieldLabel><Input id="hsts-max-age" v-model.number="hsts.maxAge" type="number" :disabled="!hsts.enabled" min="0" max="63072000" /><FieldDescription>{{ t('maxAgeHint') }}</FieldDescription>
              </Field>
            </div>
            <div class="setting-row border p-4 mt-2">
              <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
                <Field orientation="horizontal">
                  <Switch id="hsts-subdomains" v-model="hsts.includeSubDomains" :disabled="!hsts.enabled" /><FieldContent>
                    <FieldLabel for="hsts-subdomains">
                      {{ t('includeSubDomains') }}
                    </FieldLabel><FieldDescription>{{ t('includeSubDomainsHint') }}</FieldDescription>
                  </FieldContent>
                </Field><Field orientation="horizontal">
                  <Switch id="hsts-preload" v-model="hsts.preload" :disabled="!hsts.enabled" /><FieldContent>
                    <FieldLabel for="hsts-preload">
                      {{ t('preload') }}
                    </FieldLabel><FieldDescription>{{ t('preloadHint') }}</FieldDescription>
                  </FieldContent>
                </Field>
              </div>
            </div>
          </CardContent><CardFooter class="justify-end border-t">
            <Button class="settings-action" :disabled="savingHsts || hsts.enabled && !hsts.hosts.length" @click="saveHsts">
              <Spinner v-if="savingHsts" />{{ t('saveHsts') }}
            </Button>
          </CardFooter>
        </Card>
      </TabsContent><TabsContent value="management">
        <Card class="settings-panel mt-4">
          <CardHeader class="flex-row items-center border-b">
            <Settings class="size-5" /><div>
              <div>{{ t('management') }}</div><div class="card-subtitle">
                {{ t('managementSubtitle') }}
              </div>
            </div>
          </CardHeader><CardContent>
            <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
              <div>
                <div class="management-block">
                  <h2 class="text-base font-semibold font-bold mb-2">
                    {{ t('managementPlane') }}
                  </h2><ItemGroup>
                    <Item size="sm">
                      <ItemContent><ItemTitle>{{ t('version') }}</ItemTitle><ItemDescription>{{ status?.version || t('unknown') }}</ItemDescription></ItemContent>
                    </Item><Item size="sm">
                      <ItemContent><ItemTitle>{{ t('graphqlEndpoint') }}</ItemTitle><ItemDescription>{{ config?.graphqlEndpoint }}</ItemDescription></ItemContent>
                    </Item><Item size="sm">
                      <ItemContent><ItemTitle>{{ t('documentation') }}</ItemTitle><ItemDescription>{{ config?.documentationUrl }}</ItemDescription></ItemContent>
                    </Item>
                  </ItemGroup>
                </div>
              </div><div>
                <div class="management-block">
                  <h2 class="text-base font-semibold font-bold mb-2">
                    {{ t('authentication') }}
                  </h2><ItemGroup>
                    <Item size="sm">
                      <ItemContent><ItemTitle>{{ t('localAdministrator') }}</ItemTitle><ItemDescription>{{ t('enabled') }}</ItemDescription></ItemContent>
                    </Item><Item size="sm">
                      <ItemContent><ItemTitle>{{ t('entraId') }}</ItemTitle><ItemDescription>{{ config?.entra?.clientId ? t('configured') : t('notConfigured') }}</ItemDescription></ItemContent>
                    </Item><Item v-if="config?.entra?.authority" size="sm">
                      <ItemContent><ItemTitle>{{ t('authority') }}</ItemTitle><ItemDescription>{{ config.entra.authority }}</ItemDescription></ItemContent>
                    </Item>
                  </ItemGroup>
                </div>
              </div>
            </div>
            <Separator class="my-6" /><div class="settings-subsection-header mb-4">
              <div>
                <h2 class="text-base font-semibold font-bold">
                  {{ t('retention') }}
                </h2><p class="text-sm text-muted-foreground">
                  {{ t('retentionHelp') }}
                </p>
              </div>
            </div><div class="grid grid-cols-1 gap-4 md:grid-cols-3">
              <Field>
                <FieldLabel for="activation-days">
                  {{ t('activationDays') }}
                </FieldLabel><Input id="activation-days" v-model.number="activationDays" type="number" min="1" />
              </Field><Field>
                <FieldLabel for="audit-days">
                  {{ t('auditDays') }}
                </FieldLabel><Input id="audit-days" v-model.number="auditDays" type="number" min="1" />
              </Field><div class="flex items-end justify-end">
                <Button class="settings-action" variant="destructive" @click="runRetention">
                  <Trash2 />{{ t('runMaintenance') }}
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>
      </TabsContent>
    </Tabs>
    <Dialog v-model:open="profileDialog">
      <DialogContent size="xl">
        <DialogHeader><DialogTitle>{{ profileForm.id ? t('editUnavailable') : t('addUnavailable') }}</DialogTitle></DialogHeader><FieldGroup>
          <Field>
            <FieldLabel for="profile-name">
              {{ t('name') }}
            </FieldLabel><Input id="profile-name" v-model="profileForm.name" autofocus />
          </Field><Field>
            <FieldLabel for="profile-mode">
              {{ t('responseType') }}
            </FieldLabel><Select v-model="profileForm.mode">
              <SelectTrigger id="profile-mode">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="mode in responseModes" :key="mode.value" :value="mode.value">
                  {{ mode.title }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field><template v-if="profileForm.mode === 'HOSTED'">
            <Field>
              <FieldLabel for="profile-status">
                {{ t('statusCode') }}
              </FieldLabel><Input id="profile-status" v-model.number="profileForm.statusCode" type="number" min="400" max="599" />
            </Field><Field>
              <FieldLabel for="profile-title">
                {{ t('pageTitle') }}
              </FieldLabel><Input id="profile-title" v-model="profileForm.title" />
            </Field><Field>
              <FieldLabel for="profile-message">
                {{ t('message') }}
              </FieldLabel><Textarea id="profile-message" v-model="profileForm.message" rows="3" />
            </Field>
          </template><Field v-else>
            <FieldLabel for="profile-upstream">
              {{ t('dedicatedUpstreamUrl') }}
            </FieldLabel><Input id="profile-upstream" v-model="profileForm.upstreamUrl" placeholder="https://maintenance.example/" />
          </Field><Field>
            <FieldLabel for="profile-retry">
              {{ t('retryAfter') }}
            </FieldLabel><Input id="profile-retry" v-model="profileForm.retryAfter" placeholder="PT5M" /><FieldDescription>{{ t('retryAfterHint') }}</FieldDescription>
          </Field>
        </FieldGroup><DialogFooter class="sm:justify-between">
          <Button v-if="profileForm.id" variant="destructive" @click="deleteProfile">
            <Trash2 />{{ t('delete') }}
          </Button><div class="flex justify-end gap-2">
            <Button variant="outline" @click="profileDialog = false">
              {{ t('cancel') }}
            </Button><Button :disabled="savingProfile || !profileForm.name || (profileForm.mode === 'UPSTREAM' && !profileForm.upstreamUrl)" @click="saveProfile">
              <Spinner v-if="savingProfile" />{{ isStaged ? tg('common.saveUnpublished') : t('saveActivate') }}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

<script setup lang="ts">
import type { Component } from 'vue';
import { Alert, AlertDescription, Badge, Button, Card, CardContent, CardFooter, CardHeader, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle, Field, FieldContent, FieldDescription, FieldGroup, FieldLabel, Input, Item, ItemContent, ItemDescription, ItemGroup, ItemTitle, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Separator, Spinner, Switch, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Tabs, TabsContent, TabsList, TabsTrigger, Textarea } from '@aditify/ui';
import { CircleAlert, CircleCheck, Info, PanelTop, Pencil, PlugZap, Plus, Server, Settings, ShieldCheck, Trash2, Waves, WifiOff, Workflow } from '@lucide/vue';
import { computed, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';
import { graphql } from '../api';
import EnvironmentRequiredAlert from '../components/EnvironmentRequiredAlert.vue';
import IconButton from '../components/IconButton.vue';
import { confirmAction } from '../composables/confirmDialog';
import { editableRevisionId, loadEnvironments, selectedEnvironment, selectedEnvironmentId } from '../composables/environmentContext';

interface ClientConfig { authenticationModes: string[]; graphqlEndpoint: string; documentationUrl: string; entra?: { authority?: string; clientId?: string; scope?: string } | null }
interface Status { version: string; checkedAtUtc: string }
interface ResponseProfile { id: string; name: string; response: { statusCode: number; title?: string; message?: string; retryAfter?: string; upstreamUrl?: string } }
type DefaultProfileKey = 'drainingProfileId' | 'maintenanceProfileId' | 'offlineProfileId';

const config = ref<ClientConfig | null>(null); const status = ref<Status | null>(null); const error = ref(''); const message = ref(''); const activationDays = ref(30); const auditDays = ref(365);
const { t } = useI18n();
const { t: tg } = useI18n({ useScope: 'global' });
const route = useRoute();
const settingsTabs = ['traffic', 'inbound-security', 'management'];
const requestedTab = typeof route.query.tab === 'string' ? route.query.tab : '';
const activeTab = ref(settingsTabs.includes(requestedTab) ? requestedTab : 'traffic');
const profiles = ref<ResponseProfile[]>([]); const defaults = ref({ drainingProfileId: null as string | null, maintenanceProfileId: null as string | null, offlineProfileId: null as string | null });
const profileDialog = ref(false); const savingProfile = ref(false); const savingDefaults = ref(false);
const savingHsts = ref(false); const hsts = ref({ enabled: false, hosts: [] as string[], maxAge: 15552000, includeSubDomains: false, preload: false, version: null as string | null });
const responseModes = computed(() => [{ title: t('gatewayHostedStatus'), value: 'HOSTED' }, { title: t('dedicatedMaintenanceUpstream'), value: 'UPSTREAM' }]);
const profileForm = ref({ id: null as string | null, name: '', mode: 'HOSTED', statusCode: 503, title: '', message: '', retryAfter: '', upstreamUrl: 'https://' });
const configurationVersion = computed(() => editableRevisionId.value);
const isStaged = computed(() => selectedEnvironment.value?.publishingMode === 'STAGED');
const profileOptions = computed(() => profiles.value.map(x => ({ title: x.name, value: x.id })));
const defaultStates = computed<Array<{ key: DefaultProfileKey; title: string; icon: Component; class: string; description: string }>>(() => [
  { key: 'drainingProfileId', title: t('draining'), icon: Waves, class: 'size-4 text-amber-500', description: t('drainingDescription') },
  { key: 'maintenanceProfileId', title: t('maintenance'), icon: Settings, class: 'size-4 text-amber-500', description: t('maintenanceDescription') },
  { key: 'offlineProfileId', title: t('offline'), icon: PlugZap, class: 'size-4 text-destructive', description: t('offlineDescription') },
]);

async function loadResponses() {
  if (!selectedEnvironmentId.value) { profiles.value = []; return; }

  const data = await graphql<{ routeUnavailableResponseProfiles: ResponseProfile[]; routeOperationalDefaults: typeof defaults.value }>(`query RouteResponses($environmentId:UUID!){routeUnavailableResponseProfiles(environmentId:$environmentId){id name response{statusCode title message retryAfter upstreamUrl}} routeOperationalDefaults(environmentId:$environmentId){drainingProfileId maintenanceProfileId offlineProfileId}}`, { environmentId: selectedEnvironmentId.value });

  profiles.value = data.routeUnavailableResponseProfiles; defaults.value = data.routeOperationalDefaults;
}
async function loadHsts() {
  const data = await graphql<any>(`query{inboundSecuritySettings{hstsEnabled hstsHosts hstsMaxAgeSeconds hstsIncludeSubDomains hstsPreload version}}`);
  const settings = data.inboundSecuritySettings;

  hsts.value = { enabled: settings.hstsEnabled, hosts: settings.hstsHosts, maxAge: settings.hstsMaxAgeSeconds, includeSubDomains: settings.hstsIncludeSubDomains, preload: settings.hstsPreload, version: settings.version };
}
async function saveHsts() {
  savingHsts.value = true; error.value = ''; message.value = '';

  try {
    const value = hsts.value;

    await graphql(`mutation($version:UUID,$enabled:Boolean!,$hosts:[String!]!,$maxAge:Int!,$includeSubDomains:Boolean!,$preload:Boolean!){updateInboundSecuritySettings(expectedVersion:$version,enabled:$enabled,hosts:$hosts,maxAgeSeconds:$maxAge,includeSubDomains:$includeSubDomains,preload:$preload){version}}`, { version: value.version, enabled: value.enabled, hosts: value.hosts, maxAge: value.maxAge, includeSubDomains: value.includeSubDomains, preload: value.preload });
    await loadHsts(); message.value = t('hstsSaved');
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
  finally { savingHsts.value = false; }
}
function newProfile() { profileForm.value = { id: null, name: '', mode: 'HOSTED', statusCode: 503, title: '', message: '', retryAfter: '', upstreamUrl: 'https://' }; profileDialog.value = true; }
function editProfile(profile: ResponseProfile) {
  const r = profile.response;

  profileForm.value = { id: profile.id, name: profile.name, mode: r.upstreamUrl ? 'UPSTREAM' : 'HOSTED', statusCode: r.statusCode, title: r.title || '', message: r.message || '', retryAfter: r.retryAfter || '', upstreamUrl: r.upstreamUrl || 'https://' }; profileDialog.value = true;
}
async function saveProfile() {
  savingProfile.value = true; error.value = '';

  try {
    const f = profileForm.value;

    await graphql(`mutation SaveResponse($environmentId:UUID!,$version:UUID,$input:SaveRouteUnavailableResponseProfileInput!){saveRouteUnavailableResponseProfile(environmentId:$environmentId,expectedConfigurationVersion:$version,input:$input){revision{id}}}`, { environmentId: selectedEnvironmentId.value, version: configurationVersion.value || null, input: { id: f.id, name: f.name, statusCode: f.statusCode, title: f.mode === 'HOSTED' ? f.title || null : null, message: f.mode === 'HOSTED' ? f.message || null : null, retryAfter: f.retryAfter || null, upstreamUrl: f.mode === 'UPSTREAM' ? f.upstreamUrl : null } }); profileDialog.value = false; await loadEnvironments(); await loadResponses();
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
  finally { savingProfile.value = false; }
}
async function deleteProfile() {
  if (!profileForm.value.id || !configurationVersion.value || !await confirmAction(t('deleteResponseMessage'), { title: t('deleteResponseTitle'), confirmText: t('delete'), color: 'error' }))
    return;

  savingProfile.value = true;

  try { await graphql(`mutation DeleteResponse($environmentId:UUID!,$version:UUID!,$profileId:String!){deleteRouteUnavailableResponseProfile(environmentId:$environmentId,expectedConfigurationVersion:$version,profileId:$profileId){revision{id}}}`, { environmentId: selectedEnvironmentId.value, version: configurationVersion.value, profileId: profileForm.value.id }); profileDialog.value = false; await loadEnvironments(); await loadResponses(); }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
  finally { savingProfile.value = false; }
}
async function saveDefaults() {
  savingDefaults.value = true;

  try { await graphql(`mutation SaveDefaults($environmentId:UUID!,$version:UUID,$input:UpdateRouteOperationalDefaultsInput!){updateRouteOperationalDefaults(environmentId:$environmentId,expectedConfigurationVersion:$version,input:$input){revision{id}}}`, { environmentId: selectedEnvironmentId.value, version: configurationVersion.value || null, input: defaults.value }); await loadEnvironments(); await loadResponses(); message.value = isStaged.value ? tg('common.savedUnpublished') : t('defaultsSaved'); }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
  finally { savingDefaults.value = false; }
}
async function runRetention() {
  if (!await confirmAction(t('retentionMessage', { activationDays: activationDays.value, auditDays: auditDays.value }), { title: t('retentionTitle'), confirmText: t('deletePermanently'), color: 'error' }))
    return;

  error.value = ''; message.value = '';

  try {
    const result = await graphql<{ runRetentionMaintenance: { leaseAcquired: boolean; activationEventsDeleted: number; auditEventsDeleted: number } }>(`mutation($activations:DateTime!,$audits:DateTime!){runRetentionMaintenance(activationBeforeUtc:$activations,auditBeforeUtc:$audits){leaseAcquired activationEventsDeleted auditEventsDeleted}}`, { activations: new Date(Date.now() - activationDays.value * 86400000).toISOString(), audits: new Date(Date.now() - auditDays.value * 86400000).toISOString() });

    message.value = result.runRetentionMaintenance.leaseAcquired ? t('retentionResult', { activations: result.runRetentionMaintenance.activationEventsDeleted, audits: result.runRetentionMaintenance.auditEventsDeleted }) : t('retentionLeaseBusy');
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}
onMounted(async () => {
  try {
    const [configuration, system] = await Promise.all([fetch('/admin/config.json').then(x => x.json() as Promise<ClientConfig>), graphql<{ systemStatus: Status }>(`query{systemStatus{version checkedAtUtc}}`)]);

    config.value = configuration; status.value = system.systemStatus; await Promise.all([loadResponses(), loadHsts()]);
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
});
watch(selectedEnvironmentId, () => void loadResponses());
</script>

<i18n lang="json">
{
    "en": {"title":"Settings","lead":"Configure shared route behavior, inbound security, and management maintenance.","routeTraffic":"Route traffic","management":"Management","routeTrafficStates":"Route traffic states","routeTrafficSubtitle":"Shared responses and defaults for the selected environment.","sharedHelp":"Create shared gateway-hosted pages or dedicated upstreams, then use them as defaults or select them on individual routes.","environmentDefaults":"Environment defaults","defaultsHelp":"Applied when a route does not select a response override.","responseProfile":"Response profile","saveDefaults":"Save defaults","sharedResponses":"Shared responses","sharedResponsesHelp":"Reusable hosted responses and dedicated upstreams that routes reference by ID.","addResponse":"Add response","name":"Name","type":"Type","response":"Response","actions":"Actions","dedicatedUpstream":"Dedicated upstream","gatewayHosted":"Gateway hosted","serviceUnavailable":"Service unavailable","edit":"Edit","noSharedResponses":"No shared responses","noSharedResponsesText":"Add a reusable unavailable response for route draining, maintenance, or offline states.","managementSubtitle":"Service information, authentication, and data retention.","managementPlane":"Management plane","version":"Version","unknown":"Unknown","graphqlEndpoint":"GraphQL endpoint","documentation":"Documentation","authentication":"Authentication","localAdministrator":"Local administrator","entraId":"Microsoft Entra ID","enabled":"Enabled","configured":"Configured","notConfigured":"Not configured","authority":"Authority","retention":"Retention maintenance","retentionHelp":"Delete old operational records using the lease shared by management replicas.","activationDays":"Keep activation history (days)","auditDays":"Keep audit history (days)","runMaintenance":"Run maintenance now","editUnavailable":"Edit unavailable response","addUnavailable":"Add unavailable response","responseType":"Response type","statusCode":"HTTP status code","pageTitle":"Page title","message":"Message","dedicatedUpstreamUrl":"Dedicated upstream URL","retryAfter":"Retry-After","retryAfterHint":"Optional ISO 8601 duration, for example PT5M.","delete":"Delete","cancel":"Cancel","saveActivate":"Save and activate","gatewayHostedStatus":"Gateway-hosted status response","dedicatedMaintenanceUpstream":"Dedicated maintenance upstream","draining":"Draining","maintenance":"Maintenance","offline":"Offline","drainingDescription":"Returned to new requests while active requests finish.","maintenanceDescription":"Returned while planned work is in progress.","offlineDescription":"Returned when the route is intentionally unavailable.","deleteResponseMessage":"Delete this shared unavailable response?","deleteResponseTitle":"Delete response?","defaultsSaved":"Route traffic-state defaults saved.","retentionMessage":"Permanently delete activation events older than {activationDays} days and audit events older than {auditDays} days?","retentionTitle":"Delete retained history?","deletePermanently":"Delete permanently","retentionResult":"Deleted {activations} activation events and {audits} audit events.","retentionLeaseBusy":"Another management replica owns the retention lease.","inboundSecurity":"Inbound security","hsts":"HTTP Strict Transport Security","hstsLead":"Apply one consistent browser HTTPS policy to selected public hostnames.","hstsInfo":"HSTS is a global hostname policy shared across environments, not a route or path policy. Every HTTPS response for a matching hostname receives the same header. Enable it only after HTTPS works reliably.","hstsEnabled":"Enable HSTS","hstsEnabledHint":"Disabled by default to prevent accidental browser lockout.","hstsHosts":"Protected hostnames","hstsHostsHint":"Add exact hostnames or wildcard patterns such as *.example.com.","maxAge":"Max age (seconds)","maxAgeHint":"One year is 31536000 seconds.","includeSubDomains":"Include subdomains","includeSubDomainsHint":"Apply the policy to every subdomain of each configured hostname.","preload":"Request preload eligibility","preloadHint":"Requires includeSubDomains and a max age of at least one year.","saveHsts":"Save HSTS settings","hstsSaved":"HSTS settings saved."},
    "sv": {"title":"Inställningar","lead":"Konfigurera delat routebeteende, inkommande säkerhet och underhåll av hanteringsplanet.","routeTraffic":"Routetrafik","management":"Hantering","routeTrafficStates":"Trafiklägen för routes","routeTrafficSubtitle":"Delade svar och standardvärden för den valda miljön.","sharedHelp":"Skapa delade gatewaysidor eller dedikerade upstreams och använd dem som standard eller välj dem på enskilda routes.","environmentDefaults":"Miljöstandarder","defaultsHelp":"Används när en route inte väljer ett eget svar.","responseProfile":"Svarsprofil","saveDefaults":"Spara standarder","sharedResponses":"Delade svar","sharedResponsesHelp":"Återanvändbara gatewaysvar och dedikerade upstreams som routes refererar till med ID.","addResponse":"Lägg till svar","name":"Namn","type":"Typ","response":"Svar","actions":"Åtgärder","dedicatedUpstream":"Dedikerad upstream","gatewayHosted":"Gatewayhanterat","serviceUnavailable":"Tjänsten är otillgänglig","edit":"Redigera","noSharedResponses":"Inga delade svar","noSharedResponsesText":"Lägg till ett återanvändbart otillgänglighetssvar för dränering, underhåll eller offline-läge.","managementSubtitle":"Tjänsteinformation, autentisering och datalagring.","managementPlane":"Hanteringsplan","version":"Version","unknown":"Okänd","graphqlEndpoint":"GraphQL-slutpunkt","documentation":"Dokumentation","authentication":"Autentisering","localAdministrator":"Lokal administratör","entraId":"Microsoft Entra ID","enabled":"Aktiverad","configured":"Konfigurerad","notConfigured":"Inte konfigurerad","authority":"Utfärdare","retention":"Lagringsunderhåll","retentionHelp":"Ta bort gamla driftposter med låset som delas av hanteringsrepliker.","activationDays":"Behåll aktiveringshistorik (dagar)","auditDays":"Behåll granskningshistorik (dagar)","runMaintenance":"Kör underhåll nu","editUnavailable":"Redigera otillgänglighetssvar","addUnavailable":"Lägg till otillgänglighetssvar","responseType":"Svarstyp","statusCode":"HTTP-statuskod","pageTitle":"Sidtitel","message":"Meddelande","dedicatedUpstreamUrl":"URL till dedikerad upstream","retryAfter":"Retry-After","retryAfterHint":"Valfri ISO 8601-varaktighet, till exempel PT5M.","delete":"Ta bort","cancel":"Avbryt","saveActivate":"Spara och aktivera","gatewayHostedStatus":"Gatewayhanterat statussvar","dedicatedMaintenanceUpstream":"Dedikerad underhållsupstream","draining":"Dränering","maintenance":"Underhåll","offline":"Offline","drainingDescription":"Returneras till nya anrop medan aktiva anrop slutförs.","maintenanceDescription":"Returneras medan planerat arbete pågår.","offlineDescription":"Returneras när routen avsiktligt är otillgänglig.","deleteResponseMessage":"Ta bort detta delade otillgänglighetssvar?","deleteResponseTitle":"Ta bort svar?","defaultsSaved":"Standarder för routens trafikläge har sparats.","retentionMessage":"Ta permanent bort aktiveringshändelser äldre än {activationDays} dagar och granskningshändelser äldre än {auditDays} dagar?","retentionTitle":"Ta bort sparad historik?","deletePermanently":"Ta bort permanent","retentionResult":"Tog bort {activations} aktiveringshändelser och {audits} granskningshändelser.","retentionLeaseBusy":"En annan hanteringsreplik äger lagringslåset.","inboundSecurity":"Inkommande säkerhet","hsts":"HTTP Strict Transport Security","hstsLead":"Tillämpa en konsekvent HTTPS-policy för webbläsare på valda publika värdnamn.","hstsInfo":"HSTS är en global värdnamnspolicy som delas mellan miljöer, inte en route- eller sökvägspolicy. Varje HTTPS-svar för ett matchande värdnamn får samma header. Aktivera den först när HTTPS fungerar tillförlitligt.","hstsEnabled":"Aktivera HSTS","hstsEnabledHint":"Inaktiverat som standard för att förhindra oavsiktlig låsning i webbläsaren.","hstsHosts":"Skyddade värdnamn","hstsHostsHint":"Lägg till exakta värdnamn eller jokertecken som *.example.com.","maxAge":"Maximal ålder (sekunder)","maxAgeHint":"Ett år är 31536000 sekunder.","includeSubDomains":"Inkludera underdomäner","includeSubDomainsHint":"Tillämpa policyn på alla underdomäner till varje konfigurerat värdnamn.","preload":"Begär kvalificering för preload","preloadHint":"Kräver underdomäner och en maximal ålder på minst ett år.","saveHsts":"Spara HSTS-inställningar","hstsSaved":"HSTS-inställningarna har sparats."}
}
</i18n>

<style scoped>
.settings-tabs {
  border-bottom: 1px solid var(--border);
}
.settings-window {
  overflow: visible;
}
.card-subtitle {
  margin-top: 2px;
  color: var(--muted-foreground);
  font-size: 0.8125rem;
  font-weight: 400;
}
.settings-subsection-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}
.default-state-card,
.management-block,
.table-frame {
  border: 1px solid var(--border);
  border-radius: 8px;
}
.default-state-card {
  min-height: 146px;
  padding: 16px;
}
.management-block {
  height: 100%;
  padding: 16px;
}
.table-frame {
  overflow: hidden;
}
.setting-row {
  border-radius: 8px;
}
.settings-action {
  height: 40px !important;
  min-width: 144px;
  font-size: 0.875rem !important;
  letter-spacing: normal;
}
@media (max-width: 600px) {
  .settings-subsection-header {
    align-items: stretch;
    flex-direction: column;
  }
  .settings-action {
    width: 100%;
  }
}
</style>
