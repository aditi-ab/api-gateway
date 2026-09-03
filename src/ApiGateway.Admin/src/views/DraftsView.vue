<template>
  <div class="page-container">
    <div class="flex items-center flex-wrap gap-2">
      <div>
        <h1>
          Draft editor <Badge v-if="dirty" variant="warning">
            Unsaved
          </Badge>
        </h1><p class="text-muted-foreground">
          Use guided forms or edit the complete versioned JSON document.
        </p>
      </div><div class="ml-auto flex flex-wrap items-end gap-2">
        <Field class="w-62">
          <FieldLabel for="draft-environment">
            Environment
          </FieldLabel><Select v-model="environmentId">
            <SelectTrigger id="draft-environment">
              <SelectValue />
            </SelectTrigger><SelectContent>
              <SelectItem v-for="environment in environments" :key="environment.id" :value="environment.id">
                {{ environment.displayName }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field><Field v-if="revisions.length" class="w-45">
          <FieldLabel for="draft-selection">
            Draft
          </FieldLabel><Select v-model="selectedId" @update:model-value="selectById">
            <SelectTrigger id="draft-selection">
              <SelectValue />
            </SelectTrigger><SelectContent>
              <SelectItem v-for="revision in revisions" :key="revision.id" :value="revision.id">
                Draft {{ revision.number }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field><Button v-if="selected" variant="outline" @click="importDialog = true">
          <Upload />Import JSON
        </Button><Button :disabled="!environmentId" @click="run(createDraft)">
          <Plus />New draft
        </Button>
      </div>
    </div>
    <Alert v-if="error" variant="destructive" class="mt-4">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert><Alert v-if="message" class="mt-4 border-emerald-500/40 text-emerald-700 dark:text-emerald-300">
      <CircleCheck /><AlertDescription>{{ message }}</AlertDescription>
    </Alert>
    <Card v-if="selected && document" class="mt-6">
      <Tabs v-model="tab">
        <TabsList>
          <TabsTrigger value="guided">
            Routes and clusters
          </TabsTrigger><TabsTrigger value="policies">
            Policies
          </TabsTrigger><TabsTrigger value="json">
            Advanced JSON
          </TabsTrigger><TabsTrigger value="validation">
            Validation
          </TabsTrigger>
        </TabsList>
        <TabsContent value="guided">
          <CardContent>
            <div class="flex items-center">
              <h2 class="text-lg font-semibold">
                Routes
              </h2><Button class="ml-auto" size="sm" @click="openRoute()">
                <Plus />Add route
              </Button>
            </div><Table>
              <TableHeader><TableRow><TableHead>ID</TableHead><TableHead>Methods and path</TableHead><TableHead>Cluster</TableHead><TableHead /></TableRow></TableHeader><TableBody>
                <TableRow v-for="(route, index) in document.routes" :key="route.id">
                  <TableCell><code>{{ route.id }}</code></TableCell><TableCell>{{ route.match.methods.join(', ') }} <code>{{ route.match.path }}</code></TableCell><TableCell>{{ route.clusterId }}</TableCell><TableCell>
                    <div class="flex justify-end gap-1">
                      <IconButton label="Edit route" @click="openRoute(index)">
                        <Pencil />
                      </IconButton><IconButton label="Delete route" class="text-destructive" @click="deleteRoute(index)">
                        <Trash2 />
                      </IconButton>
                    </div>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table><div class="flex items-center mt-8">
              <h2 class="text-lg font-semibold">
                Clusters
              </h2><Button class="ml-auto" size="sm" @click="openCluster()">
                <Plus />Add cluster
              </Button>
            </div><Table>
              <TableHeader><TableRow><TableHead>ID</TableHead><TableHead>Load balancing</TableHead><TableHead>Destinations</TableHead><TableHead /></TableRow></TableHeader><TableBody>
                <TableRow v-for="(cluster, index) in document.clusters" :key="cluster.id">
                  <TableCell><code>{{ cluster.id }}</code></TableCell><TableCell>{{ cluster.loadBalancingPolicy }}</TableCell><TableCell>{{ Object.keys(cluster.destinations).length }}</TableCell><TableCell>
                    <div class="flex justify-end gap-1">
                      <IconButton label="Edit cluster" @click="openCluster(index)">
                        <Pencil />
                      </IconButton><IconButton label="Delete cluster" class="text-destructive" @click="deleteCluster(index)">
                        <Trash2 />
                      </IconButton>
                    </div>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </CardContent>
        </TabsContent><TabsContent value="policies">
          <CardContent>
            <div class="flex">
              <h2 class="text-lg font-semibold">
                Policy catalog
              </h2><Button class="ml-auto" size="sm" @click="openPolicy">
                <Plus />Add policy
              </Button>
            </div><Table>
              <TableHeader><TableRow><TableHead>Kind</TableHead><TableHead>ID</TableHead><TableHead>Configuration</TableHead><TableHead /></TableRow></TableHeader><TableBody>
                <template v-for="(values, kind) in document.policies" :key="kind">
                  <TableRow v-for="(value, id) in values" :key="`${kind}-${id}`">
                    <TableCell>{{ kind }}</TableCell><TableCell><code>{{ id }}</code></TableCell><TableCell><code>{{ JSON.stringify(value) }}</code></TableCell><TableCell class="text-right">
                      <IconButton label="Delete policy" class="text-destructive" @click="deletePolicy(String(kind), String(id))">
                        <Trash2 />
                      </IconButton>
                    </TableCell>
                  </TableRow>
                </template>
              </TableBody>
            </Table>
          </CardContent>
        </TabsContent><TabsContent value="json">
          <CardContent>
            <Field>
              <FieldLabel for="configuration-json">
                Configuration JSON
              </FieldLabel><Textarea id="configuration-json" v-model="content" rows="28" class="font-mono text-sm" @blur="parseDocument" />
            </Field>
          </CardContent>
        </TabsContent><TabsContent value="validation">
          <CardContent>
            <Empty v-if="!issues.length">
              <EmptyHeader>
                <EmptyMedia variant="icon">
                  <CircleCheck />
                </EmptyMedia><EmptyTitle>No validation errors</EmptyTitle><EmptyDescription>Run validation after your latest changes.</EmptyDescription>
              </EmptyHeader>
            </Empty><ItemGroup v-else>
              <Item v-for="issue in issues" :key="issue.code + issue.jsonPath" as="button" type="button" @click="goToIssue(issue)">
                <ItemMedia variant="icon">
                  <CircleAlert :class="issue.severity === 'ERROR' ? 'text-destructive' : 'text-amber-500'" />
                </ItemMedia><ItemContent><ItemTitle>{{ issue.code }}</ItemTitle><ItemDescription>{{ issue.jsonPath }}: {{ issue.message }}</ItemDescription></ItemContent><ItemActions><ArrowRight /></ItemActions>
              </Item>
            </ItemGroup>
          </CardContent>
        </TabsContent>
      </Tabs><CardFooter>
        <Button variant="destructive" @click="run(deleteDraft)">
          <Trash2 />Delete draft
        </Button><Button variant="outline" @click="run(save)">
          Save
        </Button><Button variant="outline" @click="run(validate)">
          Validate
        </Button><Button class="ml-auto" @click="run(publishDraft)">
          Publish
        </Button>
      </CardFooter>
    </Card>
    <Empty v-else class="mt-8">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <FilePenLine />
        </EmptyMedia><EmptyTitle>No draft selected</EmptyTitle><EmptyDescription>Create a draft to begin editing.</EmptyDescription>
      </EmptyHeader>
    </Empty>
    <Dialog v-model:open="routeDialog">
      <DialogContent size="3xl">
        <DialogHeader><DialogTitle>{{ editingRouteIndex >= 0 ? 'Edit route' : 'Add route' }}</DialogTitle></DialogHeader><div class="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Field>
            <FieldLabel for="draft-route-id">
              Route ID
            </FieldLabel><Input id="draft-route-id" v-model="routeForm.id" />
          </Field><Field>
            <FieldLabel for="draft-route-cluster">
              Cluster
            </FieldLabel><Select v-model="routeForm.clusterId">
              <SelectTrigger id="draft-route-cluster">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="cluster in document?.clusters || []" :key="cluster.id" :value="cluster.id">
                  {{ cluster.id }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field><Field>
            <FieldLabel for="draft-route-path">
              Path pattern
            </FieldLabel><Input id="draft-route-path" v-model="routeForm.path" placeholder="/api/{**remainder}" />
          </Field><Field>
            <FieldLabel for="draft-route-methods">
              Methods
            </FieldLabel><Input id="draft-route-methods" v-model="routeForm.methods" /><FieldDescription>Comma separated</FieldDescription>
          </Field><Field>
            <FieldLabel for="draft-route-auth">
              Authorization policy
            </FieldLabel><Input id="draft-route-auth" v-model="routeForm.authorizationPolicy" />
          </Field><Field>
            <FieldLabel for="draft-route-rate">
              Rate-limit policy
            </FieldLabel><Input id="draft-route-rate" v-model="routeForm.rateLimitPolicy" />
          </Field><Field>
            <FieldLabel for="draft-route-timeout">
              Timeout policy
            </FieldLabel><Input id="draft-route-timeout" v-model="routeForm.timeoutPolicy" />
          </Field><Field>
            <FieldLabel for="draft-route-cors">
              CORS policy
            </FieldLabel><Input id="draft-route-cors" v-model="routeForm.corsPolicy" />
          </Field><Field class="md:col-span-2">
            <FieldLabel for="draft-route-transforms">
              YARP transforms
            </FieldLabel><Textarea id="draft-route-transforms" v-model="routeForm.transforms" rows="4" /><FieldDescription>JSON array of transform dictionaries</FieldDescription>
          </Field><Field>
            <FieldLabel for="draft-route-mirror">
              Mirror cluster
            </FieldLabel><Input id="draft-route-mirror" v-model="routeForm.mirrorCluster" />
          </Field><Field>
            <FieldLabel for="draft-route-mirror-percent">
              Mirror percent
            </FieldLabel><Input id="draft-route-mirror-percent" v-model.number="routeForm.mirrorPercentage" type="number" min="0" max="100" />
          </Field>
        </div><DialogFooter>
          <Button variant="outline" @click="routeDialog = false">
            Cancel
          </Button><Button :disabled="!routeForm.id || !routeForm.path || !routeForm.clusterId" @click="saveRoute">
            Save route
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <Dialog v-model:open="clusterDialog">
      <DialogContent size="4xl" scrollable>
        <DialogHeader><DialogTitle>{{ editingClusterIndex >= 0 ? 'Edit cluster' : 'Add cluster' }}</DialogTitle></DialogHeader><div data-slot="dialog-body" class="-mx-4 overflow-x-hidden px-4">
          <div class="grid grid-cols-1 gap-4 md:grid-cols-12">
            <Field class="md:col-span-6">
              <FieldLabel for="draft-cluster-id">
                Cluster ID
              </FieldLabel><Input id="draft-cluster-id" v-model="clusterForm.id" />
            </Field><Field class="md:col-span-6">
              <FieldLabel for="draft-cluster-balancing">
                Load balancing
              </FieldLabel><Select v-model="clusterForm.loadBalancingPolicy">
                <SelectTrigger id="draft-cluster-balancing">
                  <SelectValue />
                </SelectTrigger><SelectContent>
                  <SelectItem v-for="option in ['PowerOfTwoChoices', 'RoundRobin', 'Random', 'LeastRequests']" :key="option" :value="option">
                    {{ option }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field class="md:col-span-4">
              <FieldLabel for="draft-destination-id">
                Destination ID
              </FieldLabel><Input id="draft-destination-id" v-model="clusterForm.destinationId" />
            </Field><Field class="md:col-span-8">
              <FieldLabel for="draft-destination-address">
                Destination address
              </FieldLabel><Input id="draft-destination-address" v-model="clusterForm.address" />
            </Field><Field class="md:col-span-8">
              <FieldLabel for="draft-health-address">
                Health address
              </FieldLabel><Input id="draft-health-address" v-model="clusterForm.healthAddress" />
            </Field><Field class="md:col-span-4" orientation="horizontal">
              <Switch id="draft-active-health" v-model="clusterForm.activeHealth" /><FieldLabel for="draft-active-health">
                Active health
              </FieldLabel>
            </Field>
            <Field class="md:col-span-6">
              <FieldLabel for="draft-health-path">
                Health path
              </FieldLabel><Input id="draft-health-path" v-model="clusterForm.healthPath" />
            </Field><Field class="md:col-span-6" orientation="horizontal">
              <Switch id="draft-session-affinity" v-model="clusterForm.sessionAffinity" /><FieldLabel for="draft-session-affinity">
                Cookie affinity
              </FieldLabel>
            </Field>
            <Field class="md:col-span-12">
              <FieldLabel for="draft-resilience">
                Resilience policy
              </FieldLabel><Input id="draft-resilience" v-model="clusterForm.resiliencePolicy" />
            </Field><Field class="md:col-span-6">
              <FieldLabel for="draft-client-cert">
                Client certificate secret name
              </FieldLabel><Input id="draft-client-cert" v-model="clusterForm.clientCertificateRef" />
            </Field><Field class="md:col-span-6">
              <FieldLabel for="draft-trust-bundle">
                Trust bundle secret name
              </FieldLabel><Input id="draft-trust-bundle" v-model="clusterForm.trustBundleRef" />
            </Field>
            <Field class="md:col-span-4">
              <FieldLabel for="draft-http-version">
                Upstream HTTP version
              </FieldLabel><Select v-model="clusterForm.httpVersion">
                <SelectTrigger id="draft-http-version">
                  <SelectValue />
                </SelectTrigger><SelectContent>
                  <SelectItem v-for="option in ['1.1', '2.0', '3.0']" :key="option" :value="option">
                    {{ option }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field><Field class="md:col-span-8">
              <FieldLabel for="draft-version-policy">
                HTTP version policy
              </FieldLabel><Select v-model="clusterForm.versionPolicy">
                <SelectTrigger id="draft-version-policy">
                  <SelectValue />
                </SelectTrigger><SelectContent>
                  <SelectItem v-for="option in ['RequestVersionOrLower', 'RequestVersionOrHigher', 'RequestVersionExact']" :key="option" :value="option">
                    {{ option }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field class="md:col-span-4" orientation="horizontal">
              <Switch id="draft-decompression" v-model="clusterForm.automaticDecompression" /><FieldLabel for="draft-decompression">
                Decompress responses
              </FieldLabel>
            </Field><Field class="md:col-span-4" orientation="horizontal">
              <Switch id="draft-redirects" v-model="clusterForm.allowAutoRedirect" /><FieldLabel for="draft-redirects">
                Follow redirects
              </FieldLabel>
            </Field><Field class="md:col-span-4">
              <FieldLabel for="draft-max-connections">
                Maximum connections
              </FieldLabel><Input id="draft-max-connections" :model-value="clusterForm.maxConnections ?? ''" type="number" min="1" @update:model-value="clusterForm.maxConnections = $event === '' ? null : Number($event)" />
            </Field>
            <Field class="md:col-span-12">
              <FieldLabel for="draft-lifetime">
                Connection lifetime
              </FieldLabel><Input id="draft-lifetime" v-model="clusterForm.pooledConnectionLifetime" placeholder="00:05:00" />
            </Field><Field class="md:col-span-4">
              <FieldLabel for="draft-traffic-mode">
                Traffic mode
              </FieldLabel><Select v-model="clusterForm.trafficMode">
                <SelectTrigger id="draft-traffic-mode">
                  <SelectValue />
                </SelectTrigger><SelectContent>
                  <SelectItem v-for="option in ['random', 'stable']" :key="option" :value="option">
                    {{ option }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field><Field class="md:col-span-4">
              <FieldLabel for="draft-key-source">
                Stable key source
              </FieldLabel><Select v-model="clusterForm.trafficKeySource">
                <SelectTrigger id="draft-key-source">
                  <SelectValue />
                </SelectTrigger><SelectContent>
                  <SelectItem v-for="option in ['header', 'cookie', 'claim', 'consumerKey']" :key="option" :value="option">
                    {{ option }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field><Field class="md:col-span-4">
              <FieldLabel for="draft-key-name">
                Stable key name
              </FieldLabel><Input id="draft-key-name" v-model="clusterForm.trafficKey" />
            </Field><Field class="md:col-span-12">
              <FieldLabel for="draft-allocations">
                Pool allocations JSON
              </FieldLabel><Input id="draft-allocations" v-model="clusterForm.allocations" />
            </Field>
          </div>
        </div><DialogFooter>
          <Button variant="outline" @click="clusterDialog = false">
            Cancel
          </Button><Button :disabled="!clusterForm.id || !clusterForm.destinationId || !clusterForm.address" @click="saveCluster">
            Save cluster
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <Dialog v-model:open="policyDialog">
      <DialogContent size="xl">
        <DialogHeader><DialogTitle>Add policy</DialogTitle></DialogHeader><FieldGroup>
          <Field>
            <FieldLabel for="draft-policy-kind">
              Policy kind
            </FieldLabel><Select v-model="policyForm.kind">
              <SelectTrigger id="draft-policy-kind">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="option in ['authorization', 'rateLimits', 'timeouts', 'resilience', 'cors']" :key="option" :value="option">
                  {{ option }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field><Field>
            <FieldLabel for="draft-policy-id">
              Policy ID
            </FieldLabel><Input id="draft-policy-id" v-model="policyForm.id" />
          </Field><Field>
            <FieldLabel for="draft-policy-json">
              Policy configuration JSON
            </FieldLabel><Textarea id="draft-policy-json" v-model="policyForm.json" rows="10" class="font-mono text-sm" />
          </Field>
        </FieldGroup><DialogFooter>
          <Button variant="outline" @click="policyDialog = false">
            Cancel
          </Button><Button :disabled="!policyForm.id" @click="savePolicy">
            Save policy
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <Dialog v-model:open="importDialog">
      <DialogContent size="3xl">
        <DialogHeader><DialogTitle>Import configuration</DialogTitle></DialogHeader><Alert><Info /><AlertDescription>Import replaces this draft only. It does not publish.</AlertDescription></Alert><Field>
          <FieldLabel for="draft-import-json">
            Configuration JSON
          </FieldLabel><Textarea id="draft-import-json" v-model="importJson" rows="20" class="font-mono text-sm" />
        </Field><DialogFooter>
          <Button variant="outline" @click="importDialog = false">
            Cancel
          </Button><Button :disabled="!importJson" @click="run(importDraft)">
            Replace draft
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <AlertDialog :open="conflictDialog">
      <AlertDialogContent>
        <AlertDialogHeader><AlertDialogTitle>Draft changed on the server</AlertDialogTitle><AlertDialogDescription>Another editor saved this draft. Load their version, or explicitly replace it with your current JSON.</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter>
          <AlertDialogCancel @click="run(() => resolveConflict(false))">
            Load server version
          </AlertDialogCancel><AlertDialogAction variant="destructive" @click="run(() => resolveConflict(true))">
            Replace with my version
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, Badge, Button, Card, CardContent, CardFooter, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle, Field, FieldDescription, FieldGroup, FieldLabel, Input, Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Switch, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Tabs, TabsContent, TabsList, TabsTrigger, Textarea } from '@aditify/ui';
import { ArrowRight, CircleAlert, CircleCheck, FilePenLine, Info, Pencil, Plus, Trash2, Upload } from '@lucide/vue';
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { onBeforeRouteLeave } from 'vue-router';
import { graphql } from '../api';
import IconButton from '../components/IconButton.vue';
import { confirmAction } from '../composables/confirmDialog';

interface Environment { id: string; displayName: string }interface Revision { id: string; number: number; state: string; configJson: string; concurrencyVersion: string } interface Issue { severity: string; code: string; jsonPath: string; message: string }

const environments = ref<Environment[]>([]); const environmentId = ref(''); const revisions = ref<Revision[]>([]); const selected = ref<Revision | null>(null); const selectedId = ref(''); const content = ref(''); const document = ref<any>(null); const issues = ref<Issue[]>([]); const message = ref(''); const error = ref(''); const tab = ref('guided'); const dirty = ref(false); const routeDialog = ref(false); const clusterDialog = ref(false); const policyDialog = ref(false); const importDialog = ref(false); const conflictDialog = ref(false); const importJson = ref(''); const editingRouteIndex = ref(-1); const editingClusterIndex = ref(-1);
const routeForm = ref({ id: '', path: '', clusterId: '', methods: 'GET', authorizationPolicy: '', rateLimitPolicy: '', timeoutPolicy: '', corsPolicy: '', transforms: '[]', mirrorCluster: '', mirrorPercentage: 0 }); const clusterForm = ref({ id: '', destinationId: 'primary', address: 'https://', healthAddress: '', loadBalancingPolicy: 'PowerOfTwoChoices', activeHealth: false, healthPath: '/healthz', sessionAffinity: false, resiliencePolicy: '', clientCertificateRef: '', trustBundleRef: '', trafficMode: '', trafficKeySource: '', trafficKey: '', allocations: '{"default":100}', httpVersion: '2.0', versionPolicy: 'RequestVersionOrLower', automaticDecompression: false, allowAutoRedirect: false, maxConnections: null as number | null, pooledConnectionLifetime: '' }); const policyForm = ref({ kind: 'authorization', id: '', json: '{"type":"apiKey"}' });

async function loadEnvironments() { environments.value = (await graphql<{ environments: Environment[] }>(`query{environments{id displayName}}`)).environments; environmentId.value = environments.value[0]?.id || ''; }
async function loadDrafts() {
  if (!environmentId.value)
    return;

  revisions.value = (await graphql<{ revisions: Revision[] }>(`query($id:UUID!){revisions(environmentId:$id,state:DRAFT){id number state configJson concurrencyVersion}}`, { id: environmentId.value })).revisions; selectRevision(revisions.value[0] || null);
}
function selectRevision(revision: Revision | null) { selected.value = revision; selectedId.value = revision?.id || ''; content.value = revision?.configJson || ''; parseDocument(); issues.value = []; dirty.value = false; }
function selectById() { selectRevision(revisions.value.find(x => x.id === selectedId.value) || null); }
function parseDocument() {
  try { document.value = content.value ? JSON.parse(content.value) : null; error.value = ''; }
  catch (e) { document.value = null; error.value = `Invalid JSON: ${e instanceof Error ? e.message : String(e)}`; }
}
function syncDocument() { content.value = JSON.stringify(document.value, null, 2); dirty.value = true; }
async function createDraft() {
  const data = await graphql<{ createDraft: { revision: Revision } }>(`mutation($id:UUID!){createDraft(environmentId:$id){revision{id number state configJson concurrencyVersion}}}`, { id: environmentId.value });

  await loadDrafts(); selectRevision(revisions.value.find(x => x.id === data.createDraft.revision.id) || data.createDraft.revision);
}
async function save() {
  if (!selected.value)
    return;

  if (tab.value === 'json')
    parseDocument();

  if (!document.value)
    throw new Error('Fix the JSON before saving.');

  if (tab.value !== 'json')
    syncDocument();

  try {
    const data = await graphql<{ setDraftContent: { revision: Revision } }>(`mutation($id:UUID!,$version:UUID!,$json:String!){setDraftContent(draftId:$id,expectedVersion:$version,json:$json){revision{id number state configJson concurrencyVersion}}}`, { id: selected.value.id, version: selected.value.concurrencyVersion, json: content.value });

    selected.value = data.setDraftContent.revision; content.value = selected.value.configJson; parseDocument(); dirty.value = false; message.value = 'Draft saved.';
  }
  catch (e) {
    if (e instanceof Error && e.message.includes('changed after it was loaded')) { conflictDialog.value = true; return; }

    throw e;
  }
}
async function importDraft() {
  if (!selected.value)
    return;

  const data = await graphql<{ importDraft: { revision: Revision } }>(`mutation($id:UUID!,$version:UUID!,$json:String!){importDraft(draftId:$id,expectedVersion:$version,json:$json){revision{id number state configJson concurrencyVersion}}}`, { id: selected.value.id, version: selected.value.concurrencyVersion, json: importJson.value });

  selectRevision(data.importDraft.revision); importDialog.value = false; message.value = 'Configuration imported into the draft.';
}
async function resolveConflict(overwrite: boolean) {
  if (!selected.value)
    return;

  const mine = content.value; const current = (await graphql<{ revision: Revision }>(`query($id:UUID!){revision(id:$id){id number state configJson concurrencyVersion}}`, { id: selected.value.id })).revision;

  if (!overwrite) { selectRevision(current); message.value = 'The current server version was loaded.'; }
  else {
    const data = await graphql<{ setDraftContent: { revision: Revision } }>(`mutation($id:UUID!,$version:UUID!,$json:String!){setDraftContent(draftId:$id,expectedVersion:$version,json:$json){revision{id number state configJson concurrencyVersion}}}`, { id: current.id, version: current.concurrencyVersion, json: mine });

    selectRevision(data.setDraftContent.revision); message.value = 'Your changes replaced the newer server draft.';
  }

  conflictDialog.value = false;
}
function goToIssue(issue: Issue) { tab.value = 'json'; message.value = `Review ${issue.jsonPath} in the JSON editor.`; }
async function validate() {
  if (!selected.value)
    return;

  await save(); issues.value = (await graphql<{ validateDraft: { issues: Issue[] } }>(`mutation($id:UUID!){validateDraft(draftId:$id){issues{severity code jsonPath message}}}`, { id: selected.value.id })).validateDraft.issues;

  if (!issues.value.length)
    message.value = 'Validation passed.';
}
async function publishDraft() {
  if (!selected.value || !await confirmAction(`Publish revision ${selected.value.number} and make it active?`, { title: 'Publish revision?', confirmText: 'Publish' }))
    return;

  await validate();

  if (issues.value.some(x => x.severity === 'ERROR'))
    return;

  await graphql(`mutation($id:UUID!,$version:UUID!){publishDraft(draftId:$id,expectedVersion:$version){id}}`, { id: selected.value.id, version: selected.value.concurrencyVersion }); message.value = 'Revision published.'; await loadDrafts();
}
async function deleteDraft() {
  if (!selected.value || !await confirmAction('This cannot be undone.', { title: `Delete draft revision ${selected.value.number}?`, confirmText: 'Delete draft', color: 'error' }))
    return;

  await graphql(`mutation($id:UUID!,$version:UUID!){deleteDraft(draftId:$id,expectedVersion:$version)}`, { id: selected.value.id, version: selected.value.concurrencyVersion }); message.value = 'Draft deleted.'; await loadDrafts();
}
function openRoute(index: number | string = -1) {
  const position = Number(index);

  editingRouteIndex.value = position;

  const route = position >= 0 ? document.value.routes[position] : null;

  routeForm.value = route ? { id: route.id, path: route.match.path, clusterId: route.clusterId, methods: (route.match.methods || []).join(', '), authorizationPolicy: route.authorizationPolicy || '', rateLimitPolicy: route.rateLimitPolicy || '', timeoutPolicy: route.timeoutPolicy || '', corsPolicy: route.corsPolicy || '', transforms: JSON.stringify(route.transforms || [], null, 2), mirrorCluster: route.mirror?.clusterId || '', mirrorPercentage: route.mirror?.percentage || 0 } : { id: '', path: '', clusterId: document.value.clusters[0]?.id || '', methods: 'GET', authorizationPolicy: '', rateLimitPolicy: '', timeoutPolicy: '', corsPolicy: '', transforms: '[]', mirrorCluster: '', mirrorPercentage: 0 }; routeDialog.value = true;
}
function saveRoute() {
  const existing = editingRouteIndex.value >= 0 ? document.value.routes[editingRouteIndex.value] : {}; const f = routeForm.value; const route = { ...existing, id: f.id, enabled: existing.enabled ?? true, match: { ...(existing.match || {}), path: f.path, hosts: existing.match?.hosts || [], methods: f.methods.split(/[\s,]+/).filter(Boolean).map(x => x.toUpperCase()), headers: existing.match?.headers || [], queryParameters: existing.match?.queryParameters || [] }, clusterId: f.clusterId, transforms: JSON.parse(f.transforms), authorizationPolicy: f.authorizationPolicy || null, rateLimitPolicy: f.rateLimitPolicy || null, timeoutPolicy: f.timeoutPolicy || null, corsPolicy: f.corsPolicy || null, mirror: f.mirrorCluster ? { clusterId: f.mirrorCluster, percentage: f.mirrorPercentage, allowedMethods: null, maximumBufferedBodyBytes: 0, timeout: null, removeHeaders: null } : null, metadata: existing.metadata || {} };

  if (editingRouteIndex.value >= 0)
    document.value.routes[editingRouteIndex.value] = route; else document.value.routes.push(route);

  syncDocument(); routeDialog.value = false;
}
async function deleteRoute(index: number | string) {
  const position = Number(index);

  if (!await confirmAction(`Delete route ${document.value.routes[position].id}?`, { title: 'Delete route?', confirmText: 'Delete', color: 'error' }))
    return;

  document.value.routes.splice(position, 1); syncDocument();
}
function openCluster(index: number | string = -1) {
  const position = Number(index);

  editingClusterIndex.value = position;

  const cluster = position >= 0 ? document.value.clusters[position] : null; const destination = cluster ? Object.entries(cluster.destinations)[0] as [string, any] : null; const http = cluster?.httpClient || {};

  clusterForm.value = cluster ? { id: cluster.id, destinationId: destination?.[0] || 'primary', address: destination?.[1].address || '', healthAddress: destination?.[1].healthAddress || '', loadBalancingPolicy: cluster.loadBalancingPolicy, activeHealth: cluster.health?.activeEnabled || false, healthPath: cluster.health?.path || '/healthz', sessionAffinity: cluster.sessionAffinity?.enabled || false, resiliencePolicy: cluster.resiliencePolicy || '', clientCertificateRef: cluster.tls?.clientCertificateRef || '', trustBundleRef: cluster.tls?.trustBundleRef || '', trafficMode: cluster.traffic?.mode || '', trafficKeySource: cluster.traffic?.keySource || '', trafficKey: cluster.traffic?.key || '', allocations: JSON.stringify(cluster.traffic?.allocations || { default: 100 }), httpVersion: http.version || '2.0', versionPolicy: http.versionPolicy || 'RequestVersionOrLower', automaticDecompression: http.automaticDecompression || false, allowAutoRedirect: http.allowAutoRedirect || false, maxConnections: http.maxConnectionsPerServer || null, pooledConnectionLifetime: http.pooledConnectionLifetime || '' } : { id: '', destinationId: 'primary', address: 'https://', healthAddress: '', loadBalancingPolicy: 'PowerOfTwoChoices', activeHealth: false, healthPath: '/healthz', sessionAffinity: false, resiliencePolicy: '', clientCertificateRef: '', trustBundleRef: '', trafficMode: '', trafficKeySource: '', trafficKey: '', allocations: '{"default":100}', httpVersion: '2.0', versionPolicy: 'RequestVersionOrLower', automaticDecompression: false, allowAutoRedirect: false, maxConnections: null, pooledConnectionLifetime: '' }; clusterDialog.value = true;
}
function saveCluster() {
  const existing = editingClusterIndex.value >= 0 ? document.value.clusters[editingClusterIndex.value] : {}; const f = clusterForm.value; const cluster = { ...existing, id: f.id, loadBalancingPolicy: f.loadBalancingPolicy, destinations: { [f.destinationId]: { address: f.address, healthAddress: f.healthAddress || null, pool: 'default', metadata: null } }, health: { activeEnabled: f.activeHealth, path: f.healthPath, interval: null, timeout: null, passiveEnabled: existing.health?.passiveEnabled || false, reactivationPeriod: existing.health?.reactivationPeriod || null, activePolicy: existing.health?.activePolicy || 'ConsecutiveFailures', passivePolicy: existing.health?.passivePolicy || 'TransportFailureRate', availableDestinationsPolicy: existing.health?.availableDestinationsPolicy || 'HealthyOrPanic', query: existing.health?.query || null }, sessionAffinity: f.sessionAffinity ? { enabled: true, policy: 'Cookie', failurePolicy: 'Redistribute', cookieName: 'ApiGateway.Affinity', path: null, domain: null, securePolicy: 'SameAsRequest', sameSite: 'Lax', expiration: null } : null, resiliencePolicy: f.resiliencePolicy || null, traffic: f.trafficMode ? { allocations: JSON.parse(f.allocations), mode: f.trafficMode, key: f.trafficKey || null, fallbackToHealthyPool: false, keySource: f.trafficKeySource || null } : null, tls: f.clientCertificateRef || f.trustBundleRef ? { clientCertificateRef: f.clientCertificateRef || null, trustBundleRef: f.trustBundleRef || null } : null, httpClient: { version: f.httpVersion, versionPolicy: f.versionPolicy, automaticDecompression: f.automaticDecompression, allowAutoRedirect: f.allowAutoRedirect, pooledConnectionLifetime: f.pooledConnectionLifetime || null, maxConnectionsPerServer: f.maxConnections || null, enableMultipleHttp2Connections: existing.httpClient?.enableMultipleHttp2Connections || false }, metadata: existing.metadata || {} };

  if (editingClusterIndex.value >= 0)
    document.value.clusters[editingClusterIndex.value] = cluster; else document.value.clusters.push(cluster);

  syncDocument(); clusterDialog.value = false;
}
function openPolicy() { policyForm.value = { kind: 'authorization', id: '', json: '{"type":"apiKey"}' }; policyDialog.value = true; }
function savePolicy() {
  const f = policyForm.value;

  document.value.policies[f.kind] ||= {}; document.value.policies[f.kind][f.id] = JSON.parse(f.json); syncDocument(); policyDialog.value = false;
}
async function deletePolicy(kind: string, id: string) {
  if (!await confirmAction(`Delete policy ${id}?`, { title: 'Delete policy?', confirmText: 'Delete', color: 'error' }))
    return;

  delete document.value.policies[kind][id]; syncDocument();
}
async function deleteCluster(index: number | string) {
  const position = Number(index);

  if (!await confirmAction('Routes referencing this cluster will fail validation.', { title: `Delete cluster ${document.value.clusters[position].id}?`, confirmText: 'Delete cluster', color: 'error' }))
    return;

  document.value.clusters.splice(position, 1); syncDocument();
}
async function run(action: () => Promise<void>) {
  error.value = ''; message.value = '';

  try { await action(); }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}
function beforeUnload(event: BeforeUnloadEvent) { if (dirty.value) { event.preventDefault(); event.returnValue = ''; } }
watch(content, (value) => {
  if (selected.value && value !== selected.value.configJson)
    dirty.value = true;
}); watch(environmentId, () => run(loadDrafts)); onMounted(() => { window.addEventListener('beforeunload', beforeUnload); run(loadEnvironments); }); onBeforeUnmount(() => window.removeEventListener('beforeunload', beforeUnload)); onBeforeRouteLeave(async () => !dirty.value || await confirmAction('Your unsaved draft changes will be lost.', { title: 'Discard unsaved changes?', confirmText: 'Discard', color: 'error' }));
</script>

<style scoped>
.json-editor :deep(textarea) {
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 0.85rem;
}
</style>
