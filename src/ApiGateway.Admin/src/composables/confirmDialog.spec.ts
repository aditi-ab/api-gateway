import { describe, expect, it } from 'vitest';
import { confirmAction, confirmDialogState, resolveConfirmation } from './confirmDialog';

describe('confirmDialog', () => {
  it('resolves the pending action from the application dialog', async () => {
    const result = confirmAction('Delete the route?', { title: 'Delete route?', confirmText: 'Delete', color: 'error' });

    expect(confirmDialogState.open).toBe(true);
    expect(confirmDialogState.title).toBe('Delete route?');
    resolveConfirmation(true);
    await expect(result).resolves.toBe(true);
    expect(confirmDialogState.open).toBe(false);
  });
});
