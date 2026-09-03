import { reactive } from 'vue';

export interface ConfirmDialogOptions {
  title?: string;
  confirmText?: string;
  cancelText?: string;
  color?: string;
}

export const confirmDialogState = reactive({
  open: false,
  title: '',
  message: '',
  confirmText: '',
  cancelText: '',
  color: 'primary',
});

let resolver: ((confirmed: boolean) => void) | undefined;

export function confirmAction(message: string, options: ConfirmDialogOptions = {}) {
  resolver?.(false);
  confirmDialogState.title = options.title || '';
  confirmDialogState.message = message;
  confirmDialogState.confirmText = options.confirmText || '';
  confirmDialogState.cancelText = options.cancelText || '';
  confirmDialogState.color = options.color || 'primary';
  confirmDialogState.open = true;

  return new Promise<boolean>((resolve) => {
    resolver = resolve;
  });
}

export function resolveConfirmation(confirmed: boolean) {
  if (!confirmDialogState.open && !resolver)
    return;

  confirmDialogState.open = false;

  const currentResolver = resolver;

  resolver = undefined;
  currentResolver?.(confirmed);
}
