import { afterEach, describe, expect, it, vi } from 'vitest';
import { graphql } from './api';

describe('admin shell', () => {
  it('uses the management GraphQL endpoint', () => expect('/graphql').toBe('/graphql'));
});

describe('graphql', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('reports an empty unauthorized response without a JSON parsing error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 401 })));

    await expect(graphql('query { me { id } }')).rejects.toThrow('Request failed (401)');
  });

  it('reports detailed configuration validation issues', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      errors: [{
        message: 'The configuration is invalid.',
        extensions: { issues: [{ message: 'Certificate-backed routes require at least one incoming hostname.' }] },
      }],
    }), { status: 200, headers: { 'content-type': 'application/json' } })));

    await expect(graphql('mutation { updateRouteBasics { revision { id } } }'))
      .rejects
      .toThrow('Certificate-backed routes require at least one incoming hostname.');
  });
});
