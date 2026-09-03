<template>
  <div class="page-container">
    <header class="flex flex-wrap items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ $t('nav.system') }}
        </p><h1>{{ t('title') }}</h1><p class="page-lead">
          {{ t('lead') }}
        </p>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button variant="outline" @click="openUpload">
          <Upload />{{ t('upload') }}
        </Button><Button :disabled="!accounts.length" @click="openIssue">
          <BadgeCheck />{{ t('issue') }}
        </Button>
      </div>
    </header>
    <Alert v-if="error" variant="destructive" class="mt-4">
      <AlertCircle /><AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Alert v-if="message" class="mt-4 border-emerald-500/40 text-emerald-700 dark:text-emerald-300">
      <CircleCheck /><AlertDescription>{{ message }}</AlertDescription>
    </Alert>

    <Card class="mt-6 gap-0 py-0">
      <CardContent class="flex flex-wrap items-center gap-4 p-4">
        <ShieldCheck class="size-7 text-primary" /><div class="grow">
          <div class="font-bold">
            {{ t('accountsTitle') }}
          </div><div class="text-sm text-muted-foreground">
            {{ accounts.length ? t('accountsReady', { count: accounts.length }) : t('accountMissing') }}
          </div>
        </div><Button variant="outline" :disabled="!availableDirectories.length" @click="openAccount()">
          {{ t('registerAccount') }}
        </Button>
      </CardContent>
      <template v-for="item in accounts" :key="item.id">
        <Separator /><CardContent class="flex flex-wrap items-center gap-4 py-4">
          <div class="grow">
            <div class="flex flex-wrap items-center gap-2">
              <span class="font-bold">{{ item.name }}</span><Badge v-if="item.isDefault" variant="secondary">
                {{ t('defaultAccount') }}
              </Badge><Badge v-if="item.isStaging" variant="warning">
                {{ t('staging') }}
              </Badge>
            </div><div class="text-sm text-muted-foreground">
              {{ item.contactEmail }} · {{ item.directoryUrl }}
            </div>
          </div><div class="flex gap-1">
            <IconButton v-if="!item.isDefault" :label="t('makeDefaultNamed', { name: item.name })" @click="makeDefaultAccount(item)">
              <CircleCheck />
            </IconButton><IconButton :label="t('editNamed', { name: item.name })" @click="openAccount(item)">
              <Pencil />
            </IconButton><IconButton class="text-destructive" :label="t('deleteNamed', { name: item.name })" @click="removeAccount(item)">
              <Trash2 />
            </IconButton>
          </div>
        </CardContent>
      </template>
    </Card>

    <Tabs v-model="tab" class="mt-6">
      <TabsList>
        <TabsTrigger value="certificates">
          <BadgeCheck />{{ t('certificatesTab') }}
        </TabsTrigger><TabsTrigger value="providers">
          <Cloud />{{ t('providersTab') }}
        </TabsTrigger>
      </TabsList>
      <TabsContent value="certificates">
        <Card class="mt-4 overflow-hidden">
          <Progress v-if="loading" class="rounded-none" /><Table>
            <TableHeader>
              <TableRow>
                <TableHead>{{ t('name') }}</TableHead><TableHead>{{ t('hosts') }}</TableHead><TableHead>{{ t('source') }}</TableHead><TableHead>{{ t('expires') }}</TableHead><TableHead>{{ t('status') }}</TableHead><TableHead class="text-right">
                  {{ t('actions') }}
                </TableHead>
              </TableRow>
            </TableHeader><TableBody>
              <TableRow v-for="entry in certificateEntries" :key="entry.key">
                <TableCell>
                  <div class="font-bold">
                    {{ entry.name }}
                  </div><div v-if="entry.thumbprint" class="break-all text-xs text-muted-foreground">
                    {{ entry.thumbprint }}
                  </div>
                </TableCell><TableCell>
                  <div class="flex flex-wrap gap-1">
                    <Badge v-for="host in entry.dnsNames" :key="host" variant="secondary">
                      {{ host }}
                    </Badge>
                  </div>
                </TableCell><TableCell>
                  <Badge variant="outline">
                    {{ entry.managed ? (entry.isStaging ? t('letsEncryptStaging') : t('letsEncrypt')) : t('uploaded') }}
                  </Badge><div v-if="entry.managed" class="mt-1 text-xs text-muted-foreground">
                    {{ entry.accountName }} · {{ challengeLabel(entry.challengeKind) }}
                  </div>
                </TableCell><TableCell>{{ entry.notAfterUtc ? formatDateTime(entry.notAfterUtc) : t('notIssued') }}</TableCell><TableCell>
                  <Badge :variant="statusVariant(entry.status.color)">
                    {{ entry.status.label }}
                  </Badge><div v-if="entry.error" class="mt-1 text-sm text-destructive">
                    {{ entry.error }}
                  </div>
                </TableCell><TableCell>
                  <div class="flex justify-end gap-1">
                    <IconButton :label="t('editNamed', { name: entry.name })" @click="openRename(entry)">
                      <Pencil />
                    </IconButton><IconButton v-if="entry.managed" :label="t('viewDetailsNamed', { name: entry.name })" @click="openDetails(entry)">
                      <Info />
                    </IconButton><IconButton v-if="entry.managed" :disabled="actionId === entry.key" :label="t('renewNamed', { name: entry.name })" @click="renew(entry)">
                      <RefreshCw :class="{ 'animate-spin': actionId === entry.key }" />
                    </IconButton><IconButton class="text-destructive" :label="t('deleteNamed', { name: entry.name })" @click="removeEntry(entry)">
                      <Trash2 />
                    </IconButton>
                  </div>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!loading && !certificateEntries.length" :colspan="6">
                <Empty>
                  <EmptyHeader>
                    <EmptyMedia variant="icon">
                      <BadgeCheck />
                    </EmptyMedia><EmptyTitle>{{ t('emptyTitle') }}</EmptyTitle><EmptyDescription>{{ t('emptyLead') }}</EmptyDescription>
                  </EmptyHeader>
                </Empty>
              </TableEmpty>
            </TableBody>
          </Table>
        </Card>
      </TabsContent>
      <TabsContent value="providers">
        <div class="mt-4 flex justify-end">
          <Button @click="openProfile()">
            <Plus />{{ t('addProvider') }}
          </Button>
        </div><Card class="mt-4 overflow-hidden">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{{ t('name') }}</TableHead><TableHead>{{ t('provider') }}</TableHead><TableHead>{{ t('zones') }}</TableHead><TableHead class="text-right">
                  {{ t('actions') }}
                </TableHead>
              </TableRow>
            </TableHeader><TableBody>
              <TableRow v-for="profile in profiles" :key="profile.id">
                <TableCell class="font-bold">
                  {{ profile.name }}
                </TableCell><TableCell>{{ providerLabel(profile.provider) }}</TableCell><TableCell>
                  <div class="flex flex-wrap gap-1">
                    <Badge v-for="zone in profile.managedZones" :key="zone" variant="secondary">
                      {{ zone }}
                    </Badge>
                  </div>
                </TableCell><TableCell>
                  <div class="flex justify-end gap-1">
                    <IconButton :label="t('testNamed', { name: profile.name })" @click="testProfile(profile)">
                      <RefreshCw />
                    </IconButton><IconButton :label="t('editNamed', { name: profile.name })" @click="openProfile(profile)">
                      <Pencil />
                    </IconButton><IconButton class="text-destructive" :label="t('deleteNamed', { name: profile.name })" @click="removeProfile(profile)">
                      <Trash2 />
                    </IconButton>
                  </div>
                </TableCell>
              </TableRow><TableEmpty v-if="!profiles.length" :colspan="4">
                {{ t('noProviders') }}
              </TableEmpty>
            </TableBody>
          </Table>
        </Card>
      </TabsContent>
    </Tabs>

    <Dialog v-model:open="detailsDialog">
      <DialogContent size="2xl" scrollable>
        <DialogHeader><DialogTitle>{{ t('certificateDetailsTitle', { name: selectedEntry?.name }) }}</DialogTitle></DialogHeader><div data-slot="dialog-body" class="-mx-4 grid gap-4 overflow-x-hidden px-4">
          <Alert v-if="detailsError" variant="destructive">
            <AlertCircle /><AlertDescription>{{ detailsError }}</AlertDescription>
          </Alert><Alert v-if="selectedEntry?.state === 'PENDING'">
            <Info /><AlertDescription>{{ queueGuidance(selectedEntry) }}</AlertDescription>
          </Alert><Alert v-if="selectedEntry?.error" variant="destructive">
            <AlertCircle /><AlertDescription>{{ selectedEntry.error }} {{ failureGuidance(selectedEntry) }}</AlertDescription>
          </Alert><div v-for="challenge in manualChallenges" :key="challenge.id" class="space-y-2 rounded-md border p-3">
            <code class="block break-all">{{ challenge.recordName }}</code><Button variant="outline" size="sm" @click="copyChallengeText(challenge.recordName)">
              <Copy />{{ t('manualDnsName') }}
            </Button><code class="block break-all">{{ challenge.recordValue }}</code><Button variant="outline" size="sm" @click="copyChallengeText(challenge.recordValue)">
              <Copy />{{ t('manualDnsValue') }}
            </Button>
          </div><p v-if="copyMessage" class="text-sm">
            {{ copyMessage }}
          </p><h3 class="font-bold">
            {{ t('activityTitle') }}
          </h3><p v-if="detailsLoading" class="text-sm text-muted-foreground">
            {{ t('activityTitle') }}…
          </p><p v-else-if="!activities.length" class="text-muted-foreground">
            {{ t('noActivity') }}
          </p><Item v-for="activity in activities" :key="activity.id">
            <ItemMedia variant="icon">
              <History />
            </ItemMedia><ItemContent><ItemTitle>{{ activityLabel(activity.action) }}</ItemTitle><ItemDescription>{{ formatDateTime(activity.occurredAtUtc) }}</ItemDescription></ItemContent>
          </Item>
        </div><DialogFooter>
          <Button variant="outline" @click="detailsDialog = false">
            {{ t('close') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="accountDialog">
      <DialogContent>
        <DialogHeader><DialogTitle>{{ editingAccount ? t('updateAccount') : t('registerAccount') }}</DialogTitle><DialogDescription>{{ t('accountInfo') }}</DialogDescription></DialogHeader><Alert v-if="dialogError" variant="destructive">
          <AlertCircle /><AlertDescription>{{ dialogError }}</AlertDescription>
        </Alert><Field v-if="!editingAccount">
          <FieldLabel>{{ t('acmeDirectory') }}</FieldLabel><Select v-model="accountDirectoryUrl">
            <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
              <SelectItem v-for="directory in availableDirectories" :key="directory.directoryUrl" :value="directory.directoryUrl">
                {{ directory.name }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field><Field><FieldLabel>{{ t('contactEmail') }}</FieldLabel><Input v-model="accountEmail" type="email" autocomplete="email" /></Field><div v-if="!editingAccount" class="flex items-center gap-2">
          <Checkbox v-model="termsAccepted" /><label class="text-sm">{{ t('acceptTerms') }}</label>
        </div><DialogFooter>
          <Button variant="outline" :disabled="saving" @click="accountDialog = false">
            {{ t('cancel') }}
          </Button><Button :disabled="saving || !accountEmail.trim() || (!editingAccount && (!accountDirectoryUrl || !termsAccepted))" @click="saveAccount">
            <Spinner v-if="saving" />{{ t('save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="issueDialog">
      <DialogContent>
        <DialogHeader><DialogTitle>{{ t('issueTitle') }}</DialogTitle><DialogDescription>{{ issueChallenge === 'HTTP01' ? t('httpInfo') : issueChallenge === 'MANUAL_DNS01' ? t('manualDnsInfo') : t('dnsInfo') }}</DialogDescription></DialogHeader><Alert v-if="dialogError" variant="destructive">
          <AlertCircle /><AlertDescription>{{ dialogError }}</AlertDescription>
        </Alert><Field>
          <FieldLabel>{{ t('acmeAccount') }}</FieldLabel><Select v-model="issueAccountId">
            <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
              <SelectItem v-for="account in accounts" :key="account.id" :value="account.id">
                {{ account.name }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field><Field><FieldLabel>{{ t('name') }}</FieldLabel><Input v-model="issueName" /></Field><Field><FieldLabel>{{ t('hosts') }}</FieldLabel><Textarea :model-value="issueHosts.join('\n')" @update:model-value="setIssueHosts" /><FieldDescription>{{ t('hostsHint') }}</FieldDescription></Field><Field>
          <FieldLabel>{{ t('challenge') }}</FieldLabel><Select v-model="issueChallenge">
            <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
              <SelectItem v-for="option in challengeOptions" :key="option.value" :value="option.value">
                {{ option.title }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field><Field v-if="issueChallenge === 'DNS01'">
          <FieldLabel>{{ t('dnsProfile') }}</FieldLabel><Select v-model="issueProfileId">
            <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
              <SelectItem v-for="profile in profiles" :key="profile.id" :value="profile.id">
                {{ profile.name }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field><DialogFooter>
          <Button variant="outline" @click="issueDialog = false">
            {{ t('cancel') }}
          </Button><Button :disabled="saving || !canIssue" @click="issueCertificate">
            <Spinner v-if="saving" />{{ t('issue') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="profileDialog">
      <DialogContent scrollable>
        <DialogHeader><DialogTitle>{{ editingProfile ? t('editProvider') : t('addProvider') }}</DialogTitle><DialogDescription>{{ editingProfile ? t('credentialOptional') : t('credentialInfo') }}</DialogDescription></DialogHeader><div data-slot="dialog-body" class="-mx-4 grid gap-4 overflow-x-hidden px-4">
          <Alert v-if="dialogError" variant="destructive">
            <AlertCircle /><AlertDescription>{{ dialogError }}</AlertDescription>
          </Alert><Field><FieldLabel>{{ t('name') }}</FieldLabel><Input v-model="profileName" /></Field><Field>
            <FieldLabel>{{ t('provider') }}</FieldLabel><Select v-model="profileProvider" :disabled="!!editingProfile">
              <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                <SelectItem v-for="option in providerOptions" :key="option.value" :value="option.value">
                  {{ option.title }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field><Field v-for="field in credentialFields" :key="field.key">
            <FieldLabel>{{ t(field.label) }}</FieldLabel><Textarea v-if="field.textarea" v-model="credentials[field.key]" /><Input v-else v-model="credentials[field.key]" :type="field.secret ? 'password' : 'text'" autocomplete="off" />
          </Field>
        </div><DialogFooter>
          <Button variant="outline" @click="profileDialog = false">
            {{ t('cancel') }}
          </Button><Button :disabled="saving || !profileName.trim()" @click="saveProfile">
            <Spinner v-if="saving" />{{ t('saveAndTest') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="renameDialog">
      <DialogContent>
        <DialogHeader><DialogTitle>{{ t('editCertificate') }}</DialogTitle></DialogHeader><Alert v-if="dialogError" variant="destructive">
          <AlertCircle /><AlertDescription>{{ dialogError }}</AlertDescription>
        </Alert><Field><FieldLabel>{{ t('name') }}</FieldLabel><Input v-model="renameName" maxlength="200" autofocus @keydown.enter="saveRename" /></Field><DialogFooter>
          <Button variant="outline" @click="renameDialog = false">
            {{ t('cancel') }}
          </Button><Button :disabled="saving || !renameName.trim()" @click="saveRename">
            <Spinner v-if="saving" />{{ t('save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="uploadDialog">
      <DialogContent>
        <DialogHeader><DialogTitle>{{ t('uploadTitle') }}</DialogTitle><DialogDescription>{{ t('uploadInfo') }}</DialogDescription></DialogHeader><Alert v-if="dialogError" variant="destructive">
          <AlertCircle /><AlertDescription>{{ dialogError }}</AlertDescription>
        </Alert><Field><FieldLabel>{{ t('name') }}</FieldLabel><Input v-model="uploadName" /></Field><Field><FieldLabel>{{ t('file') }}</FieldLabel><Input type="file" accept=".pfx,.p12" @change="selectUploadFile" /><FieldDescription>{{ t('fileHint') }}</FieldDescription></Field><Field><FieldLabel>{{ t('password') }}</FieldLabel><Input v-model="uploadPassword" type="password" autocomplete="current-password" /><FieldDescription>{{ t('passwordHint') }}</FieldDescription></Field><DialogFooter>
          <Button variant="outline" @click="uploadDialog = false">
            {{ t('cancel') }}
          </Button><Button :disabled="saving || !uploadName.trim() || !uploadFile" @click="upload">
            <Spinner v-if="saving" />{{ t('upload') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Badge, Button, Card, CardContent, Checkbox, Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle, Field, FieldDescription, FieldLabel, Input, Item, ItemContent, ItemDescription, ItemMedia, ItemTitle, Progress, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Separator, Spinner, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow, Tabs, TabsContent, TabsList, TabsTrigger, Textarea } from '@aditify/ui';
import { AlertCircle, BadgeCheck, CircleCheck, Cloud, Copy, History, Info, Pencil, Plus, RefreshCw, ShieldCheck, Trash2, Upload } from '@lucide/vue';
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql } from '../api';
import IconButton from '../components/IconButton.vue';
import { confirmAction } from '../composables/confirmDialog';
import { formatDateTime } from '../utils/dateTime';

interface Cert { id: string; name: string; thumbprint: string; dnsNames: string[]; notAfterUtc: string; version: string }
interface Account { id: string; name: string; directoryUrl: string; isStaging: boolean; isDefault: boolean; contactEmail: string; version: string }
interface Directory { name: string; directoryUrl: string; isStaging: boolean; termsOfServiceUrl?: string; registered: boolean }
interface Profile { id: string; name: string; provider: string; managedZones: string[]; version: string }
interface Managed { id: string; name: string; dnsNames: string[]; acmeAccountId: string; acmeAccountName: string; isStaging: boolean; challengeKind: string; state: string; lastErrorCode?: string; lastErrorMessage?: string; lastAttemptAtUtc?: string; nextAttemptAtUtc: string; version: string; certificate?: Cert }
interface Entry { key: string; id: string; name: string; dnsNames: string[]; thumbprint?: string; notAfterUtc?: string; managed: boolean; accountName?: string; isStaging?: boolean; challengeKind?: string; state?: string; errorCode?: string; error?: string; lastAttemptAtUtc?: string; nextAttemptAtUtc?: string; version?: string; status: { color: string; icon: string; label: string } }
interface Activity { id: number; action: string; occurredAtUtc: string }
interface ManualChallenge { id: string; recordName: string; recordValue: string; expiresAtUtc: string }

const { t } = useI18n(); const tab = ref('certificates'); const loading = ref(false); const saving = ref(false); const actionId = ref(''); const error = ref(''); const dialogError = ref(''); const message = ref(''); const certificates = ref<Cert[]>([]); const managed = ref<Managed[]>([]); const profiles = ref<Profile[]>([]); const accounts = ref<Account[]>([]); const directories = ref<Directory[]>([]); let timer: ReturnType<typeof setInterval> | undefined;
const detailsDialog = ref(false); const detailsLoading = ref(false); const detailsError = ref(''); const selectedEntry = ref<Entry>(); const activities = ref<Activity[]>([]); const manualChallenges = ref<ManualChallenge[]>([]); const copyMessage = ref(''); const accountDialog = ref(false); const editingAccount = ref<Account>(); const accountDirectoryUrl = ref(''); const accountEmail = ref(''); const termsAccepted = ref(false); const issueDialog = ref(false); const issueAccountId = ref<string>(); const issueName = ref(''); const issueHosts = ref<string[]>([]); const issueChallenge = ref('HTTP01'); const issueProfileId = ref<string>(); const profileDialog = ref(false); const editingProfile = ref<Profile>(); const profileName = ref(''); const profileProvider = ref('CLOUDFLARE'); const credentials = ref<Record<string, string>>({}); const renameDialog = ref(false); const renamingEntry = ref<Entry>(); const renameName = ref(''); const uploadDialog = ref(false); const uploadName = ref(''); const uploadFile = ref<File>(); const uploadPassword = ref('');
const challengeOptions = computed(() => [{ title: t('http01'), value: 'HTTP01' }, { title: t('dns01'), value: 'DNS01' }, { title: t('manualDns01'), value: 'MANUAL_DNS01' }]); const providerOptions = computed(() => [{ title: 'Cloudflare', value: 'CLOUDFLARE' }, { title: 'Amazon Route 53', value: 'ROUTE53' }, { title: 'Azure DNS', value: 'AZURE_DNS' }, { title: 'Google Cloud DNS', value: 'GOOGLE_CLOUD_DNS' }, { title: 'DigitalOcean', value: 'DIGITAL_OCEAN' }, { title: 'Loopia', value: 'LOOPIA' }, { title: 'Simply.com', value: 'SIMPLY' }]); const canIssue = computed(() => !!(issueAccountId.value && issueName.value.trim() && issueHosts.value.length && (issueChallenge.value !== 'DNS01' || issueProfileId.value)));
const availableDirectories = computed(() => directories.value.filter(x => !x.registered)); const selectedDirectory = computed(() => directories.value.find(x => x.directoryUrl === accountDirectoryUrl.value)); const selectedIssueAccount = computed(() => accounts.value.find(x => x.id === issueAccountId.value));

const credentialFields = computed<Array<{ key: string; label: string; secret?: boolean; textarea?: boolean }>>(() => {
  if (['CLOUDFLARE', 'DIGITAL_OCEAN', 'SIMPLY'].includes(profileProvider.value))
    return [{ key: 'apiToken', label: 'apiToken', secret: true }];

  if (profileProvider.value === 'ROUTE53')
    return [{ key: 'accessKeyId', label: 'accessKeyId' }, { key: 'secretAccessKey', label: 'secretAccessKey', secret: true }, { key: 'sessionToken', label: 'sessionToken', secret: true }];

  if (profileProvider.value === 'AZURE_DNS')
    return [{ key: 'tenantId', label: 'tenantId' }, { key: 'clientId', label: 'clientId' }, { key: 'clientSecret', label: 'clientSecret', secret: true }, { key: 'subscriptionId', label: 'subscriptionId' }, { key: 'resourceGroup', label: 'resourceGroup' }];

  if (profileProvider.value === 'GOOGLE_CLOUD_DNS')
    return [{ key: 'projectId', label: 'projectId' }, { key: 'serviceAccountJson', label: 'serviceAccountJson', textarea: true }];

  if (profileProvider.value === 'LOOPIA')
    return [{ key: 'username', label: 'username' }, { key: 'password', label: 'password', secret: true }, { key: 'customerNumber', label: 'customerNumber' }];

  return [];
});

function statusVariant(color: string): 'destructive' | 'warning' | 'success' | 'info' { return color === 'error' ? 'destructive' : color === 'warning' ? 'warning' : color === 'success' ? 'success' : 'info'; }
function setIssueHosts(value: string | number) { issueHosts.value = String(value).split(/[\n,]/).map(x => x.trim()).filter(Boolean); }
function selectUploadFile(event: Event) { uploadFile.value = (event.target as HTMLInputElement).files?.[0]; }

function certificateStatus(date: string) {
  const days = Math.ceil((new Date(date).getTime() - Date.now()) / 86_400_000);

  if (days <= 0)
    return { color: 'error', icon: 'alert-circle-outline', label: t('expired') };

  if (days <= 30)
    return { color: 'warning', icon: 'clock-alert-outline', label: t('expiresSoon', { days }) };

  return { color: 'success', icon: 'check-circle-outline', label: t('valid') };
}
function managedStatus(value: Managed) {
  if (value.state === 'FAILED')
    return { color: 'error', icon: 'alert-circle-outline', label: t('failed') };

  if (value.challengeKind === 'MANUAL_DNS01' && ['ISSUING', 'RENEWING'].includes(value.state))
    return { color: 'warning', icon: 'dns-outline', label: t('manualDnsPending') };

  if (value.state === 'ISSUING')
    return { color: 'info', icon: 'progress-clock', label: t('issuing') };

  if (value.state === 'RENEWING')
    return { color: 'info', icon: 'refresh', label: t('renewing') };

  return value.certificate ? certificateStatus(value.certificate.notAfterUtc) : { color: 'info', icon: 'clock-outline', label: t('pending') };
}

const certificateEntries = computed<Entry[]>(() => {
  const ids = new Set(managed.value.map(x => x.certificate?.id).filter(Boolean)); const values: Entry[] = managed.value.map(x => ({ key: `managed-${x.id}`, id: x.id, name: x.name, dnsNames: x.dnsNames, thumbprint: x.certificate?.thumbprint, notAfterUtc: x.certificate?.notAfterUtc, managed: true, accountName: x.acmeAccountName, isStaging: x.isStaging, challengeKind: x.challengeKind, state: x.state, errorCode: x.lastErrorCode, error: x.lastErrorMessage, lastAttemptAtUtc: x.lastAttemptAtUtc, nextAttemptAtUtc: x.nextAttemptAtUtc, version: x.version, status: managedStatus(x) }));

  values.push(...certificates.value.filter(x => !ids.has(x.id)).map(x => ({ key: `uploaded-${x.id}`, id: x.id, name: x.name, dnsNames: x.dnsNames, thumbprint: x.thumbprint, notAfterUtc: x.notAfterUtc, managed: false, version: x.version, status: certificateStatus(x.notAfterUtc) }))); return values;
});

function providerLabel(value: string) { return providerOptions.value.find(x => x.value === value)?.title || value; } function challengeLabel(value?: string) { return value === 'DNS01' ? t('dns01') : value === 'MANUAL_DNS01' ? t('manualDns01') : t('http01'); }
function failureGuidance(entry: Entry) { return entry.errorCode === 'ACME_TIMEOUT' && ['DNS01', 'MANUAL_DNS01'].includes(entry.challengeKind || '') ? t('dnsTimeoutGuidance') : t('failureGuidance'); }
function queueGuidance(entry: Entry) {
  const overdue = entry.nextAttemptAtUtc && new Date(entry.nextAttemptAtUtc).getTime() < Date.now() - 120_000;

  return overdue ? t('queueOverdueGuidance') : t('queueGuidance');
}
function activityLabel(action: string) {
  const key = `activity${action.replace(/^ManagedCertificate/, '')}`;

  return t(key);
}
function activityIcon(action: string) {
  if (action.endsWith('Failed'))
    return 'alert-circle-outline';

  if (action.endsWith('Issued') || action.endsWith('Renewed') || action.endsWith('Completed'))
    return 'check-circle-outline';

  return 'history';
}
async function openDetails(entry: Entry) {
  selectedEntry.value = entry; activities.value = []; manualChallenges.value = []; copyMessage.value = ''; detailsError.value = ''; detailsDialog.value = true;
  await loadActivities(true);
}
async function loadActivities(showLoading = false) {
  if (!selectedEntry.value)
    return;

  if (showLoading)
    detailsLoading.value = true;

  try {
    const data = await graphql<{ managedCertificateActivity: Activity[]; managedCertificateDnsChallenges: ManualChallenge[] }>(`query($id:UUID!){managedCertificateActivity(managedCertificateId:$id,take:100){id action occurredAtUtc} managedCertificateDnsChallenges(managedCertificateId:$id){id recordName recordValue expiresAtUtc}}`, { id: selectedEntry.value.id });

    activities.value = [...data.managedCertificateActivity].reverse(); manualChallenges.value = data.managedCertificateDnsChallenges ?? []; detailsError.value = '';
  }
  catch (e) { detailsError.value = e instanceof Error ? e.message : String(e); }
  finally {
    if (showLoading)
      detailsLoading.value = false;
  }
}
async function copyChallengeText(value: string) {
  try {
    await navigator.clipboard.writeText(value); copyMessage.value = t('manualDnsCopied');
  }
  catch { detailsError.value = t('manualDnsCopyFailed'); }
}
async function run(f: () => Promise<void>) {
  error.value = ''; message.value = '';

  try { await f(); }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}
async function runDialog(f: () => Promise<void>) {
  error.value = ''; dialogError.value = ''; message.value = '';

  try { await f(); }
  catch (e) { dialogError.value = e instanceof Error ? e.message : String(e); }
}
async function load() {
  loading.value = true;

  try {
    const data = await graphql<{ inboundCertificates: Cert[]; managedCertificates: Managed[]; dnsProviderProfiles: Profile[]; acmeAccounts: Account[]; acmeDirectories: Directory[] }>(`query CertificateManagement{inboundCertificates{id name thumbprint dnsNames notAfterUtc version} managedCertificates{id name dnsNames acmeAccountId acmeAccountName isStaging challengeKind state lastErrorCode lastErrorMessage lastAttemptAtUtc nextAttemptAtUtc version certificate{id name thumbprint dnsNames notAfterUtc version}} dnsProviderProfiles{id name provider managedZones version} acmeAccounts{id name directoryUrl isStaging isDefault contactEmail version} acmeDirectories{name directoryUrl isStaging termsOfServiceUrl registered}}`);

    certificates.value = data.inboundCertificates; managed.value = data.managedCertificates; profiles.value = data.dnsProviderProfiles; accounts.value = data.acmeAccounts; directories.value = data.acmeDirectories;

    if (selectedEntry.value)
      selectedEntry.value = certificateEntries.value.find(x => x.key === selectedEntry.value?.key) || selectedEntry.value;
  }
  finally { loading.value = false; }
}
function openAccount(account?: Account) { dialogError.value = ''; editingAccount.value = account; accountDirectoryUrl.value = account?.directoryUrl || availableDirectories.value[0]?.directoryUrl || ''; accountEmail.value = account?.contactEmail || ''; termsAccepted.value = false; accountDialog.value = true; } async function saveAccount() {
  saving.value = true; await runDialog(async () => {
    if (editingAccount.value)
      await graphql(`mutation($id:UUID!,$v:UUID!,$e:String!){updateAcmeAccount(id:$id,expectedVersion:$v,contactEmail:$e){id}}`, { id: editingAccount.value.id, v: editingAccount.value.version, e: accountEmail.value }); else await graphql(`mutation($d:String!,$e:String!,$a:Boolean!){registerAcmeAccount(directoryUrl:$d,contactEmail:$e,termsAccepted:$a){id}}`, { d: accountDirectoryUrl.value, e: accountEmail.value, a: termsAccepted.value });

    accountDialog.value = false; await load(); message.value = t('accountSaved');
  }); saving.value = false;
}
async function makeDefaultAccount(account: Account) { await run(async () => { await graphql(`mutation($id:UUID!,$v:UUID!){setDefaultAcmeAccount(id:$id,expectedVersion:$v){id}}`, { id: account.id, v: account.version }); await load(); message.value = t('defaultAccountSaved'); }); }
async function removeAccount(account: Account) {
  if (!await confirmAction(t('deleteAccountConfirm', { name: account.name }), { title: t('deleteAccountTitle'), confirmText: t('delete'), color: 'error' }))
    return;

  await run(async () => { await graphql(`mutation($id:UUID!){deleteAcmeAccount(id:$id)}`, { id: account.id }); await load(); });
}
function openIssue() { dialogError.value = ''; issueAccountId.value = accounts.value.find(x => x.isDefault)?.id || accounts.value[0]?.id; issueName.value = ''; issueHosts.value = []; issueChallenge.value = 'HTTP01'; issueProfileId.value = undefined; issueDialog.value = true; } async function issueCertificate() { saving.value = true; await runDialog(async () => { await graphql(`mutation($n:String!,$h:[String!]!,$c:AcmeChallengeKind!,$p:UUID,$a:UUID){issueAcmeCertificate(name:$n,dnsNames:$h,challengeKind:$c,dnsProviderProfileId:$p,acmeAccountId:$a){id}}`, { n: issueName.value, h: issueHosts.value, c: issueChallenge.value, p: issueChallenge.value === 'DNS01' ? issueProfileId.value : null, a: issueAccountId.value }); issueDialog.value = false; await load(); message.value = t('issueQueued'); }); saving.value = false; }
async function renew(entry: Entry) { actionId.value = entry.key; await run(async () => { await graphql(`mutation($id:UUID!,$v:UUID!){renewAcmeCertificate(id:$id,expectedVersion:$v){id}}`, { id: entry.id, v: entry.version }); await load(); message.value = t('renewQueued'); }); actionId.value = ''; } async function removeEntry(entry: Entry) {
  if (!await confirmAction(t('deleteConfirm', { name: entry.name }), { title: t('deleteTitle'), confirmText: t('delete'), color: 'error' }))
    return;

  actionId.value = entry.key; await run(async () => { await graphql(entry.managed ? `mutation($id:UUID!){deleteManagedCertificate(id:$id)}` : `mutation($id:UUID!){deleteInboundCertificate(id:$id)}`, { id: entry.id }); await load(); }); actionId.value = '';
}
function openRename(entry: Entry) { dialogError.value = ''; renamingEntry.value = entry; renameName.value = entry.name; renameDialog.value = true; }
async function saveRename() {
  const entry = renamingEntry.value;

  if (!entry?.version || !renameName.value.trim() || saving.value)
    return;

  saving.value = true; await runDialog(async () => {
    const mutation = entry.managed
      ? `mutation($id:UUID!,$v:UUID!,$n:String!){renameManagedCertificate(id:$id,expectedVersion:$v,name:$n){id}}`
      : `mutation($id:UUID!,$v:UUID!,$n:String!){renameInboundCertificate(id:$id,expectedVersion:$v,name:$n){id}}`;

    await graphql(mutation, { id: entry.id, v: entry.version, n: renameName.value }); renameDialog.value = false; await load(); message.value = t('certificateRenamed');
  }); saving.value = false;
}
function blankCredentials() { return { apiToken: '', accessKeyId: '', secretAccessKey: '', sessionToken: '', tenantId: '', clientId: '', clientSecret: '', subscriptionId: '', resourceGroup: '', projectId: '', serviceAccountJson: '', username: '', password: '', customerNumber: '' }; } function openProfile(profile?: Profile) { dialogError.value = ''; editingProfile.value = profile; profileName.value = profile?.name || ''; profileProvider.value = profile?.provider || 'CLOUDFLARE'; credentials.value = blankCredentials(); profileDialog.value = true; } function credentialPayload() { return Object.fromEntries(Object.entries(credentials.value).map(([key, value]) => [key, value.trim() || null])); } function hasCredentials() { return Object.values(credentials.value).some(x => x.trim()); }
async function saveProfile() {
  saving.value = true; await runDialog(async () => {
    if (editingProfile.value)
      await graphql(`mutation($id:UUID!,$v:UUID!,$n:String!,$c:DnsProviderCredentialsInput){updateDnsProviderProfile(id:$id,expectedVersion:$v,name:$n,credentials:$c){id}}`, { id: editingProfile.value.id, v: editingProfile.value.version, n: profileName.value, c: hasCredentials() ? credentialPayload() : null }); else await graphql(`mutation($n:String!,$p:DnsProviderKind!,$c:DnsProviderCredentialsInput!){createDnsProviderProfile(name:$n,provider:$p,credentials:$c){id}}`, { n: profileName.value, p: profileProvider.value, c: credentialPayload() });

    profileDialog.value = false; await load(); message.value = t('profileSaved');
  }); saving.value = false;
} async function testProfile(profile: Profile) {
  await run(async () => {
    const data = await graphql<{ testDnsProviderProfile: Array<{ name: string }> }>(`mutation($id:UUID!){testDnsProviderProfile(id:$id){name}}`, { id: profile.id });

    message.value = t('profileTested', { count: data.testDnsProviderProfile.length });
  });
} async function removeProfile(profile: Profile) {
  if (!await confirmAction(t('deleteProfileConfirm', { name: profile.name }), { title: t('deleteProfileTitle'), confirmText: t('delete'), color: 'error' }))
    return;

  await run(async () => { await graphql(`mutation($id:UUID!){deleteDnsProviderProfile(id:$id)}`, { id: profile.id }); await load(); });
}
function openUpload() { dialogError.value = ''; uploadName.value = ''; uploadFile.value = undefined; uploadPassword.value = ''; uploadDialog.value = true; } async function upload() {
  saving.value = true; await runDialog(async () => {
    if (!uploadFile.value)
      throw new Error(t('fileRequired'));

    const bytes = new Uint8Array(await uploadFile.value.arrayBuffer()); let binary = '';

    for (const byte of bytes) binary += String.fromCharCode(byte);

    await graphql(`mutation($n:String!,$b:String!,$p:String){uploadInboundCertificate(name:$n,pkcs12Base64:$b,password:$p){id}}`, { n: uploadName.value, b: btoa(binary), p: uploadPassword.value || null }); uploadDialog.value = false; await load();
  }); saving.value = false;
}
onMounted(() => {
  run(load); timer = setInterval(() => {
    if (managed.value.some(x => ['PENDING', 'ISSUING', 'RENEWING'].includes(x.state)))
      run(load);

    if (detailsDialog.value && selectedEntry.value?.managed)
      loadActivities();
  }, 5000);
}); onBeforeUnmount(() => {
  if (timer)
    clearInterval(timer);
});
</script>

<i18n lang="json">
{"en":{"title":"Inbound certificates","lead":"Manage uploaded and automatically renewed certificates used for TLS termination.","certificatesTab":"Certificates","providersTab":"DNS providers","upload":"Upload certificate","issue":"Issue Let's Encrypt certificate","name":"Name","hosts":"Hostnames","source":"Source","expires":"Expires","status":"Status","actions":"Actions","letsEncrypt":"Let's Encrypt","letsEncryptStaging":"Let's Encrypt staging","uploaded":"Uploaded","autoRenew":"Automatic renewal","notIssued":"Not issued yet","nextAttempt":"Next attempt: {date}","renewNamed":"Renew {name}","deleteNamed":"Delete {name}","testNamed":"Test {name}","editNamed":"Edit {name}","emptyTitle":"No certificates","emptyLead":"Upload a PKCS#12 certificate or issue one through Let's Encrypt.","valid":"Valid","expired":"Expired","expiresSoon":"Expires in {days} days","failed":"Action required","issuing":"Issuing","renewing":"Renewing","pending":"Pending","accountTitle":"Let's Encrypt account","accountsTitle":"Let's Encrypt accounts","accountsReady":"{count} account configured | {count} accounts configured","defaultAccount":"Default","staging":"Staging","makeDefaultNamed":"Make {name} the default account","accountReady":"Ready for automatic issuance","accountMissing":"Register an account before issuing certificates.","registerAccount":"Register account","updateAccount":"Update contact","accountInfo":"Each ACME directory uses a separate account and protected account key.","termsLink":"Read the current terms of service.","acmeDirectory":"ACME directory","acmeAccount":"ACME account","contactEmail":"Contact email","acceptTerms":"I accept the current Let's Encrypt terms of service","accountSaved":"Let's Encrypt account saved.","defaultAccountSaved":"Default ACME account updated.","deleteAccountTitle":"Delete ACME account?","deleteAccountConfirm":"Delete {name}? Accounts used by managed certificates cannot be deleted.","stagingWarning":"Staging certificates are not trusted by browsers and should be used only for testing.","issueTitle":"Issue Let's Encrypt certificate","httpInfo":"HTTP-01 requires this gateway to be publicly reachable over port 80 for every hostname.","dnsInfo":"DNS-01 creates temporary TXT records and is required for wildcard certificates.","hostsHint":"Enter one to 100 DNS names. Wildcards must start with *.","challenge":"Validation method","http01":"HTTP-01","dns01":"DNS-01","dnsProfile":"DNS provider profile","issueQueued":"Certificate issuance queued.","renewQueued":"Certificate renewal queued.","addProvider":"Add DNS provider","editProvider":"Edit DNS provider","provider":"Provider","zones":"Managed zones","noProviders":"No DNS provider profiles configured.","credentialInfo":"Credentials are encrypted before storage and tested by listing manageable zones.","credentialOptional":"Leave credential fields empty to keep the current secret, or enter replacement credentials to rotate it.","apiToken":"API token","accessKeyId":"Access key ID","secretAccessKey":"Secret access key","sessionToken":"Session token (optional)","tenantId":"Tenant ID","clientId":"Client ID","clientSecret":"Client secret","subscriptionId":"Subscription ID","resourceGroup":"Resource group","projectId":"Project ID","serviceAccountJson":"Service-account JSON","username":"API username","password":"Password","customerNumber":"Reseller customer number (optional)","saveAndTest":"Save and test","profileSaved":"DNS provider profile saved.","profileTested":"Connection succeeded. {count} zones are available.","deleteProfileTitle":"Delete DNS provider?","deleteProfileConfirm":"Delete {name}? Profiles used by managed certificates cannot be deleted.","delete":"Delete","deleteTitle":"Delete certificate?","deleteConfirm":"Delete {name}? Certificates assigned to active routes cannot be deleted.","uploadTitle":"Upload PKCS#12 certificate","uploadInfo":"The import password is used only while validating the certificate. Certificate material is encrypted before storage.","file":"PFX or P12 file","fileHint":"Maximum file size: 5 MiB.","passwordHint":"Leave empty only when the file has no password.","cancel":"Cancel","save":"Save","close":"Close","fileRequired":"Select a certificate file."},"sv":{"title":"Inkommande certifikat","lead":"Hantera uppladdade och automatiskt förnyade certifikat för TLS-terminering.","certificatesTab":"Certifikat","providersTab":"DNS-leverantörer","upload":"Ladda upp certifikat","issue":"Utfärda Let's Encrypt-certifikat","name":"Namn","hosts":"Värdnamn","source":"Källa","expires":"Upphör","status":"Status","actions":"Åtgärder","letsEncrypt":"Let's Encrypt","letsEncryptStaging":"Let's Encrypt staging","uploaded":"Uppladdat","autoRenew":"Automatisk förnyelse","notIssued":"Inte utfärdat ännu","nextAttempt":"Nästa försök: {date}","renewNamed":"Förnya {name}","deleteNamed":"Ta bort {name}","testNamed":"Testa {name}","editNamed":"Redigera {name}","emptyTitle":"Inga certifikat","emptyLead":"Ladda upp ett PKCS#12-certifikat eller utfärda ett via Let's Encrypt.","valid":"Giltigt","expired":"Utgånget","expiresSoon":"Upphör om {days} dagar","failed":"Åtgärd krävs","issuing":"Utfärdar","renewing":"Förnyar","pending":"Väntar","accountTitle":"Let's Encrypt-konto","accountsTitle":"Let's Encrypt-konton","accountsReady":"{count} konto konfigurerat | {count} konton konfigurerade","defaultAccount":"Standard","staging":"Testmiljö","makeDefaultNamed":"Gör {name} till standardkonto","accountReady":"Redo för automatisk utfärdning","accountMissing":"Registrera ett konto innan certifikat utfärdas.","registerAccount":"Registrera konto","updateAccount":"Uppdatera kontakt","accountInfo":"Varje ACME-katalog använder ett separat konto och en skyddad kontonyckel.","termsLink":"Läs de aktuella användarvillkoren.","acmeDirectory":"ACME-katalog","acmeAccount":"ACME-konto","contactEmail":"Kontaktadress","acceptTerms":"Jag godkänner de aktuella användarvillkoren för Let's Encrypt","accountSaved":"Let's Encrypt-kontot har sparats.","defaultAccountSaved":"ACME-standardkontot har uppdaterats.","deleteAccountTitle":"Ta bort ACME-konto?","deleteAccountConfirm":"Ta bort {name}? Konton som används av hanterade certifikat kan inte tas bort.","stagingWarning":"Certifikat från testmiljön är inte betrodda av webbläsare och ska endast användas för testning.","issueTitle":"Utfärda Let's Encrypt-certifikat","httpInfo":"HTTP-01 kräver att gatewayen kan nås offentligt på port 80 för varje värdnamn.","dnsInfo":"DNS-01 skapar tillfälliga TXT-poster och krävs för wildcard-certifikat.","hostsHint":"Ange ett till 100 DNS-namn. Wildcard måste börja med *.","challenge":"Valideringsmetod","http01":"HTTP-01","dns01":"DNS-01","dnsProfile":"DNS-leverantörsprofil","issueQueued":"Certifikatsutfärdningen har köats.","renewQueued":"Certifikatsförnyelsen har köats.","addProvider":"Lägg till DNS-leverantör","editProvider":"Redigera DNS-leverantör","provider":"Leverantör","zones":"Hanterade zoner","noProviders":"Inga DNS-leverantörsprofiler har konfigurerats.","credentialInfo":"Autentiseringsuppgifter krypteras före lagring och testas genom att tillgängliga zoner hämtas.","credentialOptional":"Lämna uppgiftsfälten tomma för att behålla nuvarande hemlighet eller ange nya uppgifter för att rotera den.","apiToken":"API-token","accessKeyId":"Åtkomstnyckel-ID","secretAccessKey":"Hemlig åtkomstnyckel","sessionToken":"Sessionstoken (valfri)","tenantId":"Klientorganisations-ID","clientId":"Klient-ID","clientSecret":"Klienthemlighet","subscriptionId":"Prenumerations-ID","resourceGroup":"Resursgrupp","projectId":"Projekt-ID","serviceAccountJson":"JSON för tjänstekonto","username":"API-användarnamn","password":"Lösenord","customerNumber":"Återförsäljarens kundnummer (valfritt)","saveAndTest":"Spara och testa","profileSaved":"DNS-leverantörsprofilen har sparats.","profileTested":"Anslutningen lyckades. {count} zoner är tillgängliga.","deleteProfileTitle":"Ta bort DNS-leverantör?","deleteProfileConfirm":"Ta bort {name}? Profiler som används av hanterade certifikat kan inte tas bort.","delete":"Ta bort","deleteTitle":"Ta bort certifikat?","deleteConfirm":"Ta bort {name}? Certifikat som används av aktiva routes kan inte tas bort.","uploadTitle":"Ladda upp PKCS#12-certifikat","uploadInfo":"Importlösenordet används bara när certifikatet valideras. Certifikatmaterialet krypteras före lagring.","file":"PFX- eller P12-fil","fileHint":"Maximal filstorlek: 5 MiB.","passwordHint":"Lämna tomt endast om filen saknar lösenord.","cancel":"Avbryt","save":"Spara","close":"Stäng","fileRequired":"Välj en certifikatfil."}}
</i18n>

<i18n lang="json">
{
  "en": {
    "editCertificate": "Edit certificate",
    "certificateRenamed": "Certificate name saved.",
    "failed": "Issuance failed",
    "manualDns01": "DNS-01 (manual)",
    "manualDnsPending": "Manual DNS required",
    "manualDnsInfo": "ApiGateway will show the required TXT record in certificate details. Add it in your DNS control panel before the challenge expires. Manual action is required again for every renewal.",
    "manualDnsRequiredTitle": "Add this DNS TXT record",
    "manualDnsRequired": "Create or append the value below without removing other TXT values. ApiGateway will continue automatically after it appears on the authoritative name servers.",
    "manualDnsPreparing": "ApiGateway is preparing the ACME challenge. The TXT name and value will appear here shortly.",
    "manualDnsName": "TXT record name",
    "manualDnsValue": "TXT record value",
    "manualDnsExpires": "Challenge expires: {date}",
    "copyManualDnsName": "Copy TXT record name",
    "copyManualDnsValue": "Copy TXT record value",
    "manualDnsCopied": "Copied to the clipboard.",
    "manualDnsCopyFailed": "The value could not be copied automatically. Select the displayed value and copy it manually.",
    "automaticDnsFallbackTitle": "DNS TXT challenge",
    "automaticDnsFallback": "The DNS provider was asked to publish this value. If it is missing, you can append it manually without removing other TXT values.",
    "profileTested": "TXT creation and cleanup succeeded. {count} zones are available.",
    "activityUnavailableTitle": "Issuance activity could not be loaded",
    "queueGuidance": "This request is queued. The certificate worker checks pending work every minute, and another certificate may be processed first.",
    "queueOverdueGuidance": "The scheduled attempt time has passed. The certificate worker may be processing another certificate or may be unavailable. Review the Management service logs for 'ACME certificate maintenance failed'.",
    "viewDetailsNamed": "View details for {name}",
    "certificateDetailsTitle": "Certificate details: {name}",
    "actionRequiredTitle": "Certificate issuance needs attention",
    "automaticRetryAt": "ApiGateway will retry automatically at {date}.",
    "dnsTimeoutGuidance": "Confirm that the DNS profile can update the authoritative zone. Public TXT propagation may take up to an hour. After correcting the problem, use Retry when it becomes available.",
    "failureGuidance": "Review the activity below, correct the reported configuration or connectivity problem, and then use Retry.",
    "activityTitle": "Issuance activity",
    "noActivity": "No detailed activity has been recorded yet.",
    "activityRequested": "Certificate issuance requested",
    "activityRenewalRequested": "Certificate renewal requested",
    "activityAttemptStarted": "Issuance attempt started",
    "activityOrderCreated": "ACME order created",
    "activityDnsRecordPresented": "DNS TXT record published",
    "activityManualDnsRecordRequired": "Manual DNS TXT record required",
    "activityDnsPropagationObserved": "DNS TXT propagation confirmed",
    "activityValidationRequested": "Certificate authority validation requested",
    "activityValidationCompleted": "Certificate authority validation completed",
    "activityFinalizationStarted": "Certificate finalization started",
    "activityIssued": "Certificate issued",
    "activityRenewed": "Certificate renewed",
    "activityIssuanceFailed": "Issuance attempt failed",
    "activityAttemptRecovered": "Interrupted attempt recovered"
  },
  "sv": {
    "editCertificate": "Redigera certifikat",
    "certificateRenamed": "Certifikatnamnet har sparats.",
    "failed": "Utfärdning misslyckades",
    "manualDns01": "DNS-01 (manuell)",
    "manualDnsPending": "Manuell DNS krävs",
    "manualDnsInfo": "ApiGateway visar den TXT-post som krävs i certifikatdetaljerna. Lägg till den i DNS-kontrollpanelen innan utmaningen upphör. En manuell åtgärd krävs igen vid varje förnyelse.",
    "manualDnsRequiredTitle": "Lägg till denna DNS TXT-post",
    "manualDnsRequired": "Skapa eller lägg till värdet nedan utan att ta bort andra TXT-värden. ApiGateway fortsätter automatiskt när värdet syns på de auktoritativa namnservrarna.",
    "manualDnsPreparing": "ApiGateway förbereder ACME-utmaningen. TXT-namnet och värdet visas här inom kort.",
    "manualDnsName": "Namn på TXT-post",
    "manualDnsValue": "Värde för TXT-post",
    "manualDnsExpires": "Utmaningen upphör: {date}",
    "copyManualDnsName": "Kopiera TXT-postens namn",
    "copyManualDnsValue": "Kopiera TXT-postens värde",
    "manualDnsCopied": "Kopierat till urklipp.",
    "manualDnsCopyFailed": "Värdet kunde inte kopieras automatiskt. Markera det visade värdet och kopiera det manuellt.",
    "automaticDnsFallbackTitle": "DNS TXT-utmaning",
    "automaticDnsFallback": "DNS-leverantören har ombetts att publicera detta värde. Om det saknas kan du lägga till det manuellt utan att ta bort andra TXT-värden.",
    "profileTested": "TXT-posten skapades och togs bort. {count} zoner är tillgängliga.",
    "activityUnavailableTitle": "Utfärdningsaktiviteten kunde inte läsas in",
    "queueGuidance": "Begäran ligger i kö. Certifikatarbetaren söker efter väntande arbete varje minut och ett annat certifikat kan behandlas först.",
    "queueOverdueGuidance": "Den schemalagda tiden för försöket har passerat. Certifikatarbetaren kan behandla ett annat certifikat eller vara otillgänglig. Granska Management-tjänstens loggar efter 'ACME certificate maintenance failed'.",
    "viewDetailsNamed": "Visa detaljer för {name}",
    "certificateDetailsTitle": "Certifikatdetaljer: {name}",
    "actionRequiredTitle": "Certifikatsutfärdningen kräver åtgärd",
    "automaticRetryAt": "ApiGateway försöker automatiskt igen {date}.",
    "dnsTimeoutGuidance": "Kontrollera att DNS-profilen kan uppdatera den auktoritativa zonen. Publik TXT-propagation kan ta upp till en timme. När problemet har åtgärdats använder du Försök igen när åtgärden blir tillgänglig.",
    "failureGuidance": "Granska aktiviteten nedan, åtgärda det rapporterade konfigurations- eller anslutningsproblemet och använd sedan Försök igen.",
    "activityTitle": "Utfärdningsaktivitet",
    "noActivity": "Ingen detaljerad aktivitet har registrerats ännu.",
    "activityRequested": "Certifikatsutfärdning begärd",
    "activityRenewalRequested": "Certifikatsförnyelse begärd",
    "activityAttemptStarted": "Utfärdningsförsök startat",
    "activityOrderCreated": "ACME-order skapad",
    "activityDnsRecordPresented": "DNS TXT-post publicerad",
    "activityManualDnsRecordRequired": "Manuell DNS TXT-post krävs",
    "activityDnsPropagationObserved": "DNS TXT-propagation bekräftad",
    "activityValidationRequested": "Validering hos certifikatutfärdaren begärd",
    "activityValidationCompleted": "Validering hos certifikatutfärdaren slutförd",
    "activityFinalizationStarted": "Slutförande av certifikat startat",
    "activityIssued": "Certifikat utfärdat",
    "activityRenewed": "Certifikat förnyat",
    "activityIssuanceFailed": "Utfärdningsförsök misslyckades",
    "activityAttemptRecovered": "Avbrutet försök återställt"
  }
}
</i18n>
