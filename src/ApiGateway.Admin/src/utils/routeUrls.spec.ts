import { describe, expect, it } from 'vitest';
import { routeTestUrls } from './routeUrls';

describe('route test URLs', () => {
  it('creates both HTTP and HTTPS links and removes catch-all placeholders', () => {
    expect(routeTestUrls({
      inbound: { scheme: 'ANY' },
      match: { hosts: ['api.example.com', '*.example.com'], path: '/orders/{**remainder}', methods: [] },
    })).toEqual([
      'http://api.example.com/orders/',
      'https://api.example.com/orders/',
    ]);
  });

  it('honors scheme restrictions and returns no guessed URL without a host', () => {
    expect(routeTestUrls({
      inbound: { scheme: 'HTTPS_REDIRECT' },
      match: { hosts: ['api.example.com'], path: '/orders', methods: [] },
    })).toEqual(['https://api.example.com/orders']);
    expect(routeTestUrls({
      inbound: { scheme: 'HTTP_ONLY' },
      match: { hosts: [], path: '/orders', methods: [] },
    })).toEqual([]);
  });
});
