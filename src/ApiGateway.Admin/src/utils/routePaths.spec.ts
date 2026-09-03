import { describe, expect, it } from 'vitest';
import { buildRoutePath, parseRoutePath } from './routePaths';

describe('route paths', () => {
  it('adds a terminal catch-all when subpath matching is enabled', () => {
    expect(buildRoutePath('/', true)).toBe('/{**remainder}');
    expect(buildRoutePath('/api', true)).toBe('/api/{**remainder}');
    expect(buildRoutePath('/api/', true)).toBe('/api/{**remainder}');
  });

  it('keeps an exact path when subpath matching is disabled', () => {
    expect(buildRoutePath('/', false)).toBe('/');
    expect(buildRoutePath('/api', false)).toBe('/api');
  });

  it('recognizes terminal catch-all parameters regardless of their name', () => {
    expect(parseRoutePath('/{**remainder}')).toEqual({ path: '/', matchSubpaths: true });
    expect(parseRoutePath('/api/{**path}')).toEqual({ path: '/api', matchSubpaths: true });
    expect(parseRoutePath('/api')).toEqual({ path: '/api', matchSubpaths: false });
  });
});
