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
      <DialogContent size="lg">
        <DialogHeader><DialogTitle>{{ t('createTitle') }}</DialogTitle></DialogHeader><FieldGroup>
          <Field>
            <FieldLabel for="management-key-name">
              {{ t('name') }}
            </FieldLabel><Input id="management-key-name" v-model="name" />
          </Field>
          <Field>
            <FieldLabel for="management-key-scopes">
              {{ t('scopes') }}
            </FieldLabel><Select v-model="scopes" multiple>
              <SelectTrigger id="management-key-scopes">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="scope in availableScopes" :key="scope" :value="scope">
                  {{ scope }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="management-key-cidrs">
              {{ t('allowedCidrs') }}
            </FieldLabel><Textarea id="management-key-cidrs" v-model="cidrs" /><FieldDescription>{{ t('cidrHint') }}</FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="management-key-expiry">
              {{ t('expiry') }}
            </FieldLabel><Input id="management-key-expiry" v-model="expires" type="datetime-local" />
          </Field>
        </FieldGroup><DialogFooter>
          <Button variant="outline" @click="dialog = false">
            {{ t('cancel') }}
          </Button><Button :disabled="!name || !scopes.length" @click="create">
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
          <FieldLabel for="management-key-secret">
            {{ t('apiKey') }}
          </FieldLabel><InputGroup>
            <InputGroupInput id="management-key-secret" :model-value="secret" readonly /><InputGroupAddon align="inline-end">
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

const keys = ref<KeyInfo[]>([]); const dialog = ref(false); const name = ref(''); const scopes = ref<string[]>(['config:read']); const cidrs = ref(''); const expires = ref(''); const secret = ref(''); const error = ref('');
const { t } = useI18n();
const availableScopes = ['config:read', 'config:manage', 'instances:read', 'credentials:read', 'credentials:write', 'audit:read', 'system:admin'];

function openCreate() { name.value = ''; scopes.value = ['config:read']; cidrs.value = ''; expires.value = ''; dialog.value = true; }

async function load() { keys.value = (await graphql<{ managementApiKeys: KeyInfo[] }>(`query{managementApiKeys{id name prefix expiresAtUtc revokedAtUtc lastUsedAtUtc}}`)).managementApiKeys; }
async function create() {
  await run(async () => {
    const allowedCidrs = cidrs.value.split(/[\s,]+/).filter(Boolean); const result = await graphql<{ createManagementApiKey: { secret: string } }>(`mutation($name:String!,$scopes:[String!]!,$cidrs:[String!],$expires:DateTime){createManagementApiKey(name:$name,scopes:$scopes,allowedCidrs:$cidrs,expiresAtUtc:$expires){secret}}`, { name: name.value, scopes: scopes.value, cidrs: allowedCidrs, expires: expires.value || null });

    secret.value = result.createManagementApiKey.secret; dialog.value = false; await load();
  });
}
async function revoke(id: string) {
  if (!await confirmAction(t('revokeMessage'), { title: t('revokeTitle'), confirmText: t('revoke'), color: 'error' }))
    return;

  await run(async () => { await graphql(`mutation($id:UUID!){revokeManagementApiKey(id:$id)}`, { id }); await load(); });
}
async function rotate(id: string) {
  if (!await confirmAction(t('rotateMessage'), { title: t('rotateTitle'), confirmText: t('rotate') }))
    return;

  await run(async () => {
    const result = await graphql<{ rotateManagementApiKey: { secret: string } }>(`mutation($id:UUID!){rotateManagementApiKey(id:$id){secret}}`, { id });

    secret.value = result.rotateManagementApiKey.secret; await load();
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
  "en": { "title": "Management keys", "lead": "Create scoped credentials for GraphQL automation.", "createKey": "Create key", "name": "Name", "prefix": "Prefix", "expires": "Expires", "lastUsed": "Last used", "status": "Status", "never": "Never", "revoked": "Revoked", "active": "Active", "rotate": "Rotate", "revoke": "Revoke", "createTitle": "Create management key", "scopes": "Scopes", "allowedCidrs": "Allowed CIDR ranges (optional)", "cidrHint": "Comma or whitespace separated. Leave empty to allow every source address.", "expiry": "Expiry (optional)", "cancel": "Cancel", "create": "Create", "copyTitle": "Copy the key now", "secretWarning": "This secret is displayed once. It cannot be recovered later.", "apiKey": "Management API key", "saved": "I have saved it", "revokeMessage": "Existing clients using this management key will lose access.", "revokeTitle": "Revoke management key?", "rotateMessage": "The previous secret stops working immediately.", "rotateTitle": "Rotate management key?" },
  "sv": { "title": "Hanteringsnycklar", "lead": "Skapa behörighetsbegränsade autentiseringsuppgifter för GraphQL-automatisering.", "createKey": "Skapa nyckel", "name": "Namn", "prefix": "Prefix", "expires": "Upphör", "lastUsed": "Senast använd", "status": "Status", "never": "Aldrig", "revoked": "Återkallad", "active": "Aktiv", "rotate": "Rotera", "revoke": "Återkalla", "createTitle": "Skapa hanteringsnyckel", "scopes": "Behörigheter", "allowedCidrs": "Tillåtna CIDR-intervall (valfritt)", "cidrHint": "Avgränsa med kommatecken eller blanksteg. Lämna tomt för att tillåta alla källadresser.", "expiry": "Utgångstid (valfritt)", "cancel": "Avbryt", "create": "Skapa", "copyTitle": "Kopiera nyckeln nu", "secretWarning": "Hemligheten visas endast en gång och kan inte återställas senare.", "apiKey": "API-nyckel för hantering", "saved": "Jag har sparat den", "revokeMessage": "Befintliga klienter som använder denna hanteringsnyckel förlorar åtkomst.", "revokeTitle": "Återkalla hanteringsnyckel?", "rotateMessage": "Den tidigare hemligheten slutar fungera omedelbart.", "rotateTitle": "Rotera hanteringsnyckel?" }
}
</i18n>
