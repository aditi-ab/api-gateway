<template>
  <Alert v-if="!selectedEnvironmentId" class="mt-5 grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-x-3 px-4 py-3 has-data-[slot=alert-action]:pr-4 *:[svg]:translate-y-0">
    <Info />
    <AlertDescription>{{ t('message') }}</AlertDescription>
    <AlertAction class="static">
      <Button as-child>
        <RouterLink to="/environments">
          <Globe2 />{{ t('action') }}
        </RouterLink>
      </Button>
    </AlertAction>
  </Alert>
</template>

<script setup lang="ts">
import { Alert, AlertAction, AlertDescription, Button } from '@aditify/ui';
import { Globe2, Info } from '@lucide/vue';
import { useI18n } from 'vue-i18n';
import { selectedEnvironmentId } from '../composables/environmentContext';

const { t } = useI18n();
</script>

<i18n lang="json">
{
  "en": { "message": "Create an environment before configuring environment-specific resources.", "action": "Create environment" },
  "sv": { "message": "Skapa en miljö innan du konfigurerar miljöspecifika resurser.", "action": "Skapa miljö" }
}
</i18n>
