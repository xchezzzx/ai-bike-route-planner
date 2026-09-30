import { expect, test, type Page } from '@playwright/test';
import { PNG } from 'pngjs';
import { basemap } from './basemap';
import { route } from '../tests/fixtures';
import { t } from '../src/i18n';

async function offline(page: Page) {
  const posts: { path: string; body: any }[] = [];
  await page.route('**/*', async r => {
    const url = new URL(r.request().url());
    if (url.hostname === 'tiles.openfreemap.org') return r.fulfill({ json: basemap });
    if (url.pathname === '/health') return r.fulfill({ body: 'Healthy' });
    if (url.pathname.startsWith('/api/')) {
      const body = r.request().postDataJSON(); posts.push({ path: url.pathname, body });
      if (url.pathname.endsWith('/interpret')) {
        const intent = { start: body.start, destination: body.destination, shape: body.shape, profile: 'road', elevation: 'balanced' };
        return r.fulfill({ json: { status: 'ready', draft: intent, intent, clarifications: [], limitations: [], assumptions: [] } });
      }
      return r.fulfill({ json: route });
    }
    if (url.hostname === '127.0.0.1') return r.continue();
    throw new Error(`Unexpected external request: ${url.origin}`);
  });
  return posts;
}
for (const locale of ['en', 'ru', 'he'] as const) test(`settlement search and minimal AB prompt (${locale})`, async ({ page }, info) => {
  const posts = await offline(page); await page.goto('/');
  await page.getByLabel('Language', { exact: true }).selectOption(locale);
  await page.getByRole('combobox', { name: t(locale, 'theme'), exact: true }).selectOption(locale === 'he' ? 'dark' : 'light');
  await page.getByRole('radio', { name: t(locale, 'pointToPoint'), exact: true }).check();
  const start = page.getByRole('combobox', { name: t(locale, 'start'), exact: true });
  await start.fill('Tel Aviv'); await start.press('ArrowDown'); await start.press('Enter');
  await expect(page.getByRole('radio', { name: t(locale, 'destination'), exact: true })).toBeChecked();
  const finish = page.getByRole('combobox', { name: t(locale, 'destination'), exact: true });
  await finish.fill('ha'); await finish.press('ArrowUp');
  const active = page.getByRole('listbox').getByRole('option', { selected: true });
  const bounds = (await active.boundingBox())!; const listBounds = (await page.getByRole('listbox').boundingBox())!;
  expect(bounds.y).toBeGreaterThanOrEqual(listBounds.y);
  expect(bounds.y + bounds.height).toBeLessThanOrEqual(listBounds.y + listBounds.height);
  await finish.fill(locale === 'he' ? 'חיפה' : locale === 'ru' ? 'Хайфа' : 'Haifa');
  await page.getByRole('option', { name: /^Haifa / }).click();
  await expect(finish).toHaveValue(`${t(locale, 'nearPlace')} Haifa`);
  await expect(page.getByLabel(t(locale, 'startLatitude'), { exact: true })).not.toBeVisible();
  await page.getByLabel(t(locale, 'prompt'), { exact: true }).fill('road route');
  await page.getByRole('button', { name: t(locale, 'interpret'), exact: true }).click();
  await expect(page.getByRole('button', { name: t(locale, 'generate'), exact: true })).toBeEnabled();
  expect(posts[0].body.shape).toBe('pointToPoint'); expect(posts[0].body).not.toHaveProperty('targetDistanceMeters');
  expect(posts[0].body.start).not.toEqual(posts[0].body.destination);
  await expect(page.locator('.map-point.start')).toHaveCount(1); await expect(page.locator('.map-point.destination')).toHaveCount(1);
  await page.screenshot({ path: info.outputPath(`places-${locale}.png`), fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.getByRole('button', { name: t(locale, 'generate'), exact: true }).click();
  await expect(page.getByRole('button', { name: t(locale, 'download'), exact: true })).toBeVisible();
  await start.fill('unknown settlement xyz');
  await expect(page.getByText(t(locale, 'noSettlements'), { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: t(locale, 'generate'), exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: t(locale, 'download'), exact: true })).toHaveCount(0);
  expect(posts).toHaveLength(2);
});
for (const locale of ['en', 'ru', 'he'] as const) test(`map automatically advances and offers endpoint actions (${locale})`, async ({ page, isMobile }, info) => {
  const posts = await offline(page); await page.goto('/');
  await page.getByLabel('Language', { exact: true }).selectOption(locale);
  await page.getByRole('radio', { name: t(locale, 'pointToPoint'), exact: true }).check();
  await expect(page.locator('.map-panel')).toHaveAttribute('aria-busy', 'false');
  const canvas = page.locator('canvas'); await canvas.scrollIntoViewIfNeeded();
  const click = async (x: number, y: number) => isMobile ? canvas.tap({ position: { x, y } }) : canvas.click({ position: { x, y } });
  await click(100, 100);
  await expect(page.getByRole('radio', { name: t(locale, 'destination'), exact: true })).toBeChecked();
  const start = await page.getByLabel(t(locale, 'startLatitude'), { exact: true }).inputValue();
  await canvas.scrollIntoViewIfNeeded(); await click(190, 150);
  await expect(page.locator('.map-point.destination')).toHaveCount(1);
  await expect(page.getByLabel(t(locale, 'startLatitude'), { exact: true })).toHaveValue(start);
  const finish = await page.getByLabel(t(locale, 'destinationLatitude'), { exact: true }).inputValue();
  await click(250, 210);
  const menu = page.getByRole('menu', { name: t(locale, 'mapPointActions'), exact: true });
  await expect(menu).toBeVisible();
  await expect(page.getByLabel(t(locale, 'destinationLatitude'), { exact: true })).toHaveValue(finish);
  const box = (await menu.boundingBox())!; const map = (await page.locator('.map-panel').boundingBox())!;
  expect(box.x).toBeGreaterThanOrEqual(map.x); expect(box.x + box.width).toBeLessThanOrEqual(map.x + map.width);
  expect(box.y + box.height).toBeLessThanOrEqual(map.y + map.height);
  await page.screenshot({ path: info.outputPath(`point-menu-${locale}.png`), fullPage: true });
  await page.getByRole('menuitem', { name: t(locale, 'toHere'), exact: true }).click();
  await expect(page.getByLabel(t(locale, 'destinationLatitude'), { exact: true })).not.toHaveValue(finish);
  await expect(page.getByLabel(t(locale, 'startLatitude'), { exact: true })).toHaveValue(start);
  const changedFinish = await page.getByLabel(t(locale, 'destinationLatitude'), { exact: true }).inputValue();
  await page.getByRole('button', { name: t(locale, 'clearStart'), exact: true }).click();
  await expect(page.getByRole('radio', { name: t(locale, 'start'), exact: true })).toBeChecked();
  await canvas.scrollIntoViewIfNeeded(); await click(100, 100);
  await expect(page.locator('.map-point.start')).toHaveCount(1);
  await expect(page.getByLabel(t(locale, 'destinationLatitude'), { exact: true })).toHaveValue(changedFinish);
  await page.getByRole('radio', { name: t(locale, 'loop'), exact: true }).check();
  await canvas.scrollIntoViewIfNeeded(); await canvas.click({ button: 'right', position: { x: 220, y: 180 } });
  await page.getByRole('menuitem', { name: t(locale, 'toHere'), exact: true }).click();
  await expect(page.getByRole('radio', { name: t(locale, 'pointToPoint'), exact: true })).toBeChecked();
  expect(posts).toHaveLength(0);
  const image = PNG.sync.read(await canvas.screenshot());
  expect(new Set(Array.from({ length: image.width * image.height }, (_, i) => image.data.subarray(i * 4, i * 4 + 3).join(','))).size).toBeGreaterThan(4);
});
