<template>
  <AlertDialog :open="confirmDialogState.open" @update:open="onDialogVisibility">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{ confirmDialogState.title || t('title') }}</AlertDialogTitle>
        <AlertDialogDescription class="whitespace-pre-line">
          {{ confirmDialogState.message }}
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel @click="resolveConfirmation(false)">
          {{ confirmDialogState.cancelText || t('cancel') }}
        </AlertDialogCancel>
        <AlertDialogAction :variant="confirmDialogState.color === 'error' ? 'destructive' : 'default'" @click="resolveConfirmation(true)">
          {{ confirmDialogState.confirmText || t('continue') }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>

<script setup lang="ts">
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from '@aditify/ui';
import { useI18n } from 'vue-i18n';
import { confirmDialogState, resolveConfirmation } from '../composables/confirmDialog';

const { t } = useI18n();

function onDialogVisibility(visible: boolean) {
  if (!visible)
    resolveConfirmation(false);
}
</script>

<i18n lang="json">
{
  "en": { "title": "Confirm action", "continue": "Continue", "cancel": "Cancel" },
  "sv": { "title": "Bekräfta åtgärd", "continue": "Fortsätt", "cancel": "Avbryt" }
}
</i18n>
