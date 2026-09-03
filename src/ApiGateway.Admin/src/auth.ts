import type { AccountInfo } from '@azure/msal-browser';
import { PublicClientApplication } from '@azure/msal-browser';
import { ref } from 'vue';
import { setAntiforgeryToken, setBearerToken } from './api';

export interface ExternalProvider { id: string; displayName: string; type: 'ldap' | 'oidc' | 'entra' }
export const authenticated = ref(false); export const bootstrapRequired = ref(false); export const username = ref(''); export const entraAvailable = ref(false); export const mustChangePassword = ref(false); export const identityProviders = ref<ExternalProvider[]>([]);

let msal: PublicClientApplication | null = null; let entraScope = ''; let entraAccount: AccountInfo | null = null; let usingEntra = false;

export async function initializeAuthentication() {
  await refreshAuth();

  const config = await fetch('/admin/config.json').then(x => x.json()) as { entra?: { authority?: string; clientId?: string; scope?: string } };

  if (!config.entra?.authority || !config.entra.clientId || !config.entra.scope)
    return;

  entraAvailable.value = true; entraScope = config.entra.scope; msal = new PublicClientApplication({ auth: { clientId: config.entra.clientId, authority: config.entra.authority, redirectUri: `${location.origin}/admin/`, postLogoutRedirectUri: `${location.origin}/admin/` }, cache: { cacheLocation: 'sessionStorage' } }); await msal.initialize();

  const redirect = await msal.handleRedirectPromise();

  entraAccount = redirect?.account || msal.getAllAccounts()[0] || null;

  if (entraAccount)
    await activateEntra(entraAccount);
}
export async function refreshAuth() {
  const response = await fetch('/admin/auth/status', { credentials: 'same-origin' });

  if (!response.ok)
    throw new Error('Unable to read authentication state.');

  const state = await response.json() as { bootstrapRequired: boolean; authenticated: boolean; username?: string; mustChangePassword?: boolean; providers?: ExternalProvider[]; antiforgeryToken: string };

  bootstrapRequired.value = state.bootstrapRequired;
  identityProviders.value = state.providers ?? [];

  if (!usingEntra) { authenticated.value = state.authenticated; username.value = state.username || ''; mustChangePassword.value = !!state.mustChangePassword; }

  setAntiforgeryToken(state.antiforgeryToken);
}
export async function signIn(name: string, password: string, providerId?: string) {
  usingEntra = false; setBearerToken(''); await refreshAuth();

  const path = bootstrapRequired.value ? '/admin/auth/bootstrap' : '/admin/auth/login'; const state = await fetch('/admin/auth/status', { credentials: 'same-origin' }).then(x => x.json()) as { antiforgeryToken: string }; const response = await fetch(path, { method: 'POST', credentials: 'same-origin', headers: { 'content-type': 'application/json', 'X-CSRF-TOKEN': state.antiforgeryToken }, body: JSON.stringify({ username: name, password, providerId }) });

  if (!response.ok)
    throw new Error(response.status === 401 ? 'The username or password is incorrect.' : 'Authentication failed.');

  await refreshAuth();
}
export async function signInExternal(providerId: string) {
  const state = await fetch('/admin/auth/status', { credentials: 'same-origin' }).then(x => x.json()) as { antiforgeryToken: string };
  const response = await fetch(`/admin/auth/external/${encodeURIComponent(providerId)}/start`, { method: 'POST', credentials: 'same-origin', headers: { 'content-type': 'application/json', 'X-CSRF-TOKEN': state.antiforgeryToken }, body: JSON.stringify({ returnUrl: '/admin/' }) });

  if (!response.ok)
    throw new Error('External authentication failed.');

  location.assign((await response.json() as { url: string }).url);
}
export async function signInEntra() {
  if (!msal)
    throw new Error('Microsoft Entra ID is not configured.');

  const result = await msal.loginPopup({ scopes: [entraScope] });

  await activateEntra(result.account);
}
async function activateEntra(account: AccountInfo) {
  if (!msal)
    return;

  const token = await msal.acquireTokenSilent({ account, scopes: [entraScope] });

  entraAccount = account; usingEntra = true; setBearerToken(token.accessToken); authenticated.value = true; bootstrapRequired.value = false; username.value = account.name || account.username;
}
export async function signOut() {
  if (usingEntra && msal && entraAccount) {
    const account = entraAccount;

    usingEntra = false; entraAccount = null; setBearerToken(''); authenticated.value = false; username.value = ''; await msal.logoutPopup({ account, mainWindowRedirectUri: `${location.origin}/admin/` }); return;
  }

  const state = await fetch('/admin/auth/status', { credentials: 'same-origin' }).then(x => x.json()) as { antiforgeryToken: string };

  await fetch('/admin/auth/logout', { method: 'POST', credentials: 'same-origin', headers: { 'X-CSRF-TOKEN': state.antiforgeryToken } }); await refreshAuth();
}
export async function changePassword(currentPassword: string, newPassword: string) {
  const state = await fetch('/admin/auth/status', { credentials: 'same-origin' }).then(x => x.json()) as { antiforgeryToken: string }; const response = await fetch('/admin/auth/change-password', { method: 'POST', credentials: 'same-origin', headers: { 'content-type': 'application/json', 'X-CSRF-TOKEN': state.antiforgeryToken }, body: JSON.stringify({ currentPassword, newPassword }) });

  if (!response.ok)
    throw new Error('Password change failed.');

  await refreshAuth();
}
