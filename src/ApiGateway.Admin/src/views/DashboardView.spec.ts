// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createI18n } from 'vue-i18n';
import DashboardView from './DashboardView.vue';

describe('dashboardView', () => {
  afterEach(() => vi.unstubAllGlobals()); it('renders an accessible route-first overview', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => new Response(JSON.stringify({ data: { environments: [{ id: '1', slug: 'production', displayName: 'Production', activeRevisionId: '2' }], routes: [{ id: 'orders', name: 'Orders', enabled: true, operations: { state: 'ONLINE' } }], gatewayDiagnostics: { instanceCount: 2, healthyCount: 2, staleCount: 0, driftedCount: 0 }, configurationHistory: [{ id: '3', number: 3, createdBy: 'jerry', createdAtUtc: '2026-08-25T12:00:00Z', changeSummary: 'Orders route created', changedResourceType: 'Route', changedResourceId: 'orders' }], systemStatus: { version: '1.0.0', checkedAtUtc: '2026-08-25T12:00:00Z' } } }))));

    const wrapper = mount(DashboardView, { global: { plugins: [createI18n({ legacy: false, locale: 'en' })] } });

    await flushPromises(); expect(wrapper.findAll('h1')).toHaveLength(1); expect(wrapper.get('h1').text()).toBe('Overview'); expect(wrapper.text()).toContain('Gateway posture'); expect(wrapper.text()).toContain('Service health'); expect(wrapper.text()).toContain('Orders route created');

    const cards = wrapper.findAll('.posture-card');

    expect(cards).toHaveLength(4);
    expect(cards.map(card => card.text())).toEqual([
      expect.stringContaining('Routes'),
      expect.stringContaining('Online routes'),
      expect.stringContaining('Gateway instances'),
      expect.stringContaining('Needs attention'),
    ]);
    expect(wrapper.get('.dashboard-activity-table')).toBeTruthy();
  });
});
