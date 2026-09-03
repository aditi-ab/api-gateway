<template>
  <TooltipProvider>
    <ConfigProvider :scroll-body="false">
      <div class="app-shell">
        <template v-if="authenticated">
          <header class="app-header h-28 border-b">
            <div class="w-full h-full flex flex-col">
              <div class="flex items-center px-5 md:px-8" style="height: 64px">
                <div class="app-product-icon">
                  <img :src="productIconUrl" alt="" class="product-mark-image">
                </div><div class="ml-3">
                  <div class="font-bold">
                    {{ t('common.productName') }}
                  </div><div class="text-xs text-muted-foreground">
                    {{ t('common.managementConsole') }}
                  </div>
                </div>
                <div class="ml-auto header-actions">
                  <Button variant="ghost" as-child class="hidden sm:flex">
                    <a href="/docs/" target="_blank"><BookOpen />{{ t('common.documentation') }}</a>
                  </Button>
                  <DropdownMenu :modal="false">
                    <DropdownMenuTrigger as-child>
                      <Button variant="secondary" class="language-button" :aria-label="t('common.language')">
                        <Languages />{{ locale.toUpperCase() }}
                      </Button>
                    </DropdownMenuTrigger><DropdownMenuContent align="end">
                      <DropdownMenuRadioGroup :model-value="locale" @update:model-value="selectLocale">
                        <DropdownMenuRadioItem v-for="option in languageOptions" :key="option.value" :value="option.value">
                          {{ option.title }}
                        </DropdownMenuRadioItem>
                      </DropdownMenuRadioGroup>
                    </DropdownMenuContent>
                  </DropdownMenu>
                  <DropdownMenu :modal="false">
                    <DropdownMenuTrigger as-child>
                      <span class="inline-flex">
                        <Tooltip><TooltipTrigger as-child>
                          <Button variant="outline" size="icon" :aria-label="t('common.theme')">
                            <SunMoon />
                          </Button>
                        </TooltipTrigger><TooltipContent>{{ t('common.theme') }}</TooltipContent></Tooltip>
                      </span>
                    </DropdownMenuTrigger><DropdownMenuContent align="end">
                      <DropdownMenuRadioGroup :model-value="themePreference" @update:model-value="selectTheme">
                        <DropdownMenuRadioItem v-for="option in themeOptions" :key="option.value" :value="option.value">
                          <component :is="option.icon" />{{ option.title }}
                        </DropdownMenuRadioItem>
                      </DropdownMenuRadioGroup>
                    </DropdownMenuContent>
                  </DropdownMenu>
                  <DropdownMenu :modal="false">
                    <DropdownMenuTrigger as-child>
                      <Button variant="ghost">
                        <UserCircle />{{ username }}
                      </Button>
                    </DropdownMenuTrigger><DropdownMenuContent align="end">
                      <DropdownMenuItem @select="passwordDialog = true">
                        <KeyRound />{{ t('common.changePassword') }}
                      </DropdownMenuItem><DropdownMenuItem @select="signOut">
                        <LogOut />{{ t('common.signOut') }}
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              </div>
              <nav class="global-nav flex items-center justify-between px-4 md:px-7" :aria-label="t('common.primaryNavigation')">
                <NavigationMenu :viewport="false" class="global-navigation-menu">
                  <NavigationMenuList>
                    <NavigationMenuItem v-for="item in primary" :key="item.path">
                      <NavigationMenuLink as-child :active="active(item.path)" :class="navigationMenuTriggerStyle()">
                        <RouterLink :to="item.path" :aria-current="active(item.path) ? 'page' : undefined">
                          {{ t(item.label) }}
                        </RouterLink>
                      </NavigationMenuLink>
                    </NavigationMenuItem>
                    <NavigationMenuItem v-for="group in groups" :key="group.label">
                      <NavigationMenuTrigger :class="{ 'bg-muted text-foreground': group.items.some(x => active(x.path)) }">
                        {{ t(group.label) }}
                      </NavigationMenuTrigger><NavigationMenuContent class="min-w-56">
                        <ul class="grid gap-1">
                          <li v-for="item in group.items" :key="item.path">
                            <NavigationMenuLink as-child :active="active(item.path)">
                              <RouterLink :to="item.path" :aria-current="active(item.path) ? 'page' : undefined">
                                <component :is="item.icon" />{{ t(item.label) }}
                              </RouterLink>
                            </NavigationMenuLink>
                          </li>
                        </ul>
                      </NavigationMenuContent>
                    </NavigationMenuItem>
                  </NavigationMenuList>
                </NavigationMenu>
                <div class="flex items-center ml-3">
                  <Badge v-if="selectedEnvironment?.publishingMode === 'STAGED'" class="mr-2">
                    {{ t('common.staged') }}
                  </Badge>
                  <Select v-if="environments.length" v-model="selectedEnvironmentId" @update:model-value="persistEnvironment">
                    <SelectTrigger class="environment-select" :aria-label="t('common.environment')">
                      <SelectValue :placeholder="t('common.environment')" />
                    </SelectTrigger><SelectContent>
                      <SelectItem v-for="environment in environments" :key="environment.id" :value="environment.id">
                        {{ environment.displayName }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </nav>
            </div>
          </header><main class="grow">
            <PendingChangesBanner />
            <router-view v-slot="{ Component }">
              <transition name="route-view" mode="out-in">
                <div :key="`${route.fullPath}:${configurationRefreshVersion}`" class="route-view">
                  <component :is="Component" />
                </div>
              </transition>
            </router-view>
          </main>
        </template>
        <template v-else>
          <header class="app-header flex h-16 items-center border-b px-5 md:px-8">
            <div class="app-product-icon">
              <img :src="productIconUrl" alt="" class="product-mark-image">
            </div><div class="ml-3">
              <div class="font-bold">
                {{ t('common.productName') }}
              </div><div class="text-xs text-muted-foreground">
                {{ t('common.managementConsole') }}
              </div>
            </div>
          </header><main class="grow">
            <div class="identity-sign-in-page">
              <Card class="identity-sign-in-card p-8 sm:p-10">
                <div class="eyebrow">
                  {{ t('auth.eyebrow') }}
                </div><h1 class="page-title mt-2">
                  {{ bootstrapRequired ? t('auth.createTitle') : t('auth.signInTitle') }}
                </h1><p class="page-lead mb-7">
                  {{ bootstrapRequired ? t('auth.createLead') : t('auth.localLead') }}
                </p><Alert v-if="authError" variant="destructive" class="mb-4">
                  <CircleAlert /><AlertDescription>{{ authError }}</AlertDescription>
                </Alert>
                <div v-if="loading" class="flex justify-center py-8">
                  <Spinner />
                </div>
                <form v-else class="grid gap-4" @submit.prevent="submit">
                  <Field v-if="!bootstrapRequired && passwordProviders.length > 1">
                    <FieldLabel for="login-provider">
                      {{ t('auth.provider') }}
                    </FieldLabel><Select v-model="selectedProvider">
                      <SelectTrigger id="login-provider">
                        <SelectValue />
                      </SelectTrigger><SelectContent>
                        <SelectItem v-for="provider in passwordProviders" :key="provider.id" :value="provider.id">
                          {{ provider.displayName }}
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel for="login-name">
                      {{ t('auth.username') }}
                    </FieldLabel><Input id="login-name" v-model="loginName" autocomplete="username" />
                  </Field>
                  <Field>
                    <FieldLabel for="login-password">
                      {{ t('auth.password') }}
                    </FieldLabel><Input id="login-password" v-model="password" type="password" autocomplete="current-password" />
                  </Field>
                  <Field v-if="bootstrapRequired">
                    <FieldLabel for="login-confirm-password">
                      {{ t('auth.confirmPassword') }}
                    </FieldLabel><Input id="login-confirm-password" v-model="confirmPassword" type="password" autocomplete="new-password" />
                  </Field>
                  <Button class="mt-2 w-full" type="submit" size="lg" :disabled="authenticationBusy">
                    <Spinner v-if="authenticationBusy" />{{ bootstrapRequired ? t('auth.create') : t('auth.signIn') }}
                  </Button>
                </form><template v-if="entraAvailable && !bootstrapRequired">
                  <div class="my-5 flex items-center gap-3">
                    <Separator class="grow" /><span class="text-xs text-muted-foreground">{{ t('common.or') }}</span><Separator class="grow" />
                  </div><Button class="w-full" variant="outline" @click="submitEntra">
                    {{ t('auth.microsoft') }}
                  </Button>
                </template><template v-for="provider in oidcProviders" :key="provider.id">
                  <div class="my-5 flex items-center gap-3">
                    <Separator class="grow" /><span class="text-xs text-muted-foreground">{{ t('common.or') }}</span><Separator class="grow" />
                  </div><Button class="w-full" variant="outline" @click="submitExternal(provider.id)">
                    {{ t('auth.continueWith', { provider: provider.displayName }) }}
                  </Button>
                </template>
              </Card>
            </div>
          </main>
        </template>
        <footer class="app-footer border-t">
          <div class="content-shell text-center">
            <a href="https://github.com/aditi-ab/api-gateway" target="_blank" rel="noopener noreferrer" class="text-primary">{{ t('common.sourceCode') }}</a>
          </div>
        </footer>
        <ConfirmDialog />
        <Dialog v-model:open="passwordDialog">
          <DialogContent size="md" :show-close-button="!mustChangePassword" @escape-key-down="mustChangePassword && $event.preventDefault()" @pointer-down-outside="mustChangePassword && $event.preventDefault()">
            <DialogHeader><DialogTitle>{{ t('common.changePassword') }}</DialogTitle></DialogHeader>
            <Alert v-if="mustChangePassword" class="border-amber-500/40 text-amber-700 dark:text-amber-300">
              <TriangleAlert /><AlertDescription>{{ t('common.passwordRequired') }}</AlertDescription>
            </Alert>
            <FieldGroup>
              <Field>
                <FieldLabel for="current-password">
                  {{ t('auth.password') }}
                </FieldLabel><Input id="current-password" v-model="currentPassword" type="password" />
              </Field><Field>
                <FieldLabel for="new-password">
                  {{ t('common.newPassword') }}
                </FieldLabel><Input id="new-password" v-model="newPassword" type="password" />
              </Field>
            </FieldGroup>
            <DialogFooter>
              <Button v-if="!mustChangePassword" variant="outline" @click="passwordDialog = false">
                {{ t('common.back') }}
              </Button><Button @click="submitPassword">
                {{ t('common.changePassword') }}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>
    </ConfigProvider>
  </TooltipProvider>
</template>

<script setup lang="ts">
import type { AcceptableValue } from 'reka-ui';
import type { Component } from 'vue';
import type { ThemePreference } from './composables/themePreference';
import { Alert, AlertDescription, Badge, Button, Card, ConfigProvider, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuRadioGroup, DropdownMenuRadioItem, DropdownMenuTrigger, Field, FieldGroup, FieldLabel, Input, NavigationMenu, NavigationMenuContent, NavigationMenuItem, NavigationMenuLink, NavigationMenuList, NavigationMenuTrigger, navigationMenuTriggerStyle, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Separator, Spinner, Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@aditify/ui';
import { Award, BookOpen, CircleAlert, ClipboardClock, Earth, Key, KeyRound, Languages, LogOut, Monitor, Moon, RadioTower, Settings, Sun, SunMoon, TriangleAlert, UserCircle, UserRoundKey, Users } from '@lucide/vue';
import { computed, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute } from 'vue-router';
import { authenticated, bootstrapRequired, changePassword, entraAvailable, identityProviders, initializeAuthentication, mustChangePassword, signIn, signInEntra, signInExternal, signOut, username } from './auth';
import ConfirmDialog from './components/ConfirmDialog.vue';
import PendingChangesBanner from './components/PendingChangesBanner.vue';
import { configurationRefreshVersion, environments, loadEnvironments, persistEnvironment, selectedEnvironment, selectedEnvironmentId } from './composables/environmentContext';
import { initializeTheme, setThemePreference, themePreference } from './composables/themePreference';

const { t } = useI18n(); const { locale } = useI18n({ useScope: 'global' }); const route = useRoute(); const productIconUrl = `${import.meta.env.BASE_URL}api-gateway.svg`;
const loginName = ref(''); const password = ref(''); const confirmPassword = ref(''); const authError = ref(''); const loading = ref(true); const authenticationBusy = ref(false);
const selectedProvider = ref('local');
const passwordProviders = computed(() => [{ id: 'local', displayName: t('auth.local') }, ...identityProviders.value.filter(provider => provider.type === 'ldap')]);
const oidcProviders = computed(() => identityProviders.value.filter(provider => provider.type === 'oidc'));
const passwordDialog = ref(false); const currentPassword = ref(''); const newPassword = ref('');
const primary = [{ label: 'nav.dashboard', path: '/' }, { label: 'nav.routes', path: '/routes' }, { label: 'nav.upstreams', path: '/upstreams' }, { label: 'nav.activity', path: '/activity' }];
const groups: Array<{ label: string; items: Array<{ label: string; path: string; icon: Component }> }> = [{ label: 'nav.access', items: [{ label: 'nav.users', path: '/users', icon: Users }, { label: 'nav.consumerKeys', path: '/consumer-keys', icon: Key }, { label: 'nav.managementKeys', path: '/management-keys', icon: UserRoundKey }] }, { label: 'nav.system', items: [{ label: 'nav.environments', path: '/environments', icon: Earth }, { label: 'nav.instances', path: '/instances', icon: RadioTower }, { label: 'nav.certificates', path: '/certificates', icon: Award }, { label: 'nav.audit', path: '/audit', icon: ClipboardClock }, { label: 'nav.settings', path: '/settings', icon: Settings }] }];
const languageOptions = computed(() => [{ title: t('common.english'), value: 'en' }, { title: t('common.swedish'), value: 'sv' }]);
const themeOptions = computed<Array<{ title: string; value: ThemePreference; icon: Component }>>(() => [{ title: t('common.themeSystem'), value: 'system', icon: Monitor }, { title: t('common.themeLight'), value: 'light', icon: Sun }, { title: t('common.themeDark'), value: 'dark', icon: Moon }]);

function active(path: string) { return path === '/' ? route.path === '/' : route.path === path || route.path.startsWith(`${path}/`); }
function selectLocale(value: AcceptableValue) {
  if (typeof value === 'string')
    locale.value = value;
}
function selectTheme(value: AcceptableValue) {
  if (value === 'system' || value === 'light' || value === 'dark')
    setThemePreference(value);
}
onMounted(async () => {
  initializeTheme();

  try {
    await initializeAuthentication();

    if (authenticated.value)
      await loadEnvironments();
  }
  catch (e) { authError.value = e instanceof Error ? e.message : String(e); }
  finally { loading.value = false; }
});
watch(mustChangePassword, (value) => {
  if (value)
    passwordDialog.value = true;
}, { immediate: true });
async function submitPassword() {
  authError.value = '';

  try { await changePassword(currentPassword.value, newPassword.value); passwordDialog.value = false; currentPassword.value = ''; newPassword.value = ''; }
  catch (e) { authError.value = e instanceof Error ? e.message : String(e); }
}
watch(authenticated, (value) => {
  if (value)
    void loadEnvironments();
}); watch(locale, value => localStorage.setItem('apigateway-locale', value));
async function submit() {
  authError.value = '';

  if (bootstrapRequired.value && password.value !== confirmPassword.value) { authError.value = t('auth.passwordMismatch'); return; }

  authenticationBusy.value = true;

  try { await signIn(loginName.value, password.value, selectedProvider.value === 'local' ? undefined : selectedProvider.value); password.value = ''; confirmPassword.value = ''; }
  catch (e) { authError.value = e instanceof Error ? e.message : String(e); }
  finally { authenticationBusy.value = false; }
}
async function submitExternal(providerId: string) {
  authenticationBusy.value = true; authError.value = '';

  try { await signInExternal(providerId); }
  catch (e) { authError.value = e instanceof Error ? e.message : String(e); authenticationBusy.value = false; }
}
async function submitEntra() {
  authenticationBusy.value = true;

  try { await signInEntra(); }
  catch (e) { authError.value = e instanceof Error ? e.message : String(e); }
  finally { authenticationBusy.value = false; }
}
</script>

<i18n lang="json">
{
  "en": {
    "nav": { "dashboard": "Overview", "routes": "Routes", "upstreams": "Upstreams", "activity": "Activity", "access": "Access", "users": "Users", "certificates": "Certificates", "system": "System", "environments": "Environments", "instances": "Gateway instances", "consumerKeys": "Consumer keys", "managementKeys": "Management keys", "audit": "Audit", "settings": "Settings" },
    "auth": { "eyebrow": "Protected management plane", "createTitle": "Create the first administrator", "signInTitle": "Sign in to administer the gateway", "createLead": "Choose the local administrator credentials for this gateway.", "localLead": "Use a local or configured identity provider account.", "create": "Create administrator", "signIn": "Sign in", "username": "Username", "password": "Password", "provider": "Provider", "local": "Local", "continueWith": "Continue with {provider}", "confirmPassword": "Confirm password", "passwordMismatch": "Passwords do not match.", "microsoft": "Continue with Microsoft" },
    "common": { "productName": "API Gateway", "language": "Language", "english": "English", "swedish": "Swedish", "theme": "Theme preference", "themeSystem": "System", "themeLight": "Light", "themeDark": "Dark", "primaryNavigation": "Primary navigation", "managementConsole": "Management console", "documentation": "Documentation", "sourceCode": "View API Gateway on GitHub", "environment": "Environment", "staged": "Staged", "signOut": "Sign out", "changePassword":"Change password","newPassword":"New password","passwordRequired":"Change the temporary password before continuing.", "back": "Back", "or": "or" }
  },
  "sv": {
    "nav": { "dashboard": "Översikt", "routes": "Routes", "upstreams": "Upstreams", "activity": "Aktivitet", "access": "Åtkomst", "users": "Användare", "certificates": "Certifikat", "system": "System", "environments": "Miljöer", "instances": "Gatewayinstanser", "consumerKeys": "Konsumentnycklar", "managementKeys": "Hanteringsnycklar", "audit": "Granskning", "settings": "Inställningar" },
    "auth": { "eyebrow": "Skyddad administrationsyta", "createTitle": "Skapa den första administratören", "signInTitle": "Logga in för att administrera gatewayen", "createLead": "Välj autentiseringsuppgifter för den lokala administratören.", "localLead": "Använd ett lokalt konto eller en konfigurerad identitetsleverantör.", "create": "Skapa administratör", "signIn": "Logga in", "username": "Användarnamn", "password": "Lösenord", "provider": "Leverantör", "local": "Lokalt", "continueWith": "Fortsätt med {provider}", "confirmPassword": "Bekräfta lösenord", "passwordMismatch": "Lösenorden matchar inte.", "microsoft": "Fortsätt med Microsoft" },
    "common": { "productName": "API Gateway", "language": "Språk", "english": "Engelska", "swedish": "Svenska", "theme": "Temainställning", "themeSystem": "System", "themeLight": "Ljust", "themeDark": "Mörkt", "primaryNavigation": "Huvudnavigering", "managementConsole": "Administrationskonsol", "documentation": "Dokumentation", "sourceCode": "Visa API Gateway på GitHub", "environment": "Miljö", "staged": "Stegvis", "signOut": "Logga ut", "changePassword":"Byt lösenord","newPassword":"Nytt lösenord","passwordRequired":"Byt det tillfälliga lösenordet innan du fortsätter.", "back": "Tillbaka", "or": "eller" }
  }
}
</i18n>

<style scoped>
.route-view {
  transition:
    opacity 200ms cubic-bezier(0, 0, 0.2, 1),
    transform 200ms cubic-bezier(0, 0, 0.2, 1);
}

.route-view-enter-from {
  opacity: 0;
  transform: translateY(0.375rem);
}

.route-view-leave-to {
  opacity: 0;
  transform: translateY(-0.25rem);
}

@media (prefers-reduced-motion: reduce) {
  .route-view {
    transition: none;
  }

  .route-view-enter-from,
  .route-view-leave-to {
    opacity: 1;
    transform: none;
  }
}
</style>
