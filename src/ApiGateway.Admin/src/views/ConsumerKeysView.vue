<template>
  <div class="page-container">
    <div class="flex items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ $t('nav.access') }}
        </p><h1>{{ t('title') }}</h1><p class="page-lead">
          {{ t('lead') }}
        </p>
      </div><Button @click="openCreate">
        <Plus />{{ t('createKey') }}
      </Button>
    </div>
    <Alert v-if="error" variant="destructive" class="mt-4">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Card class="mt-6 py-0">
      <Table>
        <TableHeader><TableRow><TableHead>{{ t('name') }}</TableHead><TableHead>{{ t('prefix') }}</TableHead><TableHead>{{ t('expires') }}</TableHead><TableHead>{{ t('lastUsed') }}</TableHead><TableHead>{{ t('status') }}</TableHead><TableHead /></TableRow></TableHeader><TableBody>
          <TableRow v-for="key in keys" :key="key.id">
            <TableCell>{{ key.name }}</TableCell><TableCell><code>{{ key.prefix }}</code></TableCell><TableCell>{{ key.expiresAtUtc ? formatDateTime(key.expiresAtUtc) : t('never') }}</TableCell><TableCell>{{ key.lastUsedAtUtc ? formatDateTime(key.lastUsedAtUtc) : t('never') }}</TableCell><TableCell>
              <Badge :variant="key.revokedAtUtc ? 'destructive' : 'success'">
                {{ key.revokedAtUtc ? t('revoked') : t('active') }}
              </Badge>
            </TableCell><TableCell>
              <div class="flex justify-end gap-1">
                <IconButton v-if="!key.revokedAtUtc" variant="secondary" :label="t('rotate')" @click="rotate(key.id)">
                  <RefreshCw />
                </IconButton><IconButton v-if="!key.revokedAtUtc" variant="destructive" :label="t('revoke')" @click="revoke(key.id)">
                  <Trash2 />
                </IconButton>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>
    <Dialog v-model:open="dialog">
      <DialogContent size="xl">
        <DialogHeader><DialogTitle>{{ t('createTitle') }}</DialogTitle></DialogHeader><FieldGroup>
          <Field>
            <FieldLabel for="consumer-key-name">
              {{ t('name') }}
            </FieldLabel><Input id="consumer-key-name" v-model="name" />
          </Field>
          <Field>
            <FieldLabel for="consumer-key-environments">
              {{ t('allowedEnvironments') }}
            </FieldLabel><Select v-model="environmentIds" multiple>
              <SelectTrigger id="consumer-key-environments">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="environment in environments" :key="environment.id" :value="environment.id">
                  {{ environment.displayName }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="consumer-key-routes">
              {{ t('allowedRoutes') }}
            </FieldLabel><Textarea id="consumer-key-routes" v-model="routeIdsText" /><FieldDescription>{{ t('routesHint') }}</FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="consumer-key-cidrs">
              {{ t('allowedCidrs') }}
            </FieldLabel><Textarea id="consumer-key-cidrs" v-model="cidrs" /><FieldDescription>{{ t('cidrHint') }}</FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="consumer-key-expiry">
              {{ t('expiry') }}
            </FieldLabel><Input id="consumer-key-expiry" v-model="expires" type="datetime-local" />
          </Field>
        </FieldGroup><DialogFooter>
          <Button variant="outline" @click="dialog = false">
            {{ t('cancel') }}
          </Button><Button :disabled="!name || !environmentIds.length" @click="create">
            {{ t('create') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <Dialog :open="!!secret">
      <DialogContent size="xl" :show-close-button="false" @escape-key-down="$event.preventDefault()" @pointer-down-outside="$event.preventDefault()">
        <DialogHeader><DialogTitle>{{ t('copyTitle') }}</DialogTitle></DialogHeader>
        <Alert class="border-amber-500/40 text-amber-700 dark:text-amber-300">
          <TriangleAlert /><AlertDescription>{{ t('secretWarning') }}</AlertDescription>
        </Alert>
        <Field>
          <FieldLabel for="consumer-key-secret">
            {{ t('apiKey') }}
          </FieldLabel><InputGroup>
            <InputGroupInput id="consumer-key-secret" :model-value="secret" readonly /><InputGroupAddon align="inline-end">
              <Tooltip>
                <TooltipTrigger as-child>
                  <InputGroupButton :aria-label="t('apiKey')" @click="copySecret">
                    <Copy />
                  </InputGroupButton>
                </TooltipTrigger><TooltipContent>{{ t('apiKey') }}</TooltipContent>
              </Tooltip>
            </InputGroupAddon>
          </InputGroup>
        </Field>
        <DialogFooter>
          <Button @click="secret = ''">
            {{ t('saved') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Badge, Button, Card, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, Field, FieldDescription, FieldGroup, FieldLabel, Input, InputGroup, InputGroupAddon, InputGroupButton, InputGroupInput, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Textarea, Tooltip, TooltipContent, TooltipTrigger } from '@aditify/ui';
import { CircleAlert, Copy, Plus, RefreshCw, Trash2, TriangleAlert } from '@lucide/vue';
import { onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql } from '../api';
import IconButton from '../components/IconButton.vue';
import { confirmAction } from '../composables/confirmDialog';
import { formatDateTime } from '../utils/dateTime';

interface KeyInfo { id: string; name: string; prefix: string; expiresAtUtc?: string; revokedAtUtc?: string; lastUsedAtUtc?: string }
interface Environment { id: string; displayName: string }

const keys = ref<KeyInfo[]>([]); const environments = ref<Environment[]>([]); const dialog = ref(false); const name = ref(''); const environmentIds = ref<string[]>([]); const routeIdsText = ref(''); const cidrs = ref(''); const expires = ref(''); const secret = ref(''); const error = ref('');
const { t } = useI18n();

function openCreate() { name.value = ''; environmentIds.value = []; routeIdsText.value = ''; cidrs.value = ''; expires.value = ''; dialog.value = true; }

async function load() {
  const result = await graphql<{ consumerApiKeys: KeyInfo[]; environments: Environment[] }>(`query{consumerApiKeys{id name prefix expiresAtUtc revokedAtUtc lastUsedAtUtc} environments{id displayName}}`);

  keys.value = result.consumerApiKeys; environments.value = result.environments;
}
async function create() {
  await run(async () => {
    const routeIds = routeIdsText.value.split(/[\s,]+/).filter(Boolean); const allowedCidrs = cidrs.value.split(/[\s,]+/).filter(Boolean); const result = await graphql<{ createConsumerApiKey: { secret: string } }>(`mutation($name:String!,$environments:[UUID!]!,$routes:[String!]!,$cidrs:[String!],$expires:DateTime){createConsumerApiKey(name:$name,environmentIds:$environments,routeIds:$routes,claims:[],allowedCidrs:$cidrs,expiresAtUtc:$expires){secret}}`, { name: name.value, environments: environmentIds.value, routes: routeIds, cidrs: allowedCidrs, expires: expires.value || null });

    secret.value = result.createConsumerApiKey.secret; dialog.value = false; await load();
  });
}
async function revoke(id: string) {
  if (!await confirmAction(t('revokeMessage'), { title: t('revokeTitle'), confirmText: t('revoke'), color: 'error' }))
    return;

  await run(async () => { await graphql(`mutation($id:UUID!){revokeConsumerApiKey(id:$id)}`, { id }); await load(); });
}
async function rotate(id: string) {
  if (!await confirmAction(t('rotateMessage'), { title: t('rotateTitle'), confirmText: t('rotate') }))
    return;

  await run(async () => {
    const result = await graphql<{ rotateConsumerApiKey: { secret: string } }>(`mutation($id:UUID!){rotateConsumerApiKey(id:$id){secret}}`, { id });

    secret.value = result.rotateConsumerApiKey.secret; await load();
  });
}
async function copySecret() { await navigator.clipboard.writeText(secret.value); }
async function run(action: () => Promise<void>) {
  error.value = '';

  try { await action(); }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}
onMounted(() => run(load));
</script>

<i18n lang="json">
{
  "en": { "title": "Consumer keys", "lead": "Issue proxy credentials restricted to environments, routes, and source networks.", "createKey": "Create key", "name": "Name", "prefix": "Prefix", "expires": "Expires", "lastUsed": "Last used", "status": "Status", "never": "Never", "revoked": "Revoked", "active": "Active", "rotate": "Rotate", "revoke": "Revoke", "createTitle": "Create consumer key", "allowedEnvironments": "Allowed environments", "allowedRoutes": "Allowed route IDs", "routesHint": "Comma or whitespace separated. Leave empty to allow every route in the selected environments.", "allowedCidrs": "Allowed CIDR ranges (optional)", "cidrHint": "Comma or whitespace separated. Leave empty to allow every source address.", "expiry": "Expiry (optional)", "cancel": "Cancel", "create": "Create", "copyTitle": "Copy the key now", "secretWarning": "This secret is displayed once. It cannot be recovered later.", "apiKey": "Consumer API key", "saved": "I have saved it", "revokeMessage": "Proxy requests using this consumer key will be rejected after credential convergence.", "revokeTitle": "Revoke consumer key?", "rotateMessage": "The previous secret stops working after credential convergence.", "rotateTitle": "Rotate consumer key?" },
  "sv": { "title": "Konsumentnycklar", "lead": "Utfärda proxyautentisering begränsad till miljöer, routes och källnätverk.", "createKey": "Skapa nyckel", "name": "Namn", "prefix": "Prefix", "expires": "Upphör", "lastUsed": "Senast använd", "status": "Status", "never": "Aldrig", "revoked": "Återkallad", "active": "Aktiv", "rotate": "Rotera", "revoke": "Återkalla", "createTitle": "Skapa konsumentnyckel", "allowedEnvironments": "Tillåtna miljöer", "allowedRoutes": "Tillåtna route-ID:n", "routesHint": "Avgränsa med kommatecken eller blanksteg. Lämna tomt för att tillåta alla routes i valda miljöer.", "allowedCidrs": "Tillåtna CIDR-intervall (valfritt)", "cidrHint": "Avgränsa med kommatecken eller blanksteg. Lämna tomt för att tillåta alla källadresser.", "expiry": "Utgångstid (valfritt)", "cancel": "Avbryt", "create": "Skapa", "copyTitle": "Kopiera nyckeln nu", "secretWarning": "Hemligheten visas endast en gång och kan inte återställas senare.", "apiKey": "API-nyckel för konsument", "saved": "Jag har sparat den", "revokeMessage": "Proxyanrop med denna konsumentnyckel avvisas efter att autentiseringsuppgifterna har synkroniserats.", "revokeTitle": "Återkalla konsumentnyckel?", "rotateMessage": "Den tidigare hemligheten slutar fungera efter att autentiseringsuppgifterna har synkroniserats.", "rotateTitle": "Rotera konsumentnyckel?" }
}
</i18n>
