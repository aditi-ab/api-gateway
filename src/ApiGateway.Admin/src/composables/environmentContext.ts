import { computed, ref } from 'vue';
import { graphql } from '../api';

export interface GatewayEnvironment {
  id: string;
  slug: string;
  displayName: string;
  activeRevisionId?: string;
  pendingRevisionId?: string;
  publishingMode: 'IMMEDIATE' | 'STAGED';
  concurrencyVersion: string;
}

export const environments = ref<GatewayEnvironment[]>([]);
export const selectedEnvironmentId = ref(localStorage.getItem('apigateway-environment') || '');
export const selectedEnvironment = computed(() => environments.value.find(x => x.id === selectedEnvironmentId.value));
export const editableRevisionId = computed(() => selectedEnvironment.value?.pendingRevisionId || selectedEnvironment.value?.activeRevisionId);
export const configurationRefreshVersion = ref(0);

export async function loadEnvironments() {
  environments.value = (await graphql<{ environments: GatewayEnvironment[] }>(
    `query EnvironmentContext { environments { id slug displayName activeRevisionId pendingRevisionId publishingMode concurrencyVersion } }`,
  )).environments;

  if (!environments.value.some(x => x.id === selectedEnvironmentId.value))
    selectedEnvironmentId.value = environments.value[0]?.id || '';

  persistEnvironment();
}

export function refreshConfigurationViews() {
  configurationRefreshVersion.value++;
}

export function persistEnvironment() {
  if (selectedEnvironmentId.value)
    localStorage.setItem('apigateway-environment', selectedEnvironmentId.value);
  else
    localStorage.removeItem('apigateway-environment');
}
