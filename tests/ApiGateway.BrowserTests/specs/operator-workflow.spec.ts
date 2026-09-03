import AxeBuilder from '@axe-core/playwright';
import type { Locator, Page } from '@playwright/test';
import { expect, test } from '@playwright/test';

async function chooseOption(control: Locator, page: Page, option: string) {
  await control.click();
  const listbox = page.locator('[role="listbox"]:visible').last();
  await expect(listbox).toBeVisible();
  const controlBox = await control.boundingBox();
  const listboxBox = await listbox.boundingBox();
  expect(Math.abs(listboxBox!.width - controlBox!.width)).toBeLessThanOrEqual(2);
  await page.getByRole('option', { name: option, exact: true }).click();
}

test('administrator creates a live route, adds a feature, and reverts a change', async ({ page }) => {
  const relevantWarnings: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'warning' && /\[Vue warn\]|\[intlify\]/.test(message.text()))
      relevantWarnings.push(message.text());
  });
  const suffix = Date.now().toString();
  const environmentName = `Browser ${suffix}`;
  const slug = `browser-${suffix}`;

  const authenticationStatus = page.waitForResponse(response => response.url().endsWith('/admin/auth/status') && response.request().method() === 'GET');

  await page.goto('/admin/');
  const initialAuthentication = await (await authenticationStatus).json() as { bootstrapRequired: boolean };
  await expect(page.locator('link[rel="icon"]')).toHaveAttribute('href', 'api-gateway.svg');
  expect((await page.request.get('/admin/api-gateway.svg')).ok()).toBeTruthy();
  await page.getByLabel('Username').fill('browser-admin');
  await page.getByLabel('Password', { exact: true }).fill('Browser-only-password-42!');
  const confirmation = page.getByLabel('Confirm password');
  const authenticationButton = page.getByRole('button', { name: /Create administrator|Sign in/ });

  await expect(authenticationButton).toBeVisible();

  if (initialAuthentication.bootstrapRequired)
    await confirmation.fill('Browser-only-password-42!');

  await authenticationButton.click();
  await expect(page.getByRole('heading', { name: 'Overview' })).toBeVisible();
  expect(await page.locator('.route-view').evaluate(element => getComputedStyle(element).transitionDuration)).toContain('0.2s');
  const documentationButton = page.getByRole('link', { name: 'Documentation' });
  const documentationMetrics = await documentationButton.evaluate((element) => {
    const style = getComputedStyle(element);
    return { height: element.getBoundingClientRect().height, fontSize: style.fontSize, fontWeight: style.fontWeight };
  });
  expect(documentationMetrics).toEqual({ height: 36, fontSize: '14px', fontWeight: '600' });
  expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);

  await page.getByRole('button', { name: 'System' }).click();
  await page.getByRole('link', { name: 'Settings' }).click();
  const inboundSecurityTab = page.getByRole('tab', { name: 'Inbound security' });
  expect(await inboundSecurityTab.evaluate(element => getComputedStyle(element).transitionDuration)).toContain('0.15s');
  await inboundSecurityTab.click();
  expect(await page.locator('.aui-window-item:visible').evaluate(element => getComputedStyle(element).animationName)).toBe('aui-content-in');
  const protectedHostnames = page.getByRole('combobox', { name: 'Protected hostnames' });
  const maxAge = page.getByRole('spinbutton', { name: 'Max age (seconds)' });
  await expect(protectedHostnames).toBeVisible();
  expect(await protectedHostnames.evaluate(element => element.closest('.aui-combobox')!.getBoundingClientRect().height)).toBe(36);
  expect(await maxAge.evaluate(element => element.getBoundingClientRect().height)).toBe(36);
  expect(await page.getByRole('switch', { name: 'Enable HSTS' }).evaluate(element => getComputedStyle(element).cursor)).toBe('pointer');

  await page.getByRole('button', { name: 'System' }).click();
  await page.getByRole('link', { name: 'Instances' }).click();
  const instancesToolbar = page.locator('.instances-toolbar');
  const instancesEnvironment = instancesToolbar.getByRole('combobox', { name: 'Environment' });
  const refreshButton = instancesToolbar.getByRole('button', { name: 'Refresh' });
  await expect(page.getByRole('heading', { name: 'Instances and diagnostics' })).toBeVisible();
  expect(await instancesEnvironment.evaluate(element => element.closest('.aui-field')!.getBoundingClientRect().width)).toBe(280);
  const refreshBackground = await refreshButton.evaluate(element => getComputedStyle(element).backgroundColor);
  await refreshButton.hover();
  await expect.poll(() => refreshButton.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe(refreshBackground);
  await page.mouse.down();
  expect(await refreshButton.evaluate(element => getComputedStyle(element).transform)).toBe('none');
  await page.mouse.up();

  await page.getByRole('button', { name: 'System' }).click();
  await page.getByRole('link', { name: 'Environments' }).click();
  await page.getByRole('button', { name: 'Create environment' }).click();
  await page.getByLabel('Display name').fill(environmentName);
  await expect(page.getByLabel('Slug')).toHaveValue(slug);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('row', { name: new RegExp(environmentName) })).toBeVisible();

  await page.getByRole('link', { name: 'Routes', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Environment' })).toContainText(environmentName);
  await page.getByRole('button', { name: 'Add route' }).click();
  expect(await page.getByRole('dialog').evaluate(element => getComputedStyle(element).transitionDuration)).toContain('0.2s');
  await page.getByLabel('Name').fill('Orders API');
  await page.getByLabel('Incoming path').fill('/orders/{**remainder}');
  await page.getByLabel('Upstream URL').fill('https://orders.example/');
  await page.getByRole('button', { name: 'Create and activate' }).click();
  await expect(page.getByRole('heading', { name: 'Orders API' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Online', exact: true })).toHaveClass(/aui-button--success-tonal/);
  await expect(page.getByRole('button', { name: 'Enabled', exact: true })).toHaveClass(/aui-button--success-tonal/);
  const onlineButton = page.getByRole('button', { name: 'Online', exact: true });
  const onlineBackground = await onlineButton.evaluate(element => getComputedStyle(element).backgroundColor);
  await onlineButton.hover();
  await expect.poll(() => onlineButton.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe(onlineBackground);

  const upstreamUrl = page.getByLabel('Upstream URL');
  await upstreamUrl.focus();
  await expect.poll(() => upstreamUrl.evaluate(element => getComputedStyle(element).boxShadow)).toContain('rgb(99, 102, 241)');

  const incomingHosts = page.getByRole('combobox', { name: 'Incoming hosts' });

  await incomingHosts.fill('api.example.com');
  await incomingHosts.press('Enter');
  await expect(incomingHosts).toHaveValue('');
  await incomingHosts.fill('*.api.example.com');
  await incomingHosts.press('Enter');
  await expect(incomingHosts).toHaveValue('');
  await expect(page.getByRole('button', { name: 'api.example.com', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: '*.api.example.com', exact: true })).toBeVisible();
  const pathHandling = page.getByRole('combobox', { name: 'Upstream path handling' });

  await chooseOption(pathHandling, page, 'Remove path prefix');
  await expect(page.getByLabel('Path prefix to remove')).toHaveValue('/orders');
  await page.getByRole('button', { name: 'Advanced matching' }).click();
  await page.getByRole('spinbutton', { name: 'Precedence' }).fill('10');
  await page.getByLabel('Allowed methods').fill('GET, HEAD');
  await page.getByRole('button', { name: 'Add header' }).click();
  await page.getByLabel('Header name').fill('X-Tenant');
  await page.getByLabel('Value').first().fill('north');
  await page.getByRole('button', { name: 'Add query parameter' }).click();
  await page.getByLabel('Parameter name').fill('preview');
  await chooseOption(page.getByRole('combobox', { name: 'Match' }).last(), page, 'Contains');
  await page.getByLabel('Value').last().fill('true');
  await page.getByRole('button', { name: 'Advanced upstream' }).click();
  const http2Switch = page.getByRole('switch', { name: 'Allow multiple HTTP/2 connections' });
  const http2Thumb = http2Switch.locator('.aui-switch__thumb');
  const checkedBefore = await http2Switch.getAttribute('aria-checked');
  const thumbBefore = await http2Thumb.evaluate(element => getComputedStyle(element).transform);
  await http2Switch.click();
  await expect(http2Switch).toHaveAttribute('aria-checked', checkedBefore === 'true' ? 'false' : 'true');
  await expect.poll(() => http2Thumb.evaluate(element => getComputedStyle(element).transform)).not.toBe(thumbBefore);
  await http2Switch.click();
  await expect(http2Switch).toHaveAttribute('aria-checked', checkedBefore!);
  await page.getByRole('combobox', { name: 'Load balancing' }).focus();
  await page.getByRole('combobox', { name: 'Load balancing' }).press('ArrowDown');
  await page.getByRole('combobox', { name: 'Load balancing' }).press('Enter');
  await page.getByRole('button', { name: 'Add destination' }).click();
  await page.getByLabel('Destination name').fill('secondary');
  await page.getByLabel('Destination URL').fill('https://orders-secondary.example/');
  await page.getByRole('button', { name: 'Save and activate' }).first().click();
  await expect(page.getByText('Route saved and activated.')).toBeVisible();
  const routeSummary = page.getByLabel('Route summary');
  await expect(routeSummary).toContainText('api.example.com');
  await expect(routeSummary).toContainText('https://orders-secondary.example/');
  await expect(routeSummary.getByRole('link', { name: 'http://api.example.com/orders/', exact: true })).toHaveAttribute('target', '_blank');
  await expect(routeSummary.getByRole('link', { name: 'https://orders-secondary.example/', exact: true })).toHaveAttribute('target', '_blank');

  await page.getByRole('button', { name: 'Online', exact: true }).click();
  const stateDialog = page.getByRole('dialog', { name: 'Route traffic state' });

  await chooseOption(stateDialog.getByRole('combobox', { name: 'Traffic state' }), page, 'Draining');
  await expect(stateDialog.getByRole('combobox', { name: 'Unavailable response' })).toContainText('Use environment default');
  await stateDialog.getByRole('button', { name: 'Save and activate' }).click();
  await expect(page.getByText('Route traffic state saved and activated.')).toBeVisible();
  await expect(page.getByText(/new requests do not use/i)).toBeVisible();
  await page.getByRole('button', { name: 'Draining', exact: true }).click();
  await chooseOption(page.getByRole('dialog', { name: 'Route traffic state' }).getByRole('combobox', { name: 'Traffic state' }), page, 'Online');
  await page.getByRole('dialog', { name: 'Route traffic state' }).getByRole('button', { name: 'Save and activate' }).click();

  await page.getByRole('link', { name: 'Routes', exact: true }).click();
  await page.getByRole('switch', { name: 'Disable route Orders API' }).click();
  await expect(page.getByRole('switch', { name: 'Enable route Orders API' })).toBeVisible();
  await page.getByRole('switch', { name: 'Enable route Orders API' }).click();
  await expect(page.getByRole('switch', { name: 'Disable route Orders API' })).toBeVisible();
  const routeDetailsButton = page.getByRole('button', { name: 'Show traffic details for Orders API' });
  const routeDetailsIcon = routeDetailsButton.locator('.aui-table__expand-icon');
  expect(await routeDetailsIcon.evaluate(element => getComputedStyle(element).transitionDuration)).toContain('0.2s');
  const routeDetailsId = await routeDetailsButton.getAttribute('aria-controls');
  const routeDetailsCell = page.locator(`#${routeDetailsId} > td`);
  const routeRow = page.locator('.routes-table tr.click-row').filter({ hasText: 'Orders API' });
  await expect(routeRow.locator('td').first()).toHaveText('Orders API');
  await expect(routeDetailsCell).toHaveCSS('border-top-width', '0px');
  await routeDetailsButton.click();
  const expandedRouteDetailsButton = page.locator(`button[aria-controls="${routeDetailsId}"]`);
  await expect(expandedRouteDetailsButton).toHaveAttribute('aria-expanded', 'true');
  await expect(page.locator(`#${routeDetailsId}`)).toHaveAttribute('data-state', 'open');
  await expect(routeDetailsCell).toHaveCSS('border-top-width', '1px');
  await expect(expandedRouteDetailsButton.locator('.aui-table__expand-icon')).toHaveCSS('transform', 'matrix(-1, 0, 0, -1, 0, 0)');
  const expandedTrafficFlow = page.locator(`#${routeDetailsId}`);
  const incomingTestUrl = expandedTrafficFlow.getByRole('link', { name: 'http://api.example.com/orders/', exact: true });
  await expect(incomingTestUrl).toHaveAttribute('href', 'http://api.example.com/orders/');
  await expect(incomingTestUrl).toHaveAttribute('target', '_blank');
  await expect(expandedTrafficFlow.getByRole('link', { name: 'https://orders.example/', exact: true })).toHaveAttribute('target', '_blank');
  await routeRow.locator('td').first().click();
  await expect(page.locator(`#${routeDetailsId}`)).toHaveAttribute('data-state', 'closed');
  await routeRow.locator('td').first().click();
  await expect(page.locator(`#${routeDetailsId}`)).toHaveAttribute('data-state', 'open');
  const routeEditButton = routeRow.getByRole('button', { name: 'Edit' });
  await expect(routeEditButton).toHaveClass(/aui-button--context/);
  await expect(routeEditButton).toHaveClass(/aui-button--tonal/);
  await expect(routeEditButton).toHaveClass(/aui-button--small/);
  await routeEditButton.click();

  await page.getByRole('button', { name: 'Add feature' }).click();
  await page.getByRole('button', { name: /Rate limiting/ }).click();
  await page.setViewportSize({ width: 390, height: 320 });
  const featureDialogBody = page.getByRole('dialog').locator('.aui-card__body');
  await expect.poll(() => featureDialogBody.evaluate(element => element.scrollHeight > element.clientHeight)).toBe(true);
  await featureDialogBody.evaluate((element) => { element.scrollTop = 100; });
  await expect.poll(() => featureDialogBody.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  await page.getByLabel('Request limit').fill('25');
  await page.getByRole('dialog').getByRole('button', { name: 'Save and activate' }).click();
  await page.setViewportSize({ width: 1280, height: 720 });
  await expect(page.getByText('Feature settings saved and activated.')).toBeVisible();

  await page.getByRole('link', { name: 'Activity' }).click();
  await expect(page.getByText('Updated features for route Orders API')).toBeVisible();
  await expect(page.locator('.activity-card').first()).toContainText(/\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}/);
  expect(await page.locator('.activity-card .aui-card__item-title').first().evaluate(element => getComputedStyle(element).fontWeight)).toBe('600');
  expect((await page.locator('.activity-card').first().boundingBox())!.width).toBeGreaterThan(600);
  await page.getByRole('button', { name: 'Advanced details' }).first().click();
  const activityDetails = page.locator('.aui-collapsible__content[data-selected]').first();
  expect(await activityDetails.evaluate(element => getComputedStyle(element).transitionDuration)).toContain('0.2s');
  await expect(activityDetails).not.toHaveCSS('grid-template-rows', '0px');
  const timelineOffset = await page.locator('.activity-marker').first().evaluate((marker) => {
    const list = marker.closest('.activity-list')!;
    const listRect = list.getBoundingClientRect();
    const lineStyle = getComputedStyle(list, '::before');
    const lineCenter = listRect.left + Number.parseFloat(lineStyle.left) + Number.parseFloat(lineStyle.width) / 2;
    const markerRect = marker.getBoundingClientRect();
    return Math.abs(lineCenter - (markerRect.left + markerRect.width / 2));
  });
  expect(timelineOffset).toBeLessThan(0.1);
  const createEntry = page.locator('.activity-card').filter({ hasText: 'Created route Orders API' });

  await createEntry.getByRole('button', { name: 'Revert' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Revert' }).click();
  await expect(page.getByText(/reverted|conflict/i)).toBeVisible();

  await page.getByRole('button', { name: 'Language' }).click();
  await page.getByText('Swedish', { exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Aktivitet' })).toBeVisible();
  await page.getByRole('button', { name: 'Språk' }).click();
  await page.getByText('Engelska', { exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Activity' })).toBeVisible();

  await page.getByRole('button', { name: 'Access' }).click();
  await page.getByRole('link', { name: 'Local users' }).click();
  await expect(page.getByRole('heading', { name: 'Local users' })).toBeVisible();
  expect(await page.locator('.route-view > .aui-container').evaluate(element => getComputedStyle(element).paddingTop)).toBe('24px');
  await expect(page.locator('.aui-table-actions').first().getByRole('button', { name: 'Edit' })).toHaveClass(/aui-button--context/);
  await expect(page.locator('.aui-table-actions').first().locator('.aui-button--icon')).toHaveClass(/aui-button--context/);

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole('link', { name: 'Routes', exact: true })).toBeVisible();
  await page.emulateMedia({ reducedMotion: 'reduce' });
  expect(await page.locator('.route-view').evaluate(element => Number.parseFloat(getComputedStyle(element).transitionDuration))).toBeLessThanOrEqual(0.001);
  const reducedDuration = await page.getByRole('link', { name: 'Routes', exact: true }).evaluate(element => Number.parseFloat(getComputedStyle(element).transitionDuration));
  expect(reducedDuration).toBeLessThanOrEqual(0.001);
  expect(relevantWarnings).toEqual([]);
});
