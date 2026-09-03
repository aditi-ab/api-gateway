import { describe, expect, it } from 'vitest';
import { isHeaderTransform, isPathTransform, preservesOriginalHost, removeTransforms, replaceFirstTransform, transformValue } from './routeTransforms';

describe('route transforms', () => {
  const header = [{ key: 'RequestHeader', value: 'Host' }, { key: 'Set', value: 'example.com' }];
  const path = [{ key: 'PathRemovePrefix', value: '/api' }];
  const preserveHost = [{ key: 'RequestHeaderOriginalHost', value: 'true' }];

  it('classifies and reads transforms case-insensitively', () => {
    expect(isHeaderTransform(header)).toBe(true);
    expect(isPathTransform(path)).toBe(true);
    expect(transformValue(header, 'requestheader')).toBe('Host');
    expect(preservesOriginalHost([preserveHost])).toBe(true);
  });

  it('replaces only the first matching transform and preserves unrelated transforms', () => {
    const replacement = [{ key: 'RequestHeader', value: 'X-Tenant' }, { key: 'Set', value: 'aditi' }];

    expect(replaceFirstTransform([path, header, preserveHost], isHeaderTransform, replacement))
      .toEqual([path, replacement, preserveHost]);
  });

  it('removes only transforms selected by the predicate', () => {
    expect(removeTransforms([path, header, preserveHost], isHeaderTransform)).toEqual([path, preserveHost]);
  });
});
