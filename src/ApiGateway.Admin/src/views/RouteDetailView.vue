<template>
  <div class="page-container">
    <EnvironmentRequiredAlert />
    <template v-if="routeModel">
      <header class="mt-6 flex flex-wrap items-end justify-between gap-4">
        <div>
          <p class="eyebrow">
            {{ t('route') }}
          </p><h1>{{ routeModel.name }}</h1>
        </div>
        <div class="flex flex-wrap gap-2">
          <Button variant="outline" @click="operationsDialog = true">
            <Settings2 />{{ t('routeTrafficState') }}
          </Button><Button variant="outline" :disabled="togglingEnabled" @click="toggleRouteEnabled">
            <Power />{{ routeModel.enabled ? t('disabled') : t('enabled') }}
          </Button><Button variant="destructive" @click="deleteRoute">
            <Trash2 />{{ t('delete') }}
          </Button>
        </div>
      </header>
      <Alert v-if="error" variant="destructive" class="mt-4">
        <AlertCircle /><AlertDescription>{{ error }}</AlertDescription>
      </Alert>
      <Alert v-if="message" class="mt-4 border-emerald-500/40 text-emerald-700 dark:text-emerald-300">
        <CircleCheck /><AlertDescription>{{ message }}</AlertDescription>
      </Alert>

      <Card class="mt-6">
        <CardHeader><CardTitle>{{ t('routeSummary') }}</CardTitle></CardHeader><CardContent class="grid gap-4 md:grid-cols-[1fr_auto_1fr] md:items-center">
          <div>
            <p class="text-xs font-bold uppercase text-muted-foreground">
              {{ t('incomingRequest') }}
            </p><code>{{ basics.path }}</code>
          </div><ArrowRight class="hidden text-muted-foreground md:block" /><div>
            <p class="text-xs font-bold uppercase text-muted-foreground">
              {{ t('upstreamUrl') }}
            </p><a class="text-primary underline-offset-4 hover:underline" :href="routeTestUrls(routeModel)[0]" target="_blank"><code>{{ basics.upstreamUrl }}</code></a>
          </div>
        </CardContent>
      </Card>

      <Card class="mt-6">
        <CardHeader><CardTitle>{{ t('routing') }}</CardTitle></CardHeader><CardContent class="space-y-5">
          <div class="grid gap-4 md:grid-cols-2">
            <Field><FieldLabel>{{ t('name') }}</FieldLabel><Input v-model="basics.name" /></Field><Field><FieldLabel>{{ t('incomingPath') }}</FieldLabel><Input v-model="basics.path" /></Field>
          </div>
          <div class="flex items-center gap-2">
            <Switch v-model="basics.matchSubpaths" /><label class="text-sm">{{ t('matchSubpaths') }}</label>
          </div>
          <div class="grid gap-4 md:grid-cols-2">
            <Field>
              <FieldLabel>{{ t('upstream') }}</FieldLabel><Select v-model="selectedUpstreamId">
                <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                  <SelectItem value="__manual">
                    {{ t('manualUpstream') }}
                  </SelectItem><SelectItem v-for="upstream in upstreams" :key="upstream.id" :value="upstream.id">
                    {{ upstream.name }}
                  </SelectItem>
                </SelectContent>
              </Select><FieldDescription>{{ t('upstreamChoiceHelp') }}</FieldDescription>
            </Field><Field v-if="selectedUpstreamId === '__manual'">
              <FieldLabel>{{ t('upstreamUrl') }}</FieldLabel><Input v-model="basics.upstreamUrl" />
            </Field><Field>
              <FieldLabel>{{ t('incomingHosts') }}</FieldLabel><TagsInput v-model="basics.hosts" add-on-paste add-on-tab :delimiter="hostDelimiter">
                <TagsInputItem v-for="host in basics.hosts" :key="host" :value="host">
                  <TagsInputItemText /><TagsInputItemDelete />
                </TagsInputItem><TagsInputInput />
              </TagsInput><FieldDescription>{{ t('incomingHostsHint') }}</FieldDescription>
            </Field>
          </div>
          <div class="grid gap-4 md:grid-cols-2">
            <Field>
              <FieldLabel>{{ t('inboundScheme') }}</FieldLabel><Select v-model="basics.inboundScheme">
                <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                  <SelectItem v-for="option in inboundSchemes" :key="option.value" :value="option.value">
                    {{ option.title }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field><Field>
              <FieldLabel>{{ t('inboundCertificate') }}</FieldLabel><Select v-model="basics.certificateId">
                <SelectTrigger><SelectValue :placeholder="t('notConfigured')" /></SelectTrigger><SelectContent>
                  <SelectItem :value="null">
                    {{ t('notConfigured') }}
                  </SelectItem>
                  <SelectItem v-for="certificate in certificates" :key="certificate.id" :value="certificate.id">
                    {{ certificate.name }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
          </div>
          <div class="flex flex-wrap gap-6">
            <div class="flex items-center gap-2">
              <Switch v-model="basics.webSocketsAllowed" /><label class="text-sm">{{ t('webSocketsAllowed') }}</label>
            </div><div class="flex items-center gap-2">
              <Switch v-model="basics.preserveOriginalHost" /><label class="text-sm">{{ t('preserveOriginalHost') }}</label>
            </div>
          </div>

          <Accordion type="multiple" class="w-full">
            <AccordionItem value="matching">
              <AccordionTrigger>{{ t('advancedMatching') }}</AccordionTrigger><AccordionContent class="space-y-5">
                <div class="grid gap-4 md:grid-cols-2">
                  <Field><FieldLabel>{{ t('allowedMethods') }}</FieldLabel><Input v-model="basics.methods" /><FieldDescription>{{ t('allowedMethodsHint') }}</FieldDescription></Field><Field><FieldLabel>{{ t('precedence') }}</FieldLabel><Input :model-value="basics.order ?? ''" type="number" @update:model-value="basics.order = $event === '' ? null : Number($event)" /></Field>
                </div>
                <section>
                  <div class="flex items-center justify-between">
                    <div>
                      <h3 class="font-semibold">
                        {{ t('headerConditions') }}
                      </h3><p class="text-sm text-muted-foreground">
                        {{ t('headerConditionsHelp') }}
                      </p>
                    </div><Button variant="outline" size="sm" @click="addMatchRule(basics.headers)">
                      <Plus />{{ t('addHeader') }}
                    </Button>
                  </div>
                  <Card v-for="(rule, index) in basics.headers" :key="`h${index}`" class="mt-3">
                    <CardContent class="grid gap-3 pt-6 md:grid-cols-4">
                      <Input v-model="rule.name" :placeholder="t('headerName')" /><Select v-model="rule.mode">
                        <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                          <SelectItem v-for="mode in headerMatchModes" :key="mode" :value="mode">
                            {{ mode }}
                          </SelectItem>
                        </SelectContent>
                      </Select><Input v-model="rule.pattern" :placeholder="t('value')" /><IconButton class="text-destructive" :label="t('removeHeaderCondition', { number: index + 1 })" @click="basics.headers.splice(index, 1)">
                        <Trash2 />
                      </IconButton>
                    </CardContent>
                  </Card>
                </section>
                <section>
                  <div class="flex items-center justify-between">
                    <div>
                      <h3 class="font-semibold">
                        {{ t('queryConditions') }}
                      </h3><p class="text-sm text-muted-foreground">
                        {{ t('queryConditionsHelp') }}
                      </p>
                    </div><Button variant="outline" size="sm" @click="addMatchRule(basics.queryParameters)">
                      <Plus />{{ t('addQueryParameter') }}
                    </Button>
                  </div>
                  <Card v-for="(rule, index) in basics.queryParameters" :key="`q${index}`" class="mt-3">
                    <CardContent class="grid gap-3 pt-6 md:grid-cols-4">
                      <Input v-model="rule.name" :placeholder="t('parameterName')" /><Select v-model="rule.mode">
                        <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                          <SelectItem v-for="mode in queryMatchModes" :key="mode" :value="mode">
                            {{ mode }}
                          </SelectItem>
                        </SelectContent>
                      </Select><Input v-model="rule.pattern" :placeholder="t('value')" /><IconButton class="text-destructive" :label="t('removeQueryCondition', { number: index + 1 })" @click="basics.queryParameters.splice(index, 1)">
                        <Trash2 />
                      </IconButton>
                    </CardContent>
                  </Card>
                </section>
              </AccordionContent>
            </AccordionItem>
            <AccordionItem value="upstream">
              <AccordionTrigger>{{ t('advancedUpstream') }}</AccordionTrigger><AccordionContent class="space-y-5">
                <Field>
                  <FieldLabel>{{ t('pathHandling') }}</FieldLabel><Select v-model="basics.pathHandling" @update:model-value="setPathHandling(String($event))">
                    <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                      <SelectItem v-for="option in pathHandlingOptions" :key="option.value" :value="option.value">
                        {{ option.title }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field v-if="basics.pathHandling === 'STRIP_PREFIX'">
                  <FieldLabel>{{ t('pathPrefix') }}</FieldLabel><Input v-model="basics.pathPrefixToRemove" />
                </Field>
                <Alert v-if="selectedUpstreamId !== '__manual'">
                  <Info /><AlertDescription>{{ t('namedUpstreamSettings') }}</AlertDescription>
                </Alert><template v-else>
                  <div class="grid gap-4 md:grid-cols-3">
                    <Field>
                      <FieldLabel>{{ t('loadBalancing') }}</FieldLabel><Select v-model="basics.loadBalancingPolicy">
                        <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                          <SelectItem v-for="policy in loadBalancingPolicies" :key="policy" :value="policy">
                            {{ policy }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                    </Field><Field>
                      <FieldLabel>{{ t('upstreamHttpVersion') }}</FieldLabel><Select v-model="basics.upstreamHttpVersion">
                        <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                          <SelectItem value="1.1">
                            1.1
                          </SelectItem><SelectItem value="2.0">
                            2.0
                          </SelectItem>
                        </SelectContent>
                      </Select>
                    </Field><Field>
                      <FieldLabel>{{ t('upstreamVersionPolicy') }}</FieldLabel><Select v-model="basics.upstreamVersionPolicy">
                        <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                          <SelectItem v-for="policy in upstreamVersionPolicies" :key="policy" :value="policy">
                            {{ policy }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                    </Field>
                  </div>
                  <div class="flex items-center gap-2">
                    <Switch v-model="basics.enableMultipleHttp2Connections" /><label class="text-sm">{{ t('multipleHttp2Connections') }}</label>
                  </div>
                  <div class="flex items-center justify-between">
                    <div>
                      <h3 class="font-semibold">
                        {{ t('additionalDestinations') }}
                      </h3><p class="text-sm text-muted-foreground">
                        {{ t('additionalDestinationsHelp') }}
                      </p>
                    </div><Button variant="outline" size="sm" @click="addDestination">
                      <Plus />{{ t('addDestination') }}
                    </Button>
                  </div>
                  <Table v-if="basics.additionalDestinations.length" class="table-fixed">
                    <TableHeader>
                      <TableRow>
                        <TableHead class="w-1/5">
                          {{ t('destinationName') }}
                        </TableHead><TableHead class="w-2/5">
                          {{ t('destinationUrl') }}
                        </TableHead><TableHead>
                          {{ t('healthUrl') }}
                        </TableHead><TableHead class="w-12" />
                      </TableRow>
                    </TableHeader><TableBody>
                      <TableRow v-for="(destination, index) in basics.additionalDestinations" :key="destination.clientKey">
                        <TableCell><Input v-model="destination.id" :aria-label="t('destinationName')" :placeholder="t('destinationName')" /></TableCell><TableCell><Input v-model="destination.address" :aria-label="t('destinationUrl')" :placeholder="t('destinationUrl')" /></TableCell><TableCell><Input v-model="destination.healthAddress" :aria-label="t('healthUrl')" :placeholder="t('healthUrl')" /></TableCell><TableCell class="text-right">
                          <IconButton class="text-destructive" :label="t('removeDestination', { number: index + 1 })" @click="basics.additionalDestinations.splice(index, 1)">
                            <Trash2 />
                          </IconButton>
                        </TableCell>
                      </TableRow>
                    </TableBody>
                  </Table>
                </template>
              </AccordionContent>
            </AccordionItem>
          </Accordion>
        </CardContent><CardFooter class="justify-end">
          <Button :disabled="saving" @click="saveBasics">
            <Spinner v-if="saving" />{{ t('saveActivate') }}
          </Button>
        </CardFooter>
      </Card>

      <section class="mt-8">
        <div class="flex items-end justify-between gap-4">
          <div>
            <h2 class="text-xl font-semibold">
              {{ t('gatewayFeatures') }}
            </h2><p class="text-muted-foreground">
              {{ t('gatewayFeaturesHelp') }}
            </p>
          </div><Button @click="catalogDialog = true">
            <Plus />{{ t('addFeature') }}
          </Button>
        </div>
        <Card class="mt-4 overflow-hidden">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{{ t('feature') }}</TableHead><TableHead>{{ t('configuration') }}</TableHead><TableHead>{{ t('status') }}</TableHead><TableHead class="text-right">
                  {{ t('actions') }}
                </TableHead>
              </TableRow>
            </TableHeader><TableBody>
              <TableRow v-for="item in configuredFeatures" :key="item.id">
                <TableCell class="font-semibold">
                  {{ item.name }}
                </TableCell><TableCell>{{ item.summary }}</TableCell><TableCell>
                  <div class="flex items-center gap-2">
                    <Switch :model-value="item.enabled" @update:model-value="toggleFeature(item.id, !!$event)" /><span>{{ item.enabled ? t('enabled') : t('disabled') }}</span>
                  </div>
                </TableCell><TableCell>
                  <div class="flex justify-end gap-1">
                    <IconButton variant="outline" :label="t('configure')" @click="openFeature(item.id)">
                      <Pencil />
                    </IconButton><IconButton class="text-destructive" :label="t('removeFeatureNamed', { name: item.name })" @click="removeFeature(item.id)">
                      <Trash2 />
                    </IconButton>
                  </div>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!configuredFeatures.length" :colspan="4">
                {{ t('noFeaturesText') }}
              </TableEmpty>
            </TableBody>
          </Table>
        </Card>
      </section>

      <Dialog v-model:open="operationsDialog">
        <DialogContent>
          <DialogHeader><DialogTitle>{{ t('routeTrafficState') }}</DialogTitle><DialogDescription>{{ t('trafficStateHelp') }}</DialogDescription></DialogHeader><Field>
            <FieldLabel>{{ t('trafficState') }}</FieldLabel><Select v-model="operationsForm.state">
              <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                <SelectItem v-for="option in operationalStateOptions" :key="option.value" :value="option.value">
                  {{ option.title }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field><Field>
            <FieldLabel>{{ t('unavailableResponse') }}</FieldLabel><Select v-model="operationsForm.responseProfileId">
              <SelectTrigger><SelectValue :placeholder="t('useEnvironmentDefault')" /></SelectTrigger><SelectContent>
                <SelectItem :value="null">
                  {{ t('useEnvironmentDefault') }}
                </SelectItem>
                <SelectItem v-for="option in responseProfiles" :key="option.id" :value="option.id">
                  {{ option.name }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field><DialogFooter>
            <Button variant="outline" @click="operationsDialog = false">
              {{ t('cancel') }}
            </Button><Button :disabled="saving" @click="saveOperationalState">
              <Spinner v-if="saving" />{{ t('saveActivate') }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog v-model:open="catalogDialog">
        <DialogContent size="4xl" scrollable>
          <DialogHeader><DialogTitle>{{ t('addGatewayFeature') }}</DialogTitle></DialogHeader><div data-slot="dialog-body" class="-mx-4 overflow-y-auto px-4">
            <div class="grid gap-3 md:grid-cols-2">
              <Card v-for="feature in availableFeatures" :key="feature.id" class="cursor-pointer transition-colors hover:bg-accent" @click="openFeature(feature.id)">
                <CardHeader><CardTitle>{{ featureName(feature.id, feature.displayName) }}</CardTitle><CardDescription>{{ featureCategory(feature.category) }}</CardDescription></CardHeader><CardContent>{{ featureDescription(feature.id, feature.description) }}</CardContent>
              </Card>
            </div>
          </div><DialogFooter>
            <Button variant="outline" @click="catalogDialog = false">
              {{ t('close') }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog v-model:open="featureDialog">
        <DialogContent size="2xl" scrollable>
          <DialogHeader><DialogTitle>{{ featureTitle }}</DialogTitle><DialogDescription>{{ featureHelp }}</DialogDescription></DialogHeader><div data-slot="dialog-body" class="-mx-4 grid gap-4 overflow-x-hidden px-4">
            <Alert v-if="dialogError" variant="destructive">
              <AlertCircle /><AlertDescription>{{ dialogError }}</AlertDescription>
            </Alert>
            <template v-if="selectedFeature === 'authorization'">
              <Field>
                <FieldLabel>{{ t('authenticationType') }}</FieldLabel><Select v-model="featureForm.authType">
                  <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                    <SelectItem v-for="option in authenticationTypes" :key="option.value" :value="option.value">
                      {{ option.title }}
                    </SelectItem>
                  </SelectContent>
                </Select>
              </Field><template v-if="featureForm.authType === 'jwt'">
                <Field><FieldLabel>{{ t('authorityUrl') }}</FieldLabel><Input v-model="featureForm.authority" /></Field><Field><FieldLabel>{{ t('expectedIssuer') }}</FieldLabel><Input v-model="featureForm.issuer" /></Field><Field><FieldLabel>{{ t('audiences') }}</FieldLabel><Input v-model="featureForm.audiences" /></Field>
              </template>
            </template>
            <template v-else-if="selectedFeature === 'rate-limit'">
              <Field><FieldLabel>{{ t('algorithm') }}</FieldLabel><Input v-model="featureForm.rateType" /></Field><Field><FieldLabel>{{ t('requestLimit') }}</FieldLabel><Input v-model.number="featureForm.permitLimit" type="number" /></Field><Field><FieldLabel>{{ t('window') }}</FieldLabel><Input v-model="featureForm.window" /></Field><Field><FieldLabel>{{ t('limitBy') }}</FieldLabel><Input v-model="featureForm.partitionBy" /></Field>
            </template>
            <template v-else-if="selectedFeature === 'headers'">
              <Field>
                <FieldLabel>{{ t('direction') }}</FieldLabel><Select v-model="featureForm.headerDirection">
                  <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                    <SelectItem v-for="option in headerDirections" :key="option.value" :value="option.value">
                      {{ option.title }}
                    </SelectItem>
                  </SelectContent>
                </Select>
              </Field><Field><FieldLabel>{{ t('headerName') }}</FieldLabel><Input v-model="featureForm.headerName" /></Field><Field><FieldLabel>{{ t('value') }}</FieldLabel><Input v-model="featureForm.headerValue" /></Field>
            </template>
            <Field v-else-if="selectedFeature === 'transforms'">
              <FieldLabel>{{ t('removePathPrefix') }}</FieldLabel><Input v-model="featureForm.pathPrefix" />
            </Field>
            <Field v-else-if="selectedFeature === 'timeout'">
              <FieldLabel>{{ t('totalTimeout') }}</FieldLabel><Input v-model="featureForm.timeout" />
            </Field>
            <template v-else-if="selectedFeature === 'resilience'">
              <Field><FieldLabel>{{ t('retryCount') }}</FieldLabel><Input v-model.number="featureForm.retryCount" type="number" /></Field><Field><FieldLabel>{{ t('attemptTimeout') }}</FieldLabel><Input v-model="featureForm.attemptTimeout" /></Field><Field><FieldLabel>{{ t('failureRatio') }}</FieldLabel><Input v-model.number="featureForm.failureRatio" type="number" step="0.1" /></Field>
            </template>
            <template v-else-if="selectedFeature === 'cors'">
              <Field><FieldLabel>{{ t('allowedOrigins') }}</FieldLabel><Input v-model="featureForm.origins" /></Field><Field><FieldLabel>{{ t('allowedMethods') }}</FieldLabel><Input v-model="featureForm.corsMethods" /></Field><Field><FieldLabel>{{ t('allowedHeaders') }}</FieldLabel><Input v-model="featureForm.corsHeaders" /></Field><div class="flex items-center gap-2">
                <Switch v-model="featureForm.allowCredentials" /><label class="text-sm">{{ t('allowCredentials') }}</label>
              </div>
            </template>
            <template v-else-if="selectedFeature === 'ip-restrictions'">
              <Field><FieldLabel>{{ t('allowedCidrs') }}</FieldLabel><Textarea v-model="featureForm.allowedCidrs" /></Field><Field><FieldLabel>{{ t('deniedCidrs') }}</FieldLabel><Textarea v-model="featureForm.deniedCidrs" /></Field>
            </template>
            <Field v-else-if="selectedFeature === 'request-size'">
              <FieldLabel>{{ t('maximumRequestBytes') }}</FieldLabel><Input v-model.number="featureForm.maximumRequestBodyBytes" type="number" />
            </Field>
            <template v-else-if="selectedFeature === 'request-validation'">
              <Field><FieldLabel>{{ t('jsonSchema') }}</FieldLabel><Textarea v-model="featureForm.jsonSchema" rows="12" /></Field><Field><FieldLabel>{{ t('maximumValidatedBytes') }}</FieldLabel><Input v-model.number="featureForm.validationBodyBytes" type="number" /></Field>
            </template>
            <template v-else-if="selectedFeature === 'response-cache'">
              <Field><FieldLabel>{{ t('cacheLifetime') }}</FieldLabel><Input v-model="featureForm.cacheTtl" /></Field><Field><FieldLabel>{{ t('maximumCachedBytes') }}</FieldLabel><Input v-model.number="featureForm.cacheBodyBytes" type="number" /></Field><Field><FieldLabel>{{ t('varyHeaders') }}</FieldLabel><Input v-model="featureForm.varyHeaders" /></Field>
            </template>
            <template v-else-if="selectedFeature === 'mirror'">
              <Field>
                <FieldLabel>{{ t('mirrorRoute') }}</FieldLabel><Select v-model="featureForm.mirrorRoute">
                  <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                    <SelectItem v-for="option in mirrorTargetOptions" :key="option.value" :value="option.value">
                      {{ option.title }}
                    </SelectItem>
                  </SelectContent>
                </Select>
              </Field><Field><FieldLabel>{{ t('samplePercentage') }}</FieldLabel><Input v-model.number="featureForm.mirrorPercentage" type="number" /></Field>
            </template>
            <Alert v-else>
              <Info /><AlertDescription>{{ t('apiOnlyFeature') }}</AlertDescription>
            </Alert>
          </div>
          <DialogFooter>
            <Button variant="outline" @click="featureDialog = false">
              {{ t('cancel') }}
            </Button><Button :disabled="saving" @click="saveFeature">
              <Spinner v-if="saving" />{{ t('saveActivate') }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </template>
    <Skeleton v-else class="h-64 w-full" />
  </div>
</template>

<script setup lang="ts">
import type { RouteUrlSource } from '../utils/routeUrls';
import { Accordion, AccordionContent, AccordionItem, AccordionTrigger, Alert, AlertDescription, Button, Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle, Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, Field, FieldDescription, FieldLabel, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Skeleton, Spinner, Switch, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow, TagsInput, TagsInputInput, TagsInputItem, TagsInputItemDelete, TagsInputItemText, Textarea } from '@aditify/ui';
import { AlertCircle, ArrowRight, CircleCheck, Info, Pencil, Plus, Power, Settings2, Trash2 } from '@lucide/vue';
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';
import { graphql } from '../api';
import EnvironmentRequiredAlert from '../components/EnvironmentRequiredAlert.vue';
import IconButton from '../components/IconButton.vue';
import { confirmAction } from '../composables/confirmDialog';
import { loadEnvironments, selectedEnvironment, selectedEnvironmentId } from '../composables/environmentContext';
import { buildRoutePath, parseRoutePath } from '../utils/routePaths';
import { isHeaderTransform, isPathTransform, preservesOriginalHost, removeTransforms, replaceFirstTransform, transformValue } from '../utils/routeTransforms';
import { routeTestUrls } from '../utils/routeUrls';

type AnyObject = Record<string, any>;
interface FeatureDescriptor { id: string; category: string; displayName: string; description: string }
interface MatchRule { name: string; pattern: string; mode: string; isCaseSensitive: boolean }
interface DestinationRow { clientKey: string; id: string; address: string; healthAddress: string; pool: string; metadata?: Array<{ key: string; value: string }> }
interface RouteHostSummary { id: string; name: string; match: { hosts: string[] } }
interface UpstreamOption { id: string; name: string }

const upstreams = ref<UpstreamOption[]>([]);
const selectedUpstreamId = ref('__manual');

const currentRoute = useRoute(); const router = useRouter(); const routeModel = ref<(AnyObject & RouteUrlSource) | null>(null); const catalog = ref<FeatureDescriptor[]>([]); const certificates = ref<Array<{ id: string; name: string }>>([]); const mirrorTargets = ref<Array<{ id: string; name: string }>>([]); const routeHostSummaries = ref<RouteHostSummary[]>([]); const hsts = ref({ enabled: false, hosts: [] as string[], maxAge: 15552000, includeSubDomains: false, preload: false }); const error = ref(''); const message = ref(''); const dialogError = ref(''); const saving = ref(false); const togglingEnabled = ref(false); const catalogDialog = ref(false); const featureDialog = ref(false); const operationsDialog = ref(false); const selectedFeature = ref(''); const routeRuntime = ref({ activeRequests: 0, reportingInstances: 0 }); const basics = ref({ name: '', path: '', matchSubpaths: true, upstreamUrl: '', enabled: true, methods: '', hosts: [] as string[], inboundScheme: 'ANY', certificateId: null as string | null, webSocketsAllowed: true, pathHandling: 'PRESERVE', pathPrefixToRemove: '', preserveOriginalHost: true, order: null as number | null, headers: [] as MatchRule[], queryParameters: [] as MatchRule[], primaryDestinationId: 'primary', primaryHealthAddress: '', primaryPool: 'default', primaryMetadata: [] as Array<{ key: string; value: string }>, loadBalancingPolicy: 'PowerOfTwoChoices', upstreamHttpVersion: '2.0', upstreamVersionPolicy: 'RequestVersionOrLower', enableMultipleHttp2Connections: false, additionalDestinations: [] as DestinationRow[] });
const upstreamVersionPolicies = ['RequestVersionOrLower', 'RequestVersionOrHigher', 'RequestVersionExact'];
const inboundSchemes = computed(() => [{ title: t('schemeAny'), value: 'ANY' }, { title: t('schemeHttp'), value: 'HTTP_ONLY' }, { title: t('schemeHttps'), value: 'HTTPS_REDIRECT' }]);
const { t, te } = useI18n();
const { t: tg } = useI18n({ useScope: 'global' });
const isStaged = computed(() => selectedEnvironment.value?.publishingMode === 'STAGED');
const headerMatchModes = ['Exact', 'Prefix', 'Contains', 'NotContains', 'Exists', 'NotExists'];
const queryMatchModes = ['Exact', 'Prefix', 'Contains', 'NotContains', 'Exists'];
const loadBalancingPolicies = ['PowerOfTwoChoices', 'RoundRobin', 'LeastRequests', 'Random'];
const pathHandlingOptions = computed(() => [{ title: t('preservePath'), value: 'PRESERVE' }, { title: t('removePathPrefix'), value: 'STRIP_PREFIX' }]);
const operationalStateOptions = computed(() => [{ title: t('online'), value: 'ONLINE' }, { title: t('draining'), value: 'DRAINING' }, { title: t('maintenance'), value: 'MAINTENANCE' }, { title: t('offline'), value: 'OFFLINE' }]);
const responseProfiles = ref<Array<{ id: string; name: string }>>([]);
const responseProfileOptions = computed(() => [{ title: t('useEnvironmentDefault'), value: null }, ...responseProfiles.value.map(x => ({ title: x.name, value: x.id }))]);
const authenticationTypes = computed(() => [{ title: t('apiKey'), value: 'apiKey' }, { title: t('jwtAccessToken'), value: 'jwt' }]);
const headerDirections = computed(() => [{ title: t('requestHeader'), value: 'RequestHeader' }, { title: t('responseHeader'), value: 'ResponseHeader' }]);
const operationsForm = ref({ state: 'ONLINE', responseProfileId: null as string | null });
const hostDelimiter = /[\n,]+/;
const defaults = () => ({ authType: 'apiKey', authority: 'https://', issuer: '', audiences: '', rateType: 'fixedWindow', permitLimit: 100, window: 'PT1M', partitionBy: 'global', headerDirection: 'RequestHeader', headerName: '', headerValue: '', pathPrefix: '', timeout: 'PT30S', retryCount: 1, attemptTimeout: 'PT10S', failureRatio: 0.5, origins: '*', corsMethods: 'GET, POST, PUT, PATCH, DELETE, OPTIONS', corsHeaders: '*', allowCredentials: false, allowedCidrs: '', deniedCidrs: '', maximumRequestBodyBytes: 10485760, jsonSchema: '{\n  "type": "object"\n}', validationBodyBytes: 1048576, cacheTtl: 'PT1M', cacheBodyBytes: 1048576, varyHeaders: '', mirrorRoute: '', mirrorPercentage: 100 });
const featureForm = ref(defaults());
const featureTitle = computed(() => featureName(selectedFeature.value, catalog.value.find(x => x.id === selectedFeature.value)?.displayName || t('configureFeature')));
const featureHelp = computed(() => {
  const key = `featureHelp.${selectedFeature.value}`;

  return te(key) ? t(key) : t('featureHelp.default');
});
const mirrorTargetOptions = computed(() => mirrorTargets.value.filter(x => x.id !== routeModel.value?.id).map(x => ({ title: `${x.name} (${x.id})`, value: x.id })));
const featureHeaders = computed(() => [
  { title: t('feature'), key: 'name' },
  { title: t('configuration'), key: 'summary' },
  { title: t('status'), key: 'enabled', width: 150 },
  { title: t('actions'), key: 'actions', align: 'end' as const, width: 220 },
]);
const configuredFeatures = computed(() => {
  if (!routeModel.value)
    return [];

  const f = routeModel.value.features;
  const headerTransform = f.transforms?.find(isHeaderTransform); const pathTransform = f.transforms?.find(isPathTransform);
  const values = [{ id: 'authorization', icon: 'shield-key-outline', value: f.authorization }, { id: 'rate-limit', icon: 'speedometer', value: f.rateLimit }, { id: 'headers', icon: 'swap-horizontal', value: headerTransform }, { id: 'transforms', icon: 'arrow-decision', value: pathTransform }, { id: 'timeout', icon: 'timer-outline', value: f.timeout }, { id: 'resilience', icon: 'shield-refresh-outline', value: f.resilience }, { id: 'cors', icon: 'web', value: f.cors }, { id: 'ip-restrictions', icon: 'ip-network-outline', value: f.access?.allowedCidrs?.length || f.access?.deniedCidrs?.length ? f.access : null }, { id: 'request-size', icon: 'file-arrow-up-down-outline', value: f.access?.maximumRequestBodyBytes ? f.access : null }, { id: 'request-validation', icon: 'check-decagram-outline', value: f.requestValidation }, { id: 'response-cache', icon: 'cached', value: f.responseCache }, { id: 'mirror', icon: 'content-copy', value: f.mirror }];

  const disabled = new Set<string>(f.disabledFeatures || []);

  return values.filter(x => x.value).map(x => ({ ...x, enabled: !disabled.has(x.id), name: featureName(x.id, x.id), summary: summary(x.id, x.value) }));
});
const availableFeatures = computed(() => catalog.value.filter(x => !configuredFeatures.value.some(y => y.id === x.id)));
const hstsCoveredHosts = computed(() => basics.value.hosts.filter(host => hsts.value.hosts.some(pattern => hostMatches(pattern, host))));
const wildcardHosts = computed(() => routeModel.value?.match.hosts.filter(host => host.includes('*')) || []);
const sharedHstsRouteCount = computed(() => {
  if (!routeModel.value || !hstsCoveredHosts.value.length)
    return 0;

  return routeHostSummaries.value.filter(route => route.id !== routeModel.value?.id && route.match.hosts.some(otherHost => hstsCoveredHosts.value.some(host => hostPatternsOverlap(host, otherHost)))).length;
});
const hstsStatus = computed(() => {
  if (!basics.value.hosts.length)
    return { color: 'warning', label: t('notConfigured'), description: t('hstsRequiresHosts') };

  if (!hsts.value.enabled)
    return { color: undefined, label: t('disabled'), description: t('hstsGloballyDisabled') };

  if (!hstsCoveredHosts.value.length)
    return { color: undefined, label: t('notEnabled'), description: t('hstsNotCovered') };

  if (basics.value.inboundScheme === 'HTTP_ONLY')
    return { color: 'warning', label: t('configurationWarning'), description: t('hstsHttpOnlyWarning') };

  return { color: 'success', label: t('enabled'), description: t('hstsCovered', { hosts: hstsCoveredHosts.value.join(', ') }) };
});

function summary(id: string, value: AnyObject) {
  if (id === 'authorization')
    return value.type === 'jwt' ? t('jwtValidation') : t('apiKeyRequired');

  if (id === 'rate-limit')
    return t('rateSummary', { count: value.permitLimit, type: value.type });

  if (id === 'timeout')
    return value.total;

  if (id === 'response-cache')
    return t('cacheSummary', { duration: value.timeToLive });

  if (id === 'headers') {
    const transform = value as unknown as Array<{ key: string; value: string }>;

    return `${transformValue(transform, 'RequestHeader') || transformValue(transform, 'ResponseHeader')}: ${transformValue(transform, 'Set') || ''}`;
  }

  if (id === 'transforms') {
    const transform = value as unknown as Array<{ key: string; value: string }>;

    return t('removePrefixSummary', { prefix: transformValue(transform, 'PathRemovePrefix') });
  }

  return t('configured');
}
function featureName(id: string, fallback: string) { return te(`featureNames.${id}`) ? t(`featureNames.${id}`) : fallback; }
function featureDescription(id: string, fallback: string) { return te(`featureDescriptions.${id}`) ? t(`featureDescriptions.${id}`) : fallback; }
function featureCategory(category: string) {
  const key = `featureCategories.${category.toLowerCase().replace(/\s+/g, '-')}`;

  return te(key) ? t(key) : category;
}
function csv(value: string) { return value.split(/[\n,]+/).map(x => x.trim()).filter(Boolean); }
function hostMatches(pattern: string, host: string) {
  const normalizedPattern = pattern.trim().replace(/\.$/, '').toLowerCase();
  const normalizedHost = host.trim().replace(/\.$/, '').toLowerCase();

  return normalizedPattern.startsWith('*.')
    ? normalizedHost.endsWith(normalizedPattern.slice(1)) && normalizedHost.split('.').length === normalizedPattern.split('.').length
    : normalizedPattern === normalizedHost;
}
function hostPatternsOverlap(first: string, second: string) {
  const a = first.trim().replace(/\.$/, '').toLowerCase(); const b = second.trim().replace(/\.$/, '').toLowerCase();

  return a === b || (!a.startsWith('*.') && hostMatches(b, a)) || (!b.startsWith('*.') && hostMatches(a, b));
}
function stateLabel(state: string) { return t(state.toLowerCase()); }
function stateColor(state: string) { return state === 'ONLINE' ? 'success' : state === 'DRAINING' ? 'warning' : 'error'; }
function stateIcon(state: string) { return state === 'ONLINE' ? 'earth' : state === 'DRAINING' ? 'timer-sand' : state === 'MAINTENANCE' ? 'wrench-outline' : 'web-off'; }
function addMatchRule(collection: MatchRule[]) { collection.push({ name: '', pattern: '', mode: 'Exact', isCaseSensitive: false }); }
function matchingRules(values: MatchRule[]) { return values.map(x => ({ ...x, name: x.name.trim(), pattern: x.mode === 'Exists' || x.mode === 'NotExists' ? '' : x.pattern })); }
function addDestination() { basics.value.additionalDestinations.push({ clientKey: crypto.randomUUID(), id: `destination-${basics.value.additionalDestinations.length + 2}`, address: 'https://', healthAddress: '', pool: 'default' }); }
function pathPrefix(features: AnyObject) {
  for (const transform of features?.transforms || []) {
    const pairs = Array.isArray(transform) ? transform : [transform];
    const match = pairs.find((x: AnyObject) => x.key === 'PathRemovePrefix');

    if (match)
      return match.value || '';
  }

  return '';
}
function suggestedPathPrefix() {
  const marker = basics.value.path.indexOf('{');
  const fixedPath = (marker >= 0 ? basics.value.path.slice(0, marker) : basics.value.path).replace(/\/+$/, '');

  return fixedPath && fixedPath !== '/' ? fixedPath : '';
}
function setPathHandling(value: string) {
  if (value === 'STRIP_PREFIX' && !basics.value.pathPrefixToRemove)
    basics.value.pathPrefixToRemove = suggestedPathPrefix();
}
async function load() {
  if (!selectedEnvironmentId.value)
    return;

  error.value = '';

  try {
    const data = await graphql<any>(`query RouteDetail($environmentId:UUID!,$routeId:String!){route(environmentId:$environmentId,routeId:$routeId){id name version enabled order inbound{scheme certificateId webSocketsAllowed} operations{state responseProfileId response{statusCode title message retryAfter upstreamUrl}} match{path methods hosts headers{name pattern mode isCaseSensitive} queryParameters{name pattern mode isCaseSensitive}} upstream{url loadBalancingPolicy httpClient{version versionPolicy enableMultipleHttp2Connections} destinations{key value{address healthAddress pool metadata{key value}}}} features{disabledFeatures authorization{type requiredScopes authority issuer audiences requiredClaims{key value} policies clockSkew} rateLimit{type permitLimit window queueLimit partitionBy partitionName segmentsPerWindow tokensPerPeriod queueOrder} timeout{total} resilience{retryCount attemptTimeout statusCodes allowedMethods maximumBufferedRequestBytes failureRatio samplingDuration minimumThroughput breakDuration retryTransportFailures backoff jitter} cors{origins methods headers exposedHeaders allowCredentials preflightMaxAge} transforms{key value} mirror{clusterId percentage allowedMethods maximumBufferedBodyBytes timeout removeHeaders} access{allowedCidrs deniedCidrs maximumRequestBodyBytes} requestValidation{jsonSchema maximumBodyBytes} responseCache{timeToLive maximumBodyBytes varyByHeaders}}} inboundCertificates{id name} inboundSecuritySettings{hstsEnabled hstsHosts hstsMaxAgeSeconds hstsIncludeSubDomains hstsPreload} routes(environmentId:$environmentId){id name match{hosts}} routeFeatureCatalog{id category displayName description} routeRuntimeStatuses(environmentId:$environmentId){routeId activeRequests reportingInstances} routeUnavailableResponseProfiles(environmentId:$environmentId){id name}}`, { environmentId: selectedEnvironmentId.value, routeId: String(currentRoute.params.routeId) });

    const upstreamData = await graphql<{ route: { upstream: { upstreamId?: string | null } } | null; upstreams: UpstreamOption[] }>(`query RouteUpstreamOptions($environmentId:UUID!,$routeId:String!){route(environmentId:$environmentId,routeId:$routeId){upstream{upstreamId}} upstreams(environmentId:$environmentId){id name}}`, { environmentId: selectedEnvironmentId.value, routeId: String(currentRoute.params.routeId) });

    upstreams.value = upstreamData.upstreams; selectedUpstreamId.value = upstreamData.route?.upstream.upstreamId || '__manual'; routeModel.value = data.route; mirrorTargets.value = data.routes; routeHostSummaries.value = data.routes; catalog.value = data.routeFeatureCatalog; responseProfiles.value = data.routeUnavailableResponseProfiles; routeRuntime.value = data.routeRuntimeStatuses.find((x: AnyObject) => x.routeId === data.route?.id) || { activeRequests: 0, reportingInstances: 0 };
    hsts.value = { enabled: data.inboundSecuritySettings.hstsEnabled, hosts: data.inboundSecuritySettings.hstsHosts, maxAge: data.inboundSecuritySettings.hstsMaxAgeSeconds, includeSubDomains: data.inboundSecuritySettings.hstsIncludeSubDomains, preload: data.inboundSecuritySettings.hstsPreload };

    if (data.route) {
      const destinations = data.route.upstream.destinations || [];
      const primary = destinations[0] || { key: 'primary', value: { address: data.route.upstream.url, healthAddress: null, pool: 'default', metadata: [] } };
      const removedPrefix = pathPrefix(data.route.features);
      const operations = data.route.operations || { state: 'ONLINE' };
      const incomingPath = parseRoutePath(data.route.match.path);

      basics.value = { name: data.route.name, path: incomingPath.path, matchSubpaths: incomingPath.matchSubpaths, upstreamUrl: primary.value.address, enabled: data.route.enabled, methods: data.route.match.methods.join(', '), hosts: [...data.route.match.hosts], inboundScheme: data.route.inbound?.scheme || 'ANY', certificateId: data.route.inbound?.certificateId || null, webSocketsAllowed: data.route.inbound?.webSocketsAllowed ?? true, pathHandling: removedPrefix ? 'STRIP_PREFIX' : 'PRESERVE', pathPrefixToRemove: removedPrefix, preserveOriginalHost: preservesOriginalHost(data.route.features.transforms), order: data.route.order, headers: data.route.match.headers.map((x: MatchRule) => ({ ...x })), queryParameters: data.route.match.queryParameters.map((x: MatchRule) => ({ ...x })), primaryDestinationId: primary.key, primaryHealthAddress: primary.value.healthAddress || '', primaryPool: primary.value.pool || 'default', primaryMetadata: primary.value.metadata || [], loadBalancingPolicy: data.route.upstream.loadBalancingPolicy, upstreamHttpVersion: data.route.upstream.httpClient?.version || '2.0', upstreamVersionPolicy: data.route.upstream.httpClient?.versionPolicy || 'RequestVersionOrLower', enableMultipleHttp2Connections: data.route.upstream.httpClient?.enableMultipleHttp2Connections ?? false, additionalDestinations: destinations.slice(1).map((x: AnyObject) => ({ clientKey: crypto.randomUUID(), id: x.key, address: x.value.address, healthAddress: x.value.healthAddress || '', pool: x.value.pool || 'default', metadata: x.value.metadata || [] })) }; certificates.value = data.inboundCertificates;
      operationsForm.value = { state: operations.state, responseProfileId: operations.responseProfileId || null };
    }
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}
async function refreshRuntimeStatus() {
  if (!selectedEnvironmentId.value || !routeModel.value)
    return;

  try {
    const data = await graphql<{ routeRuntimeStatuses: Array<{ routeId: string; activeRequests: number; reportingInstances: number }> }>(`query RouteRuntimeStatuses($environmentId:UUID!){routeRuntimeStatuses(environmentId:$environmentId){routeId activeRequests reportingInstances}}`, { environmentId: selectedEnvironmentId.value });

    routeRuntime.value = data.routeRuntimeStatuses.find(x => x.routeId === routeModel.value?.id) || { activeRequests: 0, reportingInstances: 0 };
  }
  catch { /* Editing remains available during a transient diagnostics failure. */ }
}
async function saveOperationalState() {
  const route = routeModel.value;

  if (!route)
    return;

  await run(async () => {
    const v = operationsForm.value; const input = { state: v.state, responseProfileId: v.responseProfileId, useEnvironmentDefault: !v.responseProfileId };

    await graphql(`mutation SetRouteOperationalState($environmentId:UUID!,$routeId:String!,$version:String!,$input:UpdateRouteOperationalStateInput!){setRouteOperationalState(environmentId:$environmentId,routeId:$routeId,expectedRouteVersion:$version,input:$input){route{id version operations{state}}}}`, { environmentId: selectedEnvironmentId.value, routeId: route.id, version: route.version, input });
    operationsDialog.value = false; message.value = t('trafficStateSaved'); await load(); await loadEnvironments();
  });
}
async function toggleRouteEnabled() {
  const route = routeModel.value;

  if (!route)
    return;

  togglingEnabled.value = true; error.value = '';

  try {
    await graphql(`mutation SetRouteEnabled($environmentId:UUID!,$routeId:String!,$version:String!,$enabled:Boolean!){setRouteEnabled(environmentId:$environmentId,routeId:$routeId,expectedRouteVersion:$version,enabled:$enabled){route{id version enabled}}}`, { environmentId: selectedEnvironmentId.value, routeId: route.id, version: route.version, enabled: !route.enabled });
    message.value = t(route.enabled ? 'routeDisabled' : 'routeEnabledSaved'); await load(); await loadEnvironments();
  }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
  finally { togglingEnabled.value = false; }
}
async function saveBasics() {
  const route = routeModel.value;

  if (!route)
    return;

  await run(async () => {
    const destinationRows = selectedUpstreamId.value === '__manual' ? [{ id: basics.value.primaryDestinationId, address: basics.value.upstreamUrl, healthAddress: basics.value.primaryHealthAddress, pool: basics.value.primaryPool, metadata: basics.value.primaryMetadata }, ...basics.value.additionalDestinations] : [];
    const destinationNames = destinationRows.map(x => x.id.trim());

    if (destinationNames.some(x => !x) || new Set(destinationNames.map(x => x.toLowerCase())).size !== destinationNames.length)
      throw new Error(t('destinationNamesError'));

    if (basics.value.pathHandling === 'STRIP_PREFIX' && !basics.value.pathPrefixToRemove.startsWith('/'))
      throw new Error(t('pathPrefixError'));

    const hosts = [...new Set(basics.value.hosts.map(x => x.trim()).filter(Boolean))];

    if (basics.value.certificateId && hosts.length === 0)
      throw new Error(t('certificateRequiresHosts'));

    const destinations = destinationRows.map(x => ({ key: x.id.trim(), value: { address: x.address.trim(), healthAddress: x.healthAddress.trim() || null, pool: x.pool.trim() || 'default', metadata: x.metadata?.length ? x.metadata : null } }));
    const input = { name: basics.value.name, path: buildRoutePath(basics.value.path, basics.value.matchSubpaths), upstreamUrl: selectedUpstreamId.value === '__manual' ? basics.value.upstreamUrl : null, upstreamId: selectedUpstreamId.value === '__manual' ? null : selectedUpstreamId.value, enabled: basics.value.enabled, methods: csv(basics.value.methods), hosts, inbound: { scheme: basics.value.inboundScheme, certificateId: basics.value.inboundScheme === 'HTTP_ONLY' ? null : basics.value.certificateId, webSocketsAllowed: basics.value.webSocketsAllowed }, httpClient: { version: basics.value.upstreamHttpVersion, versionPolicy: basics.value.upstreamVersionPolicy, enableMultipleHttp2Connections: basics.value.enableMultipleHttp2Connections }, pathHandling: basics.value.pathHandling, pathPrefixToRemove: basics.value.pathHandling === 'STRIP_PREFIX' ? basics.value.pathPrefixToRemove : null, preserveOriginalHost: basics.value.preserveOriginalHost, order: basics.value.order, headers: matchingRules(basics.value.headers), queryParameters: matchingRules(basics.value.queryParameters), destinations: selectedUpstreamId.value === '__manual' ? destinations : null, loadBalancingPolicy: basics.value.loadBalancingPolicy };

    await graphql(`mutation UpdateBasics($environmentId:UUID!,$routeId:String!,$version:String!,$input:UpdateManagedRouteBasicsInput!){updateRouteBasics(environmentId:$environmentId,routeId:$routeId,expectedRouteVersion:$version,input:$input){revision{id}}}`, { environmentId: selectedEnvironmentId.value, routeId: route.id, version: route.version, input }); message.value = isStaged.value ? tg('common.savedUnpublished') : t('routeSaved'); await loadEnvironments(); await load();
  });
}
function openFeature(id: string) {
  selectedFeature.value = id; featureForm.value = defaults();

  const f = routeModel.value?.features || {};

  if (id === 'authorization' && f.authorization)
    Object.assign(featureForm.value, { authType: f.authorization.type, authority: f.authorization.authority || '', issuer: f.authorization.issuer || '', audiences: (f.authorization.audiences || []).join(', ') });

  if (id === 'rate-limit' && f.rateLimit)
    Object.assign(featureForm.value, { rateType: f.rateLimit.type, permitLimit: f.rateLimit.permitLimit, window: f.rateLimit.window || 'PT1M', partitionBy: f.rateLimit.partitionBy });

  if (id === 'headers') {
    const transform = f.transforms?.find(isHeaderTransform);

    if (transform)
      Object.assign(featureForm.value, { headerDirection: transformValue(transform, 'RequestHeader') !== undefined ? 'RequestHeader' : 'ResponseHeader', headerName: transformValue(transform, 'RequestHeader') || transformValue(transform, 'ResponseHeader') || '', headerValue: transformValue(transform, 'Set') || '' });
  }

  if (id === 'transforms') {
    const transform = f.transforms?.find(isPathTransform);

    if (transform)
      featureForm.value.pathPrefix = transformValue(transform, 'PathRemovePrefix') || '';
  }

  if (id === 'timeout' && f.timeout)
    featureForm.value.timeout = f.timeout.total;

  if (id === 'resilience' && f.resilience)
    Object.assign(featureForm.value, { retryCount: f.resilience.retryCount, attemptTimeout: f.resilience.attemptTimeout, failureRatio: f.resilience.failureRatio });

  if (id === 'cors' && f.cors)
    Object.assign(featureForm.value, { origins: f.cors.origins.join(', '), corsMethods: f.cors.methods.join(', '), corsHeaders: f.cors.headers.join(', '), allowCredentials: f.cors.allowCredentials });

  if (id === 'ip-restrictions' && f.access)
    Object.assign(featureForm.value, { allowedCidrs: (f.access.allowedCidrs || []).join('\n'), deniedCidrs: (f.access.deniedCidrs || []).join('\n') });

  if (id === 'request-size' && f.access)
    featureForm.value.maximumRequestBodyBytes = f.access.maximumRequestBodyBytes || 10485760;

  if (id === 'request-validation' && f.requestValidation)
    Object.assign(featureForm.value, { jsonSchema: f.requestValidation.jsonSchema, validationBodyBytes: f.requestValidation.maximumBodyBytes });

  if (id === 'response-cache' && f.responseCache)
    Object.assign(featureForm.value, { cacheTtl: f.responseCache.timeToLive, cacheBodyBytes: f.responseCache.maximumBodyBytes, varyHeaders: (f.responseCache.varyByHeaders || []).join(', ') });

  if (id === 'mirror' && f.mirror)
    Object.assign(featureForm.value, { mirrorRoute: f.mirror.clusterId, mirrorPercentage: f.mirror.percentage });

  catalogDialog.value = false; featureDialog.value = true;
}
function normalizedFeatures() {
  const f = JSON.parse(JSON.stringify(routeModel.value?.features || {})) as AnyObject;

  for (const key of Object.keys(f)) {
    if (f[key] === null)
      delete f[key];
  }

  return f;
}
async function saveFeature() {
  const f = normalizedFeatures(); const v = featureForm.value;

  if (!configuredFeatures.value.some(feature => feature.id === selectedFeature.value))
    f.disabledFeatures = (f.disabledFeatures || []).filter((id: string) => id !== selectedFeature.value);

  switch (selectedFeature.value) { case 'authorization': f.authorization = v.authType === 'jwt' ? { ...(f.authorization || {}), type: 'jwt', authority: v.authority, issuer: v.issuer, audiences: csv(v.audiences) } : { type: 'apiKey' }; break; case 'rate-limit': f.rateLimit = { ...(f.rateLimit || {}), type: v.rateType, permitLimit: v.permitLimit, window: v.rateType === 'concurrency' ? null : v.window, queueLimit: f.rateLimit?.queueLimit ?? 0, partitionBy: v.partitionBy, segmentsPerWindow: f.rateLimit?.segmentsPerWindow ?? 4, queueOrder: f.rateLimit?.queueOrder ?? 'oldestFirst' }; break; case 'headers': f.transforms = replaceFirstTransform(f.transforms, isHeaderTransform, [{ key: v.headerDirection, value: v.headerName }, { key: 'Set', value: v.headerValue }]); break; case 'transforms': f.transforms = replaceFirstTransform(f.transforms, isPathTransform, [{ key: 'PathRemovePrefix', value: v.pathPrefix }]); break; case 'timeout': f.timeout = { ...(f.timeout || {}), total: v.timeout }; break; case 'resilience': f.resilience = { ...(f.resilience || {}), retryCount: v.retryCount, attemptTimeout: v.attemptTimeout, maximumBufferedRequestBytes: f.resilience?.maximumBufferedRequestBytes ?? 0, failureRatio: v.failureRatio, retryTransportFailures: f.resilience?.retryTransportFailures ?? true, jitter: f.resilience?.jitter ?? true }; break; case 'cors': f.cors = { ...(f.cors || {}), origins: csv(v.origins), methods: csv(v.corsMethods), headers: csv(v.corsHeaders), allowCredentials: v.allowCredentials }; break; case 'ip-restrictions': f.access = { ...(f.access || {}), allowedCidrs: csv(v.allowedCidrs), deniedCidrs: csv(v.deniedCidrs) }; break; case 'request-size': f.access = { ...(f.access || {}), maximumRequestBodyBytes: v.maximumRequestBodyBytes }; break; case 'request-validation': f.requestValidation = { ...(f.requestValidation || {}), jsonSchema: v.jsonSchema, maximumBodyBytes: v.validationBodyBytes }; break; case 'response-cache': f.responseCache = { ...(f.responseCache || {}), timeToLive: v.cacheTtl, maximumBodyBytes: v.cacheBodyBytes, varyByHeaders: csv(v.varyHeaders) }; break; case 'mirror': f.mirror = { ...(f.mirror || {}), clusterId: v.mirrorRoute, percentage: v.mirrorPercentage, maximumBufferedBodyBytes: f.mirror?.maximumBufferedBodyBytes ?? 0 }; break; }

  await saveFeatures(f); featureDialog.value = false;
}
async function removeFeature(id: string) {
  if (!await confirmAction(t('removeFeatureMessage'), { title: t('removeFeatureTitle'), confirmText: t('removeActivate'), color: 'error' }))
    return;

  const f = normalizedFeatures();

  f.disabledFeatures = (f.disabledFeatures || []).filter((featureId: string) => featureId !== id);

  if (id === 'headers') {
    f.transforms = removeTransforms(f.transforms, isHeaderTransform);
  }
  else if (id === 'transforms') {
    f.transforms = removeTransforms(f.transforms, isPathTransform);
  }
  else if (id === 'ip-restrictions' && f.access) {
    delete f.access.allowedCidrs; delete f.access.deniedCidrs;

    if (!f.access.maximumRequestBodyBytes)
      delete f.access;
  }
  else if (id === 'request-size' && f.access) {
    delete f.access.maximumRequestBodyBytes;

    if (!f.access.allowedCidrs?.length && !f.access.deniedCidrs?.length)
      delete f.access;
  }
  else {
    const map: Record<string, string> = { 'rate-limit': 'rateLimit', 'timeout': 'timeout', 'resilience': 'resilience', 'authorization': 'authorization', 'cors': 'cors', 'request-validation': 'requestValidation', 'response-cache': 'responseCache', 'mirror': 'mirror' };

    delete f[map[id] || id];
  }

  await saveFeatures(f);
}
async function toggleFeature(id: string, enabled: boolean) {
  const f = normalizedFeatures();
  const disabled = new Set<string>(f.disabledFeatures || []);

  if (enabled)
    disabled.delete(id);
  else
    disabled.add(id);

  f.disabledFeatures = [...disabled];
  await saveFeatures(f);
}
async function saveFeatures(features: AnyObject) { await run(async () => { await graphql(`mutation UpdateFeatures($environmentId:UUID!,$routeId:String!,$version:String!,$input:ManagedRouteFeaturesInput!){updateRouteFeatures(environmentId:$environmentId,routeId:$routeId,expectedRouteVersion:$version,input:$input){revision{id}}}`, { environmentId: selectedEnvironmentId.value, routeId: routeModel.value!.id, version: routeModel.value!.version, input: features }); message.value = isStaged.value ? tg('common.savedUnpublished') : t('featureSaved'); await loadEnvironments(); await load(); }); }
async function deleteRoute() {
  const route = routeModel.value;

  if (!route || !await confirmAction(t('deleteRouteMessage'), { title: t('deleteRouteTitle', { name: route.name }), confirmText: t('deleteRoute'), color: 'error' }))
    return;

  await run(async () => { await graphql(`mutation DeleteRoute($environmentId:UUID!,$routeId:String!,$version:String!){deleteRoute(environmentId:$environmentId,routeId:$routeId,expectedRouteVersion:$version){revision{id}}}`, { environmentId: selectedEnvironmentId.value, routeId: route.id, version: route.version }); await loadEnvironments(); await router.push('/routes'); });
}
async function run(action: () => Promise<void>) {
  saving.value = true; error.value = ''; message.value = ''; dialogError.value = '';

  try { await action(); }
  catch (e) {
    const value = e instanceof Error ? e.message : String(e);

    error.value = value; dialogError.value = value;
  }
  finally { saving.value = false; }
}

let runtimeRefresh: ReturnType<typeof setInterval> | undefined;

watch(selectedEnvironmentId, load); onMounted(async () => {
  if (!selectedEnvironmentId.value)
    await loadEnvironments();

  await load();
  runtimeRefresh = setInterval(() => void refreshRuntimeStatus(), 5000);
});
onUnmounted(() => clearInterval(runtimeRefresh));
</script>

<i18n lang="json">
{
  "en": {
    "certificateRequiresHosts": "Add at least one explicit incoming hostname before assigning a TLS certificate.",
    "matchSubpaths": "Match this path and all subpaths",
    "matchSubpathsHint": "Adds the catch-all route pattern automatically when saved.",
    "preserveOriginalHost": "Preserve incoming Host header",
    "preserveOriginalHostHelp": "Send the public request hostname to the upstream. Enable this when the upstream selects a site by hostname, such as an IIS host binding.",
    "feature": "Feature",
    "configuration": "Configuration",
    "status": "Status",
    "actions": "Actions",
    "disableFeatureNamed": "Disable {name}",
    "enableFeatureNamed": "Enable {name}",
    "removePrefixSummary": "Remove path prefix {prefix}",
    "inboundScheme": "Incoming scheme", "inboundCertificate": "TLS certificate", "webSocketsAllowed": "Allow WebSocket upgrades", "schemeAny": "HTTP and HTTPS when available", "schemeHttp": "HTTP only", "schemeHttps": "HTTPS, redirect HTTP", "upstreamProtocol": "Upstream protocol", "upstreamProtocolHelp": "Control the HTTP version used between the gateway and upstream destinations.", "upstreamHttpVersion": "Upstream HTTP version", "upstreamVersionPolicy": "HTTP version policy", "multipleHttp2Connections": "Allow multiple HTTP/2 connections", "multipleHttp2ConnectionsHelp": "Opens another connection when the upstream's current HTTP/2 connection has no available request streams. This can improve throughput for busy routes, but uses more upstream connections.", "routeHsts": "HSTS for route hostnames", "sharedHostnamePolicy": "Shared hostname policy", "hstsSharedRoutes": "The same hostname policy affects {count} other route in this environment. | The same hostname policy affects {count} other routes in this environment.", "manageHsts": "Manage HSTS", "notConfigured": "Not configured", "notEnabled": "Not enabled", "configurationWarning": "Review configuration", "hstsRequiresHosts": "Add explicit incoming hostnames to evaluate and use HSTS safely.", "hstsGloballyDisabled": "HSTS is disabled globally in System settings.", "hstsNotCovered": "None of this route's incoming hostnames are covered by the global HSTS policy.", "hstsHttpOnlyWarning": "The global HSTS policy covers this hostname, but this route accepts HTTP only. Browsers that received HSTS will try HTTPS instead.", "hstsCovered": "HSTS is emitted on HTTPS responses for: {hosts}.",
    "route": "Route", "to": "to", "enabled": "Enabled", "disabled": "Disabled", "delete": "Delete", "routeSummary": "Route summary", "incomingRequest": "Incoming request", "anyHost": "Any host", "allMethods": "All methods", "upstreamDestinations": "Upstream destinations", "technicalId": "Technical ID", "routeVersion": "Route version", "runtimeSummary": "{active} active requests from {instances} instances", "loadBalancing": "Load balancing", "notOnlineHelp": "New requests do not use the route's normal features or upstream destinations.", "routing": "Routing", "name": "Name", "routeEnabled": "Route enabled", "incomingPath": "Incoming path", "upstreamUrl": "Upstream URL", "incomingHosts": "Incoming hosts", "incomingHostsHint": "Press Enter after each host, for example example.com or *.example.com. Leave empty to accept every host.", "pathHandling": "Upstream path handling", "pathPrefix": "Path prefix to remove", "pathPrefixHint": "The prefix is removed before forwarding. Query parameters are preserved.", "advancedMatching": "Advanced matching", "overlapTitle": "When routes overlap:", "overlapHelp": "Precedence decides which matching route wins. Lower numbers have higher priority. Leave it empty unless you need to override the normal route matching order.", "precedence": "Precedence", "precedenceHint": "Lower numbers are evaluated first.", "allowedMethods": "Allowed methods", "allowedMethodsHint": "Comma separated. Leave empty to match all methods.", "headerConditions": "Header conditions", "headerConditionsHelp": "Every condition must match the incoming request.", "addHeader": "Add header", "headerName": "Header name", "match": "Match", "value": "Value", "caseSensitive": "Case sensitive", "removeHeaderCondition": "Remove header condition {number}", "noHeaderConditions": "No header conditions. Every request header is accepted.", "queryConditions": "Query parameter conditions", "queryConditionsHelp": "Every condition must match the incoming query string.", "addQueryParameter": "Add query parameter", "parameterName": "Parameter name", "removeQueryCondition": "Remove query condition {number}", "noQueryConditions": "No query conditions. Every query string is accepted.", "advancedUpstream": "Advanced upstream", "advancedUpstreamHelp": "Requests are distributed across healthy destinations. The primary destination uses the Upstream URL shown above.", "additionalDestinations": "Additional destinations", "additionalDestinationsHelp": "Add another instance or upstream endpoint for this route.", "addDestination": "Add destination", "destinationName": "Destination name", "destinationNameHint": "Unique within this route", "destinationUrl": "Destination URL", "healthUrl": "Health URL (optional)", "pool": "Pool", "removeDestination": "Remove destination {number}", "primaryOnly": "Only the primary upstream is configured.", "saveActivate": "Save and activate", "routeTrafficState": "Route traffic state", "trafficStateHelp": "Draining lets requests already in flight finish while rejecting new requests. Maintenance and Offline override all normal route features and upstream selection.", "trafficState": "Traffic state", "unavailableResponse": "Unavailable response", "unavailableResponseHint": "Inherit the environment default or override it with a shared response configured in Settings.", "cancel": "Cancel", "gatewayFeatures": "Gateway features", "gatewayFeaturesHelp": "Add only the behavior this route needs.", "addFeature": "Add feature", "configure": "Configure", "removeFeatureNamed": "Remove {name}", "noFeatures": "No optional features", "noFeaturesText": "The route is live with simple forwarding. Add security, traffic control, transformations, or reliability features when needed.", "addGatewayFeature": "Add a gateway feature", "close": "Close", "authenticationType": "Authentication type", "authorityUrl": "Authority URL", "expectedIssuer": "Expected issuer", "audiences": "Audiences", "commaSeparated": "Comma separated", "algorithm": "Algorithm", "requestLimit": "Request limit", "window": "Window", "durationMinuteHint": "ISO 8601 duration, for example PT1M", "limitBy": "Limit by", "direction": "Direction", "removePathPrefix": "Remove path prefix", "totalTimeout": "Total timeout", "durationSecondsHint": "ISO 8601 duration, for example PT30S", "retryCount": "Retry count", "attemptTimeout": "Per-attempt timeout", "failureRatio": "Circuit failure ratio", "allowedOrigins": "Allowed origins", "allowedHeaders": "Allowed headers", "allowCredentials": "Allow credentials", "allowedCidrs": "Allowed CIDR ranges", "deniedCidrs": "Denied CIDR ranges", "onePerLine": "One per line", "maximumRequestBytes": "Maximum request body bytes", "jsonSchema": "JSON Schema", "maximumValidatedBytes": "Maximum validated body bytes", "cacheLifetime": "Cache lifetime", "maximumCachedBytes": "Maximum cached response bytes", "varyHeaders": "Vary by request headers", "mirrorRoute": "Mirror to route", "mirrorRouteHint": "Choose an existing route or enter a route ID.", "samplePercentage": "Sample percentage", "apiOnlyFeature": "This feature is configured through the typed advanced fields in the management API.", "preservePath": "Preserve incoming path", "online": "Online", "draining": "Draining", "maintenance": "Maintenance", "offline": "Offline", "useEnvironmentDefault": "Use environment default", "apiKey": "API key", "jwtAccessToken": "JWT access token", "requestHeader": "Request header", "responseHeader": "Response header", "configureFeature": "Configure feature", "jwtValidation": "JWT token validation", "apiKeyRequired": "API key required", "rateSummary": "{count} requests, {type}", "cacheSummary": "Cache for {duration}", "configured": "Configured", "trafficStateSaved": "Route traffic state saved and activated.", "routeDisabled": "Route disabled and activated.", "routeEnabledSaved": "Route enabled and activated.", "destinationNamesError": "Destination names must be non-empty and unique within the route.", "pathPrefixError": "The path prefix to remove must begin with '/'.", "routeSaved": "Route saved and activated.", "removeFeatureMessage": "The route will be activated immediately without this feature.", "removeFeatureTitle": "Remove this feature?", "removeActivate": "Remove and activate", "featureSaved": "Feature settings saved and activated.", "deleteRouteMessage": "You can revert this deletion from Activity.", "deleteRouteTitle": "Delete {name}?", "deleteRoute": "Delete route",
    "featureNames": { "authorization": "Authentication", "ip-restrictions": "IP restrictions", "rate-limit": "Rate limiting", "request-size": "Request size", "headers": "Header manipulation", "transforms": "Path and query transforms", "timeout": "Timeout", "resilience": "Retries and circuit breaker", "cors": "CORS", "mirror": "Traffic mirroring", "request-validation": "JSON request validation", "response-cache": "Response caching" },
    "featureCategories": { "security": "Security", "traffic-control": "Traffic control", "transformation": "Transformation", "reliability": "Reliability", "validation": "Validation" },
    "featureDescriptions": { "authorization": "Require an API key or validate a JWT access token.", "ip-restrictions": "Allow or deny client CIDR ranges.", "rate-limit": "Limit requests globally or by client identity.", "request-size": "Reject request bodies above a configured limit.", "headers": "Add, set, or remove request and response headers.", "transforms": "Rewrite paths and query parameters.", "timeout": "Limit total request duration.", "resilience": "Retry safe requests and isolate failing upstreams.", "cors": "Control browser origins, methods, and headers.", "mirror": "Send a bounded copy of selected requests to another upstream.", "request-validation": "Validate JSON request bodies against a schema.", "response-cache": "Cache safe anonymous GET and HEAD responses." },
    "featureHelp": { "authorization": "Require credentials before forwarding a request. API key checks a gateway consumer key. JWT validates an access token against the authority, expected issuer, and one or more audiences.", "rate-limit": "Control how many requests are accepted. Fixed and sliding windows count requests over time, token bucket refills capacity gradually, and concurrency limits simultaneous requests. Limit by chooses which callers share a counter.", "headers": "Set a request header before the upstream receives it, or set a response header before the client receives it. Existing values with the same header name are replaced.", "transforms": "Remove a fixed prefix from the request path before forwarding. For common public-path mapping, the same setting is available under basic Routing as Upstream path handling.", "timeout": "Stop requests that exceed the total duration. Enter an ISO 8601 duration such as PT30S for 30 seconds.", "resilience": "Retry transient failures and temporarily isolate an unhealthy upstream. Retry count controls extra attempts, per-attempt timeout bounds each try, and failure ratio controls when the circuit opens.", "cors": "Control which browser origins may call this route. Origins identify permitted sites, methods and headers restrict the preflight request, and credentials allows cookies or browser authentication.", "ip-restrictions": "Allow or deny clients by CIDR range using the effective client address. Deny rules take priority. Leave the allow list empty to allow addresses that are not explicitly denied.", "request-size": "Reject request bodies larger than the configured byte limit with HTTP 413. The limit also applies when the request uses chunked transfer encoding.", "request-validation": "Validate buffered JSON request bodies against the supplied JSON Schema before forwarding. The body limit bounds memory use and requests that exceed it are rejected.", "response-cache": "Cache safe anonymous GET and HEAD responses in this gateway instance. Lifetime controls freshness, the byte limit prevents large entries, and vary headers create separate entries for selected request-header values.", "mirror": "Send a sampled copy of requests to another route's upstream without changing the primary response. Choose a known route or enter its route ID. Percentage controls how much traffic is copied.", "default": "Configure how this feature behaves for the current route. The change is validated and activated when you save." }
  },
  "sv": {
    "certificateRequiresHosts": "Lägg till minst ett explicit inkommande värdnamn innan ett TLS-certifikat tilldelas.",
    "matchSubpaths": "Matcha sökvägen och alla undersökvägar",
    "matchSubpathsHint": "Lägger automatiskt till routens jokerteckenmönster när den sparas.",
    "preserveOriginalHost": "Behåll inkommande Host-header",
    "preserveOriginalHostHelp": "Skicka det publika värdnamnet till upstreamen. Aktivera detta när upstreamen väljer webbplats efter värdnamn, till exempel en IIS-värdbindning.",
    "feature": "Funktion",
    "configuration": "Konfiguration",
    "status": "Status",
    "actions": "Åtgärder",
    "disableFeatureNamed": "Inaktivera {name}",
    "enableFeatureNamed": "Aktivera {name}",
    "removePrefixSummary": "Ta bort sökvägsprefixet {prefix}",
    "inboundScheme": "Inkommande schema", "inboundCertificate": "TLS-certifikat", "webSocketsAllowed": "Tillåt WebSocket-uppgraderingar", "schemeAny": "HTTP och HTTPS när tillgängligt", "schemeHttp": "Endast HTTP", "schemeHttps": "HTTPS, omdirigera HTTP", "upstreamProtocol": "Upstream-protokoll", "upstreamProtocolHelp": "Styr HTTP-versionen som används mellan gatewayen och upstreamdestinationerna.", "upstreamHttpVersion": "HTTP-version mot uppström", "upstreamVersionPolicy": "Policy för HTTP-version", "multipleHttp2Connections": "Tillåt flera HTTP/2-anslutningar", "multipleHttp2ConnectionsHelp": "Öppnar en ny anslutning när upstreamens aktuella HTTP/2-anslutning saknar lediga requestströmmar. Detta kan öka kapaciteten för hårt belastade routes, men använder fler upstreamanslutningar.", "routeHsts": "HSTS för routens värdnamn", "sharedHostnamePolicy": "Delad värdnamnspolicy", "hstsSharedRoutes": "Samma värdnamnspolicy påverkar {count} annan route i den här miljön. | Samma värdnamnspolicy påverkar {count} andra routes i den här miljön.", "manageHsts": "Hantera HSTS", "notConfigured": "Inte konfigurerad", "notEnabled": "Inte aktiverad", "configurationWarning": "Granska konfigurationen", "hstsRequiresHosts": "Lägg till explicita inkommande värdnamn för att utvärdera och använda HSTS säkert.", "hstsGloballyDisabled": "HSTS är globalt inaktiverat i systeminställningarna.", "hstsNotCovered": "Inget av routens inkommande värdnamn omfattas av den globala HSTS-policyn.", "hstsHttpOnlyWarning": "Den globala HSTS-policyn omfattar värdnamnet, men routen accepterar endast HTTP. Webbläsare som har tagit emot HSTS försöker använda HTTPS i stället.", "hstsCovered": "HSTS skickas i HTTPS-svar för: {hosts}.",
    "route": "Route", "to": "till", "enabled": "Aktiverad", "disabled": "Inaktiverad", "delete": "Ta bort", "routeSummary": "Routesammanfattning", "incomingRequest": "Inkommande anrop", "anyHost": "Valfri värd", "allMethods": "Alla metoder", "upstreamDestinations": "Upstreamdestinationer", "technicalId": "Tekniskt ID", "routeVersion": "Routeversion", "runtimeSummary": "{active} aktiva anrop från {instances} instanser", "loadBalancing": "Lastbalansering", "notOnlineHelp": "Nya anrop använder inte routens normala funktioner eller upstreamdestinationer.", "routing": "Routing", "name": "Namn", "routeEnabled": "Route aktiverad", "incomingPath": "Inkommande sökväg", "upstreamUrl": "Upstream-URL", "incomingHosts": "Inkommande värdar", "incomingHostsHint": "Tryck Retur efter varje värd, till exempel example.com eller *.example.com. Lämna tomt för att acceptera alla värdar.", "pathHandling": "Hantering av upstreamsökväg", "pathPrefix": "Sökvägsprefix att ta bort", "pathPrefixHint": "Prefixet tas bort före vidarebefordran. Frågeparametrar behålls.", "advancedMatching": "Avancerad matchning", "overlapTitle": "När routes överlappar:", "overlapHelp": "Prioritet avgör vilken matchande route som vinner. Lägre tal har högre prioritet. Lämna tomt om den normala matchningsordningen inte behöver åsidosättas.", "precedence": "Prioritet", "precedenceHint": "Lägre tal utvärderas först.", "allowedMethods": "Tillåtna metoder", "allowedMethodsHint": "Avgränsa med kommatecken. Lämna tomt för att matcha alla metoder.", "headerConditions": "Header-villkor", "headerConditionsHelp": "Varje villkor måste matcha det inkommande anropet.", "addHeader": "Lägg till header", "headerName": "Headernamn", "match": "Matchning", "value": "Värde", "caseSensitive": "Skiftlägeskänslig", "removeHeaderCondition": "Ta bort header-villkor {number}", "noHeaderConditions": "Inga header-villkor. Alla requestheaders accepteras.", "queryConditions": "Villkor för frågeparametrar", "queryConditionsHelp": "Varje villkor måste matcha den inkommande frågesträngen.", "addQueryParameter": "Lägg till frågeparameter", "parameterName": "Parameternamn", "removeQueryCondition": "Ta bort frågevillkor {number}", "noQueryConditions": "Inga frågevillkor. Alla frågesträngar accepteras.", "advancedUpstream": "Avancerad upstream", "advancedUpstreamHelp": "Anrop fördelas mellan friska destinationer. Den primära destinationen använder upstream-URL:en ovan.", "additionalDestinations": "Ytterligare destinationer", "additionalDestinationsHelp": "Lägg till en annan instans eller upstream-slutpunkt för denna route.", "addDestination": "Lägg till destination", "destinationName": "Destinationsnamn", "destinationNameHint": "Unikt inom denna route", "destinationUrl": "Destinations-URL", "healthUrl": "Hälso-URL (valfritt)", "pool": "Pool", "removeDestination": "Ta bort destination {number}", "primaryOnly": "Endast primär upstream är konfigurerad.", "saveActivate": "Spara och aktivera", "routeTrafficState": "Routens trafikläge", "trafficStateHelp": "Dränering låter pågående anrop slutföras medan nya avvisas. Underhåll och Offline åsidosätter alla normala routefunktioner och upstreamval.", "trafficState": "Trafikläge", "unavailableResponse": "Otillgänglighetssvar", "unavailableResponseHint": "Ärv miljöns standard eller åsidosätt med ett delat svar konfigurerat i Inställningar.", "cancel": "Avbryt", "gatewayFeatures": "Gatewayfunktioner", "gatewayFeaturesHelp": "Lägg endast till beteendet som denna route behöver.", "addFeature": "Lägg till funktion", "configure": "Konfigurera", "removeFeatureNamed": "Ta bort {name}", "noFeatures": "Inga valfria funktioner", "noFeaturesText": "Routen är aktiv med enkel vidarebefordran. Lägg till säkerhet, trafikkontroll, transformeringar eller feltålighet vid behov.", "addGatewayFeature": "Lägg till en gatewayfunktion", "close": "Stäng", "authenticationType": "Autentiseringstyp", "authorityUrl": "Auktoritets-URL", "expectedIssuer": "Förväntad utfärdare", "audiences": "Målgrupper", "commaSeparated": "Kommaseparerat", "algorithm": "Algoritm", "requestLimit": "Anropsgräns", "window": "Fönster", "durationMinuteHint": "ISO 8601-varaktighet, till exempel PT1M", "limitBy": "Begränsa per", "direction": "Riktning", "removePathPrefix": "Ta bort sökvägsprefix", "totalTimeout": "Total tidsgräns", "durationSecondsHint": "ISO 8601-varaktighet, till exempel PT30S", "retryCount": "Antal återförsök", "attemptTimeout": "Tidsgräns per försök", "failureRatio": "Felandel för kretsbrytare", "allowedOrigins": "Tillåtna ursprung", "allowedHeaders": "Tillåtna headers", "allowCredentials": "Tillåt autentiseringsuppgifter", "allowedCidrs": "Tillåtna CIDR-intervall", "deniedCidrs": "Nekade CIDR-intervall", "onePerLine": "En per rad", "maximumRequestBytes": "Maximalt antal byte i request body", "jsonSchema": "JSON Schema", "maximumValidatedBytes": "Maximalt antal validerade body-byte", "cacheLifetime": "Cachelivslängd", "maximumCachedBytes": "Maximalt antal cachelagrade svarsbyte", "varyHeaders": "Variera efter requestheaders", "mirrorRoute": "Spegla till route", "mirrorRouteHint": "Välj en befintlig route eller ange ett route-ID.", "samplePercentage": "Urvalsprocent", "apiOnlyFeature": "Denna funktion konfigureras via typade avancerade fält i hanterings-API:t.", "preservePath": "Behåll inkommande sökväg", "online": "Online", "draining": "Dränering", "maintenance": "Underhåll", "offline": "Offline", "useEnvironmentDefault": "Använd miljöns standard", "apiKey": "API-nyckel", "jwtAccessToken": "JWT-åtkomsttoken", "requestHeader": "Requestheader", "responseHeader": "Responseheader", "configureFeature": "Konfigurera funktion", "jwtValidation": "Validering av JWT-token", "apiKeyRequired": "API-nyckel krävs", "rateSummary": "{count} anrop, {type}", "cacheSummary": "Cache i {duration}", "configured": "Konfigurerad", "trafficStateSaved": "Routens trafikläge sparades och aktiverades.", "routeDisabled": "Routen inaktiverades och aktiverades.", "routeEnabledSaved": "Routen aktiverades.", "destinationNamesError": "Destinationsnamn måste vara ifyllda och unika inom routen.", "pathPrefixError": "Sökvägsprefixet som ska tas bort måste börja med '/'.", "routeSaved": "Routen sparades och aktiverades.", "removeFeatureMessage": "Routen aktiveras omedelbart utan denna funktion.", "removeFeatureTitle": "Ta bort denna funktion?", "removeActivate": "Ta bort och aktivera", "featureSaved": "Funktionsinställningarna sparades och aktiverades.", "deleteRouteMessage": "Du kan återställa borttagningen från Aktivitet.", "deleteRouteTitle": "Ta bort {name}?", "deleteRoute": "Ta bort route",
    "featureNames": { "authorization": "Autentisering", "ip-restrictions": "IP-begränsningar", "rate-limit": "Hastighetsbegränsning", "request-size": "Requeststorlek", "headers": "Headerhantering", "transforms": "Sökvägs- och frågetransformeringar", "timeout": "Tidsgräns", "resilience": "Återförsök och kretsbrytare", "cors": "CORS", "mirror": "Trafikspegling", "request-validation": "JSON-requestvalidering", "response-cache": "Svarscache" },
    "featureCategories": { "security": "Säkerhet", "traffic-control": "Trafikkontroll", "transformation": "Transformering", "reliability": "Feltålighet", "validation": "Validering" },
    "featureDescriptions": { "authorization": "Kräv en API-nyckel eller validera en JWT-åtkomsttoken.", "ip-restrictions": "Tillåt eller neka klienters CIDR-intervall.", "rate-limit": "Begränsa anrop globalt eller per klientidentitet.", "request-size": "Avvisa request bodies över en konfigurerad gräns.", "headers": "Lägg till, ange eller ta bort request- och responseheaders.", "transforms": "Skriv om sökvägar och frågeparametrar.", "timeout": "Begränsa anropets totala varaktighet.", "resilience": "Försök säkra anrop igen och isolera felande upstreams.", "cors": "Styr webbläsarursprung, metoder och headers.", "mirror": "Skicka en begränsad kopia av valda anrop till en annan upstream.", "request-validation": "Validera JSON-request bodies mot ett schema.", "response-cache": "Cachelagra säkra anonyma GET- och HEAD-svar." },
    "featureHelp": { "authorization": "Kräv autentiseringsuppgifter före vidarebefordran. API-nyckel kontrollerar en konsumentnyckel i gatewayen. JWT validerar en åtkomsttoken mot auktoriteten, förväntad utfärdare och en eller flera målgrupper.", "rate-limit": "Styr hur många anrop som accepteras. Fasta och glidande fönster räknar anrop över tid, token bucket fyller gradvis på kapacitet och samtidighet begränsar parallella anrop. Begränsa per avgör vilka anropare som delar räknare.", "headers": "Ange en requestheader innan upstreamen tar emot anropet eller en responseheader innan klienten tar emot svaret. Befintliga värden med samma headernamn ersätts.", "transforms": "Ta bort ett fast prefix från sökvägen före vidarebefordran. För vanlig mappning av publika sökvägar finns samma inställning under grundläggande Routing som Hantering av upstreamsökväg.", "timeout": "Stoppa anrop som överskrider den totala varaktigheten. Ange en ISO 8601-varaktighet, till exempel PT30S för 30 sekunder.", "resilience": "Försök tillfälliga fel igen och isolera tillfälligt en ohälsosam upstream. Antal återförsök styr extra försök, tidsgräns per försök begränsar varje försök och felandel styr när kretsen öppnas.", "cors": "Styr vilka webbläsarursprung som får anropa routen. Ursprung identifierar tillåtna webbplatser, metoder och headers begränsar preflight-anropet och autentiseringsuppgifter tillåter cookies eller webbläsarautentisering.", "ip-restrictions": "Tillåt eller neka klienter per CIDR-intervall med den effektiva klientadressen. Nekningsregler prioriteras. Lämna tillåtelselistan tom för att tillåta adresser som inte uttryckligen nekas.", "request-size": "Avvisa request bodies över den konfigurerade bytegränsen med HTTP 413. Gränsen gäller även när anropet använder chunked transfer encoding.", "request-validation": "Validera buffrade JSON-request bodies mot angivet JSON Schema före vidarebefordran. Body-gränsen begränsar minnesanvändningen och större anrop avvisas.", "response-cache": "Cachelagra säkra anonyma GET- och HEAD-svar i denna gatewayinstans. Livslängden styr färskhet, bytegränsen förhindrar stora poster och varierande headers skapar separata poster för valda requestheadervärden.", "mirror": "Skicka en samplad kopia av anrop till en annan routes upstream utan att ändra primärsvaret. Välj en känd route eller ange dess route-ID. Procenten styr hur mycket trafik som kopieras.", "default": "Konfigurera hur funktionen beter sig för aktuell route. Ändringen valideras och aktiveras när du sparar." }
  }
}
</i18n>

<i18n lang="json">
{
  "en": { "upstream": "Upstream", "manualUpstream": "Enter a URL directly", "upstreamChoiceHelp": "Select a reusable upstream, or keep this route's direct server configuration.", "namedUpstreamSettings": "Servers, health checks, protocol, and load balancing are managed on the selected Upstream. Route-specific path handling remains configured here." },
  "sv": { "upstream": "Upstream", "manualUpstream": "Ange en URL direkt", "upstreamChoiceHelp": "Välj en återanvändbar upstream eller behåll routens direkta serverkonfiguration.", "namedUpstreamSettings": "Servrar, hälsokontroller, protokoll och lastbalansering hanteras på vald Upstream. Routespecifik sökvägshantering konfigureras fortfarande här." }
}
</i18n>

<style scoped>
.route-header-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.route-header-action {
  min-width: 0;
}

.route-summary {
  padding: 14px 16px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--card);
}

.route-flow {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: center;
  gap: 16px;
}

.route-summary-block {
  min-width: 0;
}

.route-summary-url {
  color: var(--primary);
  text-decoration: none;
}

.route-summary-url:hover,
.route-summary-url:focus-visible {
  text-decoration: underline;
}

.route-summary-url code {
  color: inherit;
}

.route-summary-label {
  color: var(--muted-foreground);
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

.route-summary-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 24px;
  color: var(--muted-foreground);
  font-size: 0.75rem;
}

.route-version {
  overflow-wrap: anywhere;
}

.hsts-route-status {
  border-radius: 8px;
}

.upstream-protocol {
  border-radius: 8px;
}

.feature-name-cell,
.feature-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.gateway-features-heading {
  display: flex;
  align-items: center;
  margin-top: 2rem;
}

.gateway-features-card {
  margin-top: 1rem;
  overflow: hidden;
}
.gateway-features-actions {
  text-align: right;
}

.feature-switch {
  display: flex;
  align-items: center;
  gap: 0.625rem;
}

.feature-actions {
  justify-content: flex-end;
}

.advanced-section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

@media (max-width: 700px) {
  .route-header-actions {
    width: 100%;
  }

  .route-header-action {
    flex: 1 1 128px;
    width: auto;
  }

  .route-flow {
    grid-template-columns: 1fr;
    gap: 10px;
  }

  .route-flow-arrow {
    transform: rotate(90deg);
  }

  .advanced-section-header {
    align-items: stretch;
    flex-direction: column;
  }
}
</style>
