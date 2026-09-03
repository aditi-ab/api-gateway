<template>
  <div class="page">
    <div class="flex items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ t('eyebrow') }}
        </p><h1>{{ t('title') }}</h1><p class="page-lead">
          {{ t('lead') }}
        </p>
      </div><div class="flex gap-2">
        <Button variant="outline" :disabled="!selectedEnvironmentId" @click="importExpanded = !importExpanded">
          <FileInput />{{ t('import') }}
        </Button><Button :disabled="!selectedEnvironmentId" @click="createDialog = true">
          <Plus />{{ t('addRoute') }}
        </Button>
      </div>
    </div>
    <Alert v-if="error" variant="destructive" class="mt-5">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert><EnvironmentRequiredAlert v-else-if="!selectedEnvironmentId" />
    <Collapsible v-model:open="importExpanded">
      <CollapsibleContent>
        <Card class="mt-5">
          <CardHeader><CardTitle>{{ t('importRoutes') }}</CardTitle></CardHeader><CardContent class="grid gap-4">
            <Alert v-if="importError" variant="destructive">
              <CircleAlert /><AlertDescription>{{ importError }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="route-import-upstream">
                {{ t('upstreamUrl') }}
              </FieldLabel><Input id="route-import-upstream" v-model="importUpstream" placeholder="https://service.example/" />
            </Field>
            <Field>
              <FieldLabel for="route-import-source">
                {{ t('openApiSource') }}
              </FieldLabel><Textarea id="route-import-source" v-model="importSource" rows="8" />
            </Field>
            <Button class="justify-self-start" :disabled="!importSource || !importUpstream || importing" @click="previewImport">
              <Spinner v-if="importing" />{{ t('previewImport') }}
            </Button>
            <Table v-if="importPreview.length">
              <TableHeader><TableRow><TableHead /><TableHead>{{ t('route') }}</TableHead><TableHead>{{ t('methodsPath') }}</TableHead><TableHead>{{ t('status') }}</TableHead></TableRow></TableHeader><TableBody>
                <TableRow v-for="item in importPreview" :key="item.id">
                  <TableCell><Checkbox :model-value="selectedImports.includes(item.id)" :disabled="item.conflicts" @update:model-value="toggleImportSelection(item.id, !!$event)" /></TableCell><TableCell>{{ item.id }}</TableCell><TableCell>{{ item.methods.join(', ') }} <code>{{ item.path }}</code></TableCell><TableCell>{{ item.conflicts ? t('alreadyExists') : t('ready') }}</TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </CardContent><CardFooter v-if="importPreview.length" class="justify-end">
            <Button :disabled="!selectedImports.length || importing" @click="applyImport">
              <Spinner v-if="importing" />{{ isStaged ? tg('common.importUnpublished') : t('importActivate') }}
            </Button>
          </CardFooter>
        </Card>
      </CollapsibleContent>
    </Collapsible>
    <Card class="data-panel mt-6 py-0">
      <CardContent class="p-4">
        <Field>
          <FieldLabel for="route-search">
            {{ t('searchRoutes') }}
          </FieldLabel><InputGroup>
            <InputGroupAddon><Search /></InputGroupAddon><InputGroupInput id="route-search" v-model="filter" /><InputGroupAddon v-if="filter" align="inline-end">
              <Tooltip>
                <TooltipTrigger as-child>
                  <InputGroupButton :aria-label="t('clearSearch')" @click="filter = ''">
                    <X />
                  </InputGroupButton>
                </TooltipTrigger><TooltipContent>{{ t('clearSearch') }}</TooltipContent>
              </Tooltip>
            </InputGroupAddon>
          </InputGroup>
        </Field>
      </CardContent>
      <Table v-if="filtered.length" class="routes-table">
        <TableHeader>
          <TableRow>
            <TableHead>{{ t('route') }}</TableHead><TableHead>{{ t('trafficFlow') }}</TableHead><TableHead class="state-column">
              {{ t('trafficState') }}
            </TableHead><TableHead>{{ t('enabled') }}</TableHead><TableHead class="expand-column" />
          </TableRow>
        </TableHeader>
        <TableBody v-for="route in filtered" :key="route.id">
          <TableRow class="click-row" tabindex="0" :aria-expanded="expanded.has(route.id)" :aria-controls="`route-details-${route.id}`" @click="toggleExpanded(route.id)" @keydown.enter="toggleExpanded(route.id)">
            <TableCell>
              <strong>{{ route.name }}</strong>
            </TableCell><TableCell>
              <div class="flow-summary">
                <span class="flow-endpoint truncate"><a v-if="routeTestUrls(route).length" class="route-url" :href="routeTestUrls(route)[0]" target="_blank" rel="noopener noreferrer" @click.stop @keydown.stop>{{ incomingSummary(route) }}</a><span v-else>{{ incomingSummary(route) }}</span><span v-if="additionalIncomingCount(route)"> +{{ additionalIncomingCount(route) }}</span></span><ArrowRight class="size-4" /><span class="flow-endpoint truncate"><a class="route-url" :href="destinations(route)[0]?.address" target="_blank" rel="noopener noreferrer" @click.stop @keydown.stop>{{ destinationSummary(route) }}</a><span v-if="destinations(route).length > 1"> +{{ destinations(route).length - 1 }}</span></span>
              </div>
            </TableCell><TableCell class="state-column" @click.stop @keydown.stop>
              <div class="flex items-center gap-2">
                <Button size="sm" :variant="stateVariant(route.operations.state)" :disabled="changingStateRouteId === route.id" @click="openStateDialog(route)">
                  <Spinner v-if="changingStateRouteId === route.id" />{{ stateLabel(route.operations.state) }}
                </Button><div class="text-xs text-muted-foreground">
                  <div class="whitespace-nowrap">
                    {{ t('activeRequests', { count: activeRequests(route.id) }) }}
                  </div><div v-if="route.operations.state !== 'ONLINE'" class="whitespace-nowrap">
                    {{ responseLabel(route) }}
                  </div>
                </div>
              </div>
            </TableCell><TableCell>
              <div class="flex items-center gap-2" @click.stop @keydown.stop>
                <Switch :model-value="route.enabled" :disabled="togglingRouteId === route.id" :aria-label="t(route.enabled ? 'disableRoute' : 'enableRoute', { name: route.name })" @update:model-value="setRouteEnabled(route, !!$event)" />
                <span class="text-xs">{{ route.enabled ? t('live') : t('disabled') }}</span>
              </div>
            </TableCell><TableCell class="expand-column" @click.stop @keydown.stop>
              <div class="route-actions">
                <IconButton variant="secondary" :disabled="duplicatingRouteId === route.id" :label="t('duplicateNamed', { name: route.name })" @click="openDuplicate(route)">
                  <Copy />
                </IconButton>
                <IconButton variant="secondary" :label="`${t('edit')} ${route.name}`" @click="open(route.id)">
                  <Pencil />
                </IconButton>
                <IconButton :aria-expanded="expanded.has(route.id)" :aria-controls="`route-details-${route.id}`" :label="t(expanded.has(route.id) ? 'hideDetails' : 'showDetails', { name: route.name })" @click="toggleExpanded(route.id)">
                  <ChevronDown class="transition-transform" :class="expanded.has(route.id) && 'rotate-180'" />
                </IconButton>
              </div>
            </TableCell>
          </TableRow>
          <TableRow v-if="expanded.has(route.id)" :id="`route-details-${route.id}`">
            <TableCell :colspan="5">
              <div class="route-flow-details">
                <section>
                  <div class="detail-label">
                    {{ t('incomingMatches') }}
                  </div><template v-if="routeTestUrls(route).length">
                    <div v-for="url in routeTestUrls(route)" :key="url" class="mb-1">
                      <a class="route-url mono text-sm" :href="url" target="_blank" rel="noopener noreferrer" @click.stop @keydown.stop>{{ url }}</a>
                    </div>
                  </template><div v-for="host in wildcardHosts(route)" :key="host" class="mono text-sm mb-1">
                    {{ host }}{{ route.match.path }}
                  </div><div v-if="!route.match.hosts.length" class="mono text-sm mb-1">
                    {{ incomingSummary(route) }}
                  </div><div class="text-xs text-muted-foreground mt-2">
                    {{ route.match.methods.length ? route.match.methods.join(', ') : t('allMethods') }}
                  </div>
                </section>
                <ArrowRight class="flow-arrow size-4" /><section>
                  <div class="detail-label">
                    {{ t('forwardsTo') }}
                  </div><div v-for="destination in destinations(route)" :key="destination.id" class="mb-2">
                    <a class="route-url mono text-sm" :href="destination.address" target="_blank" rel="noopener noreferrer" @click.stop @keydown.stop>{{ destination.address }}</a><span class="text-xs text-muted-foreground"> ({{ destination.id }})</span>
                  </div><div class="text-xs text-muted-foreground">
                    {{ t('loadBalancingSummary', { policy: route.upstream.loadBalancingPolicy, count: destinations(route).length }, destinations(route).length) }}
                  </div>
                </section>
                <section>
                  <div class="detail-label">
                    {{ t('gatewayFeatures') }}
                  </div><div class="flex flex-wrap gap-1">
                    <Badge v-for="feature in featureNames(route)" :key="feature" variant="secondary">
                      {{ feature }}
                    </Badge><span v-if="!featureNames(route).length" class="text-muted-foreground">{{ t('simpleForwarding') }}</span>
                  </div>
                </section>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table><Empty v-else>
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <Route />
          </EmptyMedia><EmptyTitle>{{ t('noRoutes') }}</EmptyTitle><EmptyDescription>{{ t('noRoutesText') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>
    </Card>
    <Dialog v-model:open="createDialog">
      <DialogContent size="xl">
        <DialogHeader><DialogTitle>{{ t('addRoute') }}</DialogTitle></DialogHeader><Alert v-if="dialogError" variant="destructive">
          <CircleAlert /><AlertDescription>{{ dialogError }}</AlertDescription>
        </Alert><FieldGroup>
          <Field>
            <FieldLabel for="new-route-name">
              {{ t('name') }}
            </FieldLabel><Input id="new-route-name" v-model="form.name" autofocus />
          </Field><Field>
            <FieldLabel for="new-route-path">
              {{ t('incomingPath') }}
            </FieldLabel><Input id="new-route-path" v-model="form.path" placeholder="/api" /><FieldDescription>{{ t('incomingPathHint') }}</FieldDescription>
          </Field><Field orientation="horizontal">
            <Switch id="new-route-subpaths" v-model="form.matchSubpaths" /><FieldContent>
              <FieldLabel for="new-route-subpaths">
                {{ t('matchSubpaths') }}
              </FieldLabel><FieldDescription>{{ t('matchSubpathsHint') }}</FieldDescription>
            </FieldContent>
          </Field><Field>
            <FieldLabel for="new-route-upstream-source">
              {{ t('upstream') }}
            </FieldLabel><Select v-model="form.upstreamId">
              <SelectTrigger id="new-route-upstream-source">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem value="__manual">
                  {{ t('manualUpstream') }}
                </SelectItem><SelectItem v-for="upstream in upstreams" :key="upstream.id" :value="upstream.id">
                  {{ upstream.name }}
                </SelectItem>
              </SelectContent>
            </Select><FieldDescription>{{ t('upstreamChoiceHelp') }}</FieldDescription>
          </Field><Field v-if="form.upstreamId === '__manual'">
            <FieldLabel for="new-route-upstream">
              {{ t('upstreamUrl') }}
            </FieldLabel><Input id="new-route-upstream" v-model="form.upstreamUrl" placeholder="https://service.example/" />
          </Field>
        </FieldGroup><DialogFooter>
          <Button variant="outline" @click="createDialog = false">
            {{ t('cancel') }}
          </Button><Button :disabled="saving || !form.name || !form.path || (form.upstreamId === '__manual' && !form.upstreamUrl)" @click="createRoute">
            <Spinner v-if="saving" />{{ isStaged ? tg('common.createUnpublished') : t('createActivate') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <Dialog v-model:open="duplicateDialog">
      <DialogContent size="lg">
        <DialogHeader><DialogTitle>{{ t('duplicateRoute') }}</DialogTitle></DialogHeader><Alert><Info /><AlertDescription>{{ t('duplicateInfo') }}</AlertDescription></Alert><Field>
          <FieldLabel for="duplicate-route-name">
            {{ t('name') }}
          </FieldLabel><Input id="duplicate-route-name" v-model="duplicateName" maxlength="128" autofocus @keydown.enter="duplicateSelectedRoute" />
        </Field><DialogFooter>
          <Button variant="outline" :disabled="!!duplicatingRouteId" @click="duplicateDialog = false">
            {{ t('cancel') }}
          </Button><Button :disabled="!!duplicatingRouteId || !duplicateName.trim()" @click="duplicateSelectedRoute">
            <Spinner v-if="duplicatingRouteId" />{{ t('duplicate') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <Dialog v-model:open="stateDialog">
      <DialogContent size="xl" aria-labelledby="route-state-title" data-testid="route-state-dialog">
        <DialogHeader>
          <DialogTitle id="route-state-title">
            {{ stateRoute ? t('changeStateFor', { name: stateRoute.name }) : t('changeState') }}
          </DialogTitle>
        </DialogHeader><Alert><Info /><AlertDescription>{{ t('stateHelp') }}</AlertDescription></Alert><FieldGroup>
          <Field>
            <FieldLabel for="route-state">
              {{ t('trafficState') }}
            </FieldLabel><Select v-model="stateForm.state">
              <SelectTrigger id="route-state">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="option in stateOptions" :key="option.value" :value="option.value">
                  {{ option.title }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field><Field v-if="stateForm.state !== 'ONLINE'">
            <FieldLabel for="route-response-profile">
              {{ t('unavailableResponse') }}
            </FieldLabel><Select v-model="stateForm.responseProfileId">
              <SelectTrigger id="route-response-profile">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="option in responseProfileOptions" :key="String(option.value)" :value="option.value">
                  {{ option.title }}
                </SelectItem>
              </SelectContent>
            </Select><FieldDescription>{{ t('unavailableHint') }}</FieldDescription>
          </Field>
        </FieldGroup><DialogFooter>
          <Button variant="outline" @click="stateDialog = false">
            {{ t('cancel') }}
          </Button><Button :disabled="savingState" @click="saveState">
            <Spinner v-if="savingState" />{{ t('saveActivate') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Badge, Button, Card, CardContent, CardFooter, CardHeader, CardTitle, Checkbox, Collapsible, CollapsibleContent, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle, Field, FieldContent, FieldDescription, FieldGroup, FieldLabel, Input, InputGroup, InputGroupAddon, InputGroupButton, InputGroupInput, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Spinner, Switch, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Textarea, Tooltip, TooltipContent, TooltipTrigger } from '@aditify/ui';
import { ArrowRight, ChevronDown, CircleAlert, Copy, FileInput, Info, Pencil, Plus, Route, Search, X } from '@lucide/vue';
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';
import { graphql } from '../api';
import EnvironmentRequiredAlert from '../components/EnvironmentRequiredAlert.vue';
import IconButton from '../components/IconButton.vue';
import { editableRevisionId, loadEnvironments, selectedEnvironment, selectedEnvironmentId } from '../composables/environmentContext';
import { buildRoutePath } from '../utils/routePaths';
import { routeTestUrls } from '../utils/routeUrls';

interface RouteRow { id: string; name: string; version: string; enabled: boolean; inbound: { scheme: 'ANY' | 'HTTP_ONLY' | 'HTTPS_REDIRECT' }; match: { path: string; methods: string[]; hosts: string[] }; upstream: { url: string; loadBalancingPolicy: string; destinations?: Array<{ key: string; value: { address: string } }> }; features: Record<string, unknown>; operations: { state: string; responseProfileId?: string; response?: { statusCode: number; title?: string; message?: string; retryAfter?: string; upstreamUrl?: string } } }
interface RuntimeStatus { routeId: string; activeRequests: number; reportingInstances: number }
interface ResponseProfile { id: string; name: string }
interface UpstreamOption { id: string; name: string }

const router = useRouter(); const routes = ref<RouteRow[]>([]); const upstreams = ref<UpstreamOption[]>([]); const filter = ref(''); const error = ref(''); const dialogError = ref(''); const saving = ref(false); const createDialog = ref(false); const importExpanded = ref(false); const form = ref({ name: '', path: '/', matchSubpaths: true, upstreamUrl: 'https://', upstreamId: '__manual' });
const { t } = useI18n();
const { t: tg } = useI18n({ useScope: 'global' });
const isStaged = computed(() => selectedEnvironment.value?.publishingMode === 'STAGED');
const importSource = ref(''); const importUpstream = ref('https://'); const importPreview = ref<Array<{ id: string; path: string; methods: string[]; conflicts: boolean }>>([]); const selectedImports = ref<string[]>([]); const previewToken = ref(''); const importing = ref(false); const importError = ref('');
const togglingRouteId = ref('');
const changingStateRouteId = ref(''); const stateDialog = ref(false); const savingState = ref(false); const stateRoute = ref<RouteRow | null>(null); const runtimeStatuses = ref<RuntimeStatus[]>([]);
const responseProfiles = ref<ResponseProfile[]>([]); const expanded = ref(new Set<string>());
const duplicateDialog = ref(false); const duplicateRoute = ref<RouteRow | null>(null); const duplicateName = ref(''); const duplicatingRouteId = ref('');
const stateOptions = computed(() => [{ title: t('online'), value: 'ONLINE' }, { title: t('draining'), value: 'DRAINING' }, { title: t('maintenance'), value: 'MAINTENANCE' }, { title: t('offline'), value: 'OFFLINE' }]);
const responseProfileOptions = computed(() => [{ title: t('useEnvironmentDefault'), value: null }, ...responseProfiles.value.map(x => ({ title: x.name, value: x.id }))]);
const stateForm = ref({ state: 'ONLINE', responseProfileId: null as string | null });
const filtered = computed(() => routes.value.filter(x => !filter.value || `${x.name} ${x.id} ${x.match.path} ${x.upstream.url}`.toLowerCase().includes(filter.value.toLowerCase())));

function featureNames(route: RouteRow) {
  const labels: Record<string, string> = { authorization: t('authentication'), rateLimit: t('rateLimit'), timeout: t('timeout'), resilience: t('resilience'), cors: 'CORS', transforms: t('transforms'), mirror: t('mirroring'), access: t('accessControls'), requestValidation: t('validation'), responseCache: t('cache') };

  const disabled = new Set((route.features.disabledFeatures as string[] | undefined) || []);

  return Object.entries(route.features).filter(([key, value]) => key !== 'disabledFeatures' && !disabled.has(featureId(key)) && value && (!Array.isArray(value) || value.length)).map(([key]) => labels[key] || key);
}
function featureId(key: string) {
  return ({ rateLimit: 'rate-limit', requestValidation: 'request-validation', responseCache: 'response-cache' } as Record<string, string>)[key] || key;
}
function stateLabel(state: string) { return t(state.toLowerCase()); }
function stateVariant(state: string): 'success' | 'warning' | 'destructive' { return state === 'ONLINE' ? 'success' : state === 'DRAINING' ? 'warning' : 'destructive'; }
function toggleImportSelection(id: string, selected: boolean) { selectedImports.value = selected ? [...new Set([...selectedImports.value, id])] : selectedImports.value.filter(value => value !== id); }
function responseLabel(route: RouteRow) { return route.operations.responseProfileId ? responseProfiles.value.find(x => x.id === route.operations.responseProfileId)?.name || route.operations.responseProfileId : route.operations.response ? t('legacyRouteResponse') : t('environmentDefault'); }
function activeRequests(routeId: string) { return runtimeStatuses.value.find(x => x.routeId === routeId)?.activeRequests || 0; }
function toggleExpanded(routeId: string) {
  const next = new Set(expanded.value);

  if (next.has(routeId))
    next.delete(routeId); else next.add(routeId);

  expanded.value = next;
}
function incomingSummary(route: RouteRow) {
  const urls = routeTestUrls(route);

  if (urls.length)
    return urls[0]!;

  return `${route.match.hosts[0] || t('anyHost')}${route.match.path}`;
}
function wildcardHosts(route: RouteRow) { return route.match.hosts.filter(host => host.includes('*')); }
function additionalIncomingCount(route: RouteRow) { return Math.max(0, routeTestUrls(route).length - 1) + wildcardHosts(route).length; }
function destinationSummary(route: RouteRow) {
  const values = destinations(route);

  return values[0]?.address || route.upstream.url;
}
function destinations(route: RouteRow) { return route.upstream.destinations?.map(x => ({ id: x.key, address: x.value.address })) || [{ id: 'primary', address: route.upstream.url }]; }
async function load() {
  if (!selectedEnvironmentId.value) { routes.value = []; return; }

  error.value = '';

  try {
    const data = await graphql<{ routes: RouteRow[]; upstreams: UpstreamOption[]; routeRuntimeStatuses: RuntimeStatus[]; routeUnavailableResponseProfiles: ResponseProfile[] }>(`query Routes($environmentId:UUID!){routes(environmentId:$environmentId){id name version enabled inbound{scheme} operations{state responseProfileId response{statusCode title message retryAfter upstreamUrl}} match{path methods hosts} upstream{url loadBalancingPolicy destinations{key value{address}}} features{disabledFeatures authorization{type} rateLimit{type} timeout{total} resilience{retryCount} cors{origins} transforms{key value} mirror{percentage} access{allowedCidrs deniedCidrs maximumRequestBodyBytes} requestValidation{maximumBodyBytes} responseCache{timeToLive}}} upstreams(environmentId:$environmentId){id name} routeRuntimeStatuses(environmentId:$environmentId){routeId activeRequests reportingInstances} routeUnavailableResponseProfiles(environmentId:$environmentId){id name}}`, { environmentId: selectedEnvironmentId.value });

    routes.value = data.routes; upstreams.value = data.upstreams; runtimeStatuses.value = data.routeRuntimeStatuses; responseProfiles.value = data.routeUnavailableResponseProfiles;
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}
async function refreshRuntimeStatuses() {
  if (!selectedEnvironmentId.value)
    return;

  try { runtimeStatuses.value = (await graphql<{ routeRuntimeStatuses: RuntimeStatus[] }>(`query RouteRuntimeStatuses($environmentId:UUID!){routeRuntimeStatuses(environmentId:$environmentId){routeId activeRequests reportingInstances}}`, { environmentId: selectedEnvironmentId.value })).routeRuntimeStatuses; }
  catch { /* The route list remains usable during a transient diagnostics failure. */ }
}
function openStateDialog(route: RouteRow) {
  stateRoute.value = route; stateForm.value = { state: route.operations.state, responseProfileId: route.operations.responseProfileId || null }; stateDialog.value = true;
}
async function saveState() {
  const route = stateRoute.value;

  if (!route)
    return;

  savingState.value = true; changingStateRouteId.value = route.id; error.value = '';

  try {
    const v = stateForm.value; const input = { state: v.state, responseProfileId: v.responseProfileId, useEnvironmentDefault: !v.responseProfileId };

    await graphql(`mutation SetRouteOperationalState($environmentId:UUID!,$routeId:String!,$version:String!,$input:UpdateRouteOperationalStateInput!){setRouteOperationalState(environmentId:$environmentId,routeId:$routeId,expectedRouteVersion:$version,input:$input){route{id version operations{state}}}}`, { environmentId: selectedEnvironmentId.value, routeId: route.id, version: route.version, input });
    stateDialog.value = false; await load(); await loadEnvironments();
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
  finally { savingState.value = false; changingStateRouteId.value = ''; }
}
async function createRoute() {
  saving.value = true; dialogError.value = '';

  try {
    const input = { name: form.value.name, path: buildRoutePath(form.value.path, form.value.matchSubpaths), upstreamUrl: form.value.upstreamId === '__manual' ? form.value.upstreamUrl : null, upstreamId: form.value.upstreamId === '__manual' ? null : form.value.upstreamId };
    const result = await graphql<{ createRoute: { route: { id: string } } }>(`mutation CreateRoute($environmentId:UUID!,$input:CreateManagedRouteInput!){createRoute(environmentId:$environmentId,input:$input){route{id}}}`, { environmentId: selectedEnvironmentId.value, input });

    createDialog.value = false; form.value = { name: '', path: '/', matchSubpaths: true, upstreamUrl: 'https://', upstreamId: '__manual' }; await loadEnvironments(); await router.push(`/routes/${result.createRoute.route.id}`);
  }
  catch (e) { dialogError.value = e instanceof Error ? e.message : String(e); }
  finally { saving.value = false; }
}
async function setRouteEnabled(route: RouteRow, enabled: boolean) {
  togglingRouteId.value = route.id; error.value = '';

  try {
    const data = await graphql<{ setRouteEnabled: { route: RouteRow } }>(`mutation SetRouteEnabled($environmentId:UUID!,$routeId:String!,$version:String!,$enabled:Boolean!){setRouteEnabled(environmentId:$environmentId,routeId:$routeId,expectedRouteVersion:$version,enabled:$enabled){route{id version enabled}}}`, { environmentId: selectedEnvironmentId.value, routeId: route.id, version: route.version, enabled });

    route.enabled = data.setRouteEnabled.route.enabled; route.version = data.setRouteEnabled.route.version;
    await loadEnvironments();
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); await load(); }
  finally { togglingRouteId.value = ''; }
}
function openDuplicate(route: RouteRow) { duplicateRoute.value = route; duplicateName.value = t('copyName', { name: route.name }); duplicateDialog.value = true; }
async function duplicateSelectedRoute() {
  const route = duplicateRoute.value;

  if (!route || !duplicateName.value.trim() || duplicatingRouteId.value)
    return;

  duplicatingRouteId.value = route.id; error.value = '';

  try {
    const data = await graphql<{ duplicateRoute: { route: { id: string } } }>(`mutation DuplicateRoute($environmentId:UUID!,$routeId:String!,$version:String!,$name:String!){duplicateRoute(environmentId:$environmentId,routeId:$routeId,expectedRouteVersion:$version,name:$name){route{id}}}`, { environmentId: selectedEnvironmentId.value, routeId: route.id, version: route.version, name: duplicateName.value });

    duplicateDialog.value = false; await loadEnvironments(); await router.push(`/routes/${data.duplicateRoute.route.id}`);
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); await load(); }
  finally { duplicatingRouteId.value = ''; }
}
async function previewImport() {
  const version = editableRevisionId.value;

  if (!version) { importError.value = t('createBeforeImport'); return; }

  importing.value = true; importError.value = '';

  try {
    const data = await graphql<{ previewOpenApiRoutes: { token: string; routes: Array<{ id: string; path: string; methods: string[]; conflicts: boolean }> } }>(`mutation PreviewOpenApi($environmentId:UUID!,$version:UUID!,$source:String!,$upstream:String!){previewOpenApiRoutes(environmentId:$environmentId,expectedConfigurationVersion:$version,source:$source,upstreamUrl:$upstream){token routes{id path methods conflicts}}}`, { environmentId: selectedEnvironmentId.value, version, source: importSource.value, upstream: importUpstream.value });

    previewToken.value = data.previewOpenApiRoutes.token; importPreview.value = data.previewOpenApiRoutes.routes; selectedImports.value = importPreview.value.filter(x => !x.conflicts).map(x => x.id);
  }
  catch (e) { importError.value = e instanceof Error ? e.message : String(e); }
  finally { importing.value = false; }
}
async function applyImport() {
  importing.value = true; importError.value = '';

  try {
    await graphql(`mutation ApplyOpenApi($token:String!,$routes:[String!]!){applyOpenApiRoutes(previewToken:$token,routeIds:$routes){revision{id}}}`, { token: previewToken.value, routes: selectedImports.value });
    importPreview.value = []; importSource.value = ''; await loadEnvironments(); await load();
  }
  catch (e) { importError.value = e instanceof Error ? e.message : String(e); }
  finally { importing.value = false; }
}
function open(id: string) { void router.push(`/routes/${id}`); }

let runtimeRefresh: ReturnType<typeof setInterval> | undefined;

watch(selectedEnvironmentId, load); onMounted(async () => {
  if (!selectedEnvironmentId.value)
    await loadEnvironments();

  await load();
  runtimeRefresh = setInterval(() => void refreshRuntimeStatuses(), 5000);
});
onUnmounted(() => clearInterval(runtimeRefresh));
</script>

<i18n lang="json">
{
  "en": { "eyebrow": "Traffic configuration", "title": "Routes", "lead": "Send incoming requests to an upstream, then add gateway features when you need them.", "import": "Import", "addRoute": "Add route", "createEnvironment": "Create environment", "environmentRequired": "Create an environment before adding or importing routes.", "importRoutes": "Import routes", "upstreamUrl": "Upstream URL", "openApiSource": "OpenAPI JSON or YAML", "previewImport": "Preview import", "route": "Route", "methodsPath": "Methods and path", "status": "Status", "alreadyExists": "Already exists", "ready": "Ready", "importActivate": "Import and activate", "searchRoutes": "Search routes", "trafficFlow": "Traffic flow", "trafficState": "Traffic state", "enabled": "Enabled", "activeRequests": "{count} active", "disableRoute": "Disable route {name}", "enableRoute": "Enable route {name}", "live": "Live", "disabled": "Disabled", "hideDetails": "Hide traffic details for {name}", "showDetails": "Show traffic details for {name}", "edit": "Edit", "incomingMatches": "Incoming matches", "allMethods": "All methods", "forwardsTo": "Forwards to", "loadBalancingSummary": "{policy} across {count} destination | {policy} across {count} destinations", "gatewayFeatures": "Gateway features", "simpleForwarding": "Simple forwarding", "noRoutes": "No routes", "noRoutesText": "Add a route with a name, path, and upstream URL.", "name": "Name", "incomingPath": "Incoming path", "incomingPathHint": "Matches all HTTP methods by default.", "matchSubpaths": "Match this path and all subpaths", "matchSubpathsHint": "Adds the catch-all route pattern automatically when saved.", "cancel": "Cancel", "createActivate": "Create and activate", "changeStateFor": "Change traffic state for {name}", "changeState": "Change traffic state", "stateHelp": "Online forwards normally. Draining lets active requests finish while rejecting new requests. Maintenance and Offline override route features and normal upstream selection.", "unavailableResponse": "Unavailable response", "unavailableHint": "Inherit the environment default or override it with a shared response.", "saveActivate": "Save and activate", "online": "Online", "draining": "Draining", "maintenance": "Maintenance", "offline": "Offline", "useEnvironmentDefault": "Use environment default", "authentication": "Authentication", "rateLimit": "Rate limit", "timeout": "Timeout", "resilience": "Resilience", "transforms": "Transforms", "mirroring": "Mirroring", "accessControls": "Access controls", "validation": "Validation", "cache": "Cache", "legacyRouteResponse": "Legacy route response", "environmentDefault": "Environment default", "anyHost": "Any host", "createBeforeImport": "Create a route before importing into this environment." },
  "sv": { "eyebrow": "Trafikkonfiguration", "title": "Routes", "lead": "Skicka inkommande anrop till en upstream och lägg till gatewayfunktioner vid behov.", "import": "Importera", "addRoute": "Lägg till route", "createEnvironment": "Skapa miljö", "environmentRequired": "Skapa en miljö innan du lägger till eller importerar routes.", "importRoutes": "Importera routes", "upstreamUrl": "Upstream-URL", "openApiSource": "OpenAPI JSON eller YAML", "previewImport": "Förhandsgranska import", "route": "Route", "methodsPath": "Metoder och sökväg", "status": "Status", "alreadyExists": "Finns redan", "ready": "Klar", "importActivate": "Importera och aktivera", "searchRoutes": "Sök routes", "trafficFlow": "Trafikflöde", "trafficState": "Trafikläge", "enabled": "Aktiverad", "activeRequests": "{count} aktiva", "disableRoute": "Inaktivera route {name}", "enableRoute": "Aktivera route {name}", "live": "Live", "disabled": "Inaktiverad", "hideDetails": "Dölj trafikdetaljer för {name}", "showDetails": "Visa trafikdetaljer för {name}", "edit": "Redigera", "incomingMatches": "Inkommande matchningar", "allMethods": "Alla metoder", "forwardsTo": "Vidarebefordrar till", "loadBalancingSummary": "{policy} över {count} destination | {policy} över {count} destinationer", "gatewayFeatures": "Gatewayfunktioner", "simpleForwarding": "Enkel vidarebefordran", "noRoutes": "Inga routes", "noRoutesText": "Lägg till en route med namn, sökväg och upstream-URL.", "name": "Namn", "incomingPath": "Inkommande sökväg", "incomingPathHint": "Matchar alla HTTP-metoder som standard.", "matchSubpaths": "Matcha sökvägen och alla undersökvägar", "matchSubpathsHint": "Lägger automatiskt till routens jokerteckenmönster när den sparas.", "cancel": "Avbryt", "createActivate": "Skapa och aktivera", "changeStateFor": "Ändra trafikläge för {name}", "changeState": "Ändra trafikläge", "stateHelp": "Online vidarebefordrar normalt. Dränering låter aktiva anrop slutföras medan nya avvisas. Underhåll och Offline åsidosätter routefunktioner och normalt upstreamval.", "unavailableResponse": "Otillgänglighetssvar", "unavailableHint": "Ärv miljöns standard eller åsidosätt med ett delat svar.", "saveActivate": "Spara och aktivera", "online": "Online", "draining": "Dränering", "maintenance": "Underhåll", "offline": "Offline", "useEnvironmentDefault": "Använd miljöns standard", "authentication": "Autentisering", "rateLimit": "Hastighetsbegränsning", "timeout": "Tidsgräns", "resilience": "Feltålighet", "transforms": "Transformeringar", "mirroring": "Spegling", "accessControls": "Åtkomstkontroller", "validation": "Validering", "cache": "Cache", "legacyRouteResponse": "Äldre routesvar", "environmentDefault": "Miljöstandard", "anyHost": "Valfri värd", "createBeforeImport": "Skapa en route innan du importerar till denna miljö." }
}
</i18n>

<i18n lang="json">
{
  "en": {
    "duplicate": "Duplicate",
    "duplicateRoute": "Duplicate route",
    "duplicateNamed": "Duplicate {name}",
    "duplicateInfo": "The complete route configuration will be copied. The new route starts disabled so you can review it before it receives traffic.",
    "copyName": "{name} copy"
  },
  "sv": {
    "duplicate": "Duplicera",
    "duplicateRoute": "Duplicera route",
    "duplicateNamed": "Duplicera {name}",
    "duplicateInfo": "Hela routekonfigurationen kopieras. Den nya routen är inaktiverad från början så att du kan granska den innan den tar emot trafik.",
    "copyName": "Kopia av {name}"
  }
}
</i18n>

<i18n lang="json">
{
  "en": { "upstream": "Upstream", "manualUpstream": "Enter a URL directly", "upstreamChoiceHelp": "Select a reusable upstream, or enter one server URL for this route." },
  "sv": { "upstream": "Upstream", "manualUpstream": "Ange en URL direkt", "upstreamChoiceHelp": "Välj en återanvändbar upstream eller ange en server-URL för denna route." }
}
</i18n>

<style scoped>
.routes-table {
  min-width: 900px;
}
.state-column {
  width: 220px;
  min-width: 220px;
}
.expand-column {
  width: 176px;
}
.route-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 4px;
}
.flow-summary {
  display: grid;
  grid-template-columns: minmax(120px, 1fr) auto minmax(140px, 1fr);
  align-items: center;
  gap: 12px;
  max-width: 680px;
}
.flow-endpoint {
  min-width: 0;
}
.route-url {
  color: var(--primary);
  text-decoration: none;
}
.route-url:hover,
.route-url:focus-visible {
  text-decoration: underline;
}
.route-flow-details {
  display: grid;
  grid-template-columns: minmax(220px, 1fr) auto minmax(260px, 1fr) minmax(220px, 0.8fr);
  gap: 20px;
  align-items: center;
  padding: 20px;
  border: thin solid var(--border);
  border-radius: 8px;
  background: color-mix(in srgb, var(--muted) 55%, transparent);
}
.detail-label {
  margin-bottom: 8px;
  color: var(--muted-foreground);
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}
@media (max-width: 1100px) {
  .route-flow-details {
    grid-template-columns: 1fr;
  }
  .flow-arrow {
    transform: rotate(90deg);
  }
}
</style>
