import { describe, expect, it } from 'vitest';
import { formatDateTime } from './dateTime';

describe('formatDateTime', () => {
  it('uses the shared ISO-style display format', () => {
    expect(formatDateTime(new Date(2026, 7, 23, 15, 4, 8))).toBe('2026-08-23 15:04:08');
  });
});
