import { fillField } from "./fields";
import { expect, test } from '@playwright/test';
import { basemap } from './basemap';
import { t } from '../src/i18n';
import { candidates, interpretation } from '../tests/fixtures';
import { PNG } from 'pngjs';

for (const locale of ['en', 'ru', 'he'] as const) for (const coarse of [false, true]) test(`location is opt-in and centers start (${locale}, coarse=${coarse})`, async ({ page, context }, info) => {
  await context.grantPermissions(['geolocation']);
  await context.setGeolocation({ latitude: 32.8, longitude: 35, accuracy: coarse ? 1500 : 15 });
  let calls = 0;
  await page.route('**/*', async r => {
    const url = new URL(r.request().url());
    if (url.hostname === 'tiles.openfreemap.org') return r.fulfill({ json: basemap });
    if (url.pathname === '/health') return r.fulfill({ body: 'Healthy' });
    if (url.pathname.startsWith('/api/')) { calls++; throw new Error('Location must not invoke planning'); }
    if (url.hostname === '127.0.0.1') return r.continue();
    throw new Error(`Unexpected external request: ${url.origin}`);
  });
  await page.goto('/');
  await page.getByLabel('Language', { exact: true }).selectOption(locale);
  const start = page.getByLabel(t(locale, 'startLatitude'), { exact: true });
  await expect(start).toHaveValue('');
  await page.getByRole('radio', { name: t(locale, 'pointToPoint'), exact: true }).check();
  await fillField(page, t(locale, 'destinationLatitude'), '32.2');
  await fillField(page, t(locale, 'destinationLongitude'), '34.8');
  await page.getByRole('radio', { name: t(locale, 'destination'), exact: true }).check();
  await page.getByRole('button', { name: t(locale, 'useLocation'), exact: true }).click();
  if (coarse) {
    await expect(page.getByText(t(locale, 'locationCoarse'), { exact: false })).toBeVisible();
    await expect(start).toHaveValue('');
    await page.screenshot({ path: info.outputPath(`coarse-${locale}.png`), fullPage: true });
    await page.getByRole('button', { name: t(locale, 'confirmLocation'), exact: true }).click();
  }
  await expect(start).toHaveValue('32.800000');
  await expect(page.getByLabel(t(locale, 'destinationLatitude'), { exact: true })).toHaveValue('32.2');
  await expect(page.getByText(t(locale, 'locationApplied'), { exact: false })).toBeVisible();
  const map = page.locator('.map-container');
  await map.scrollIntoViewIfNeeded();
  await expect(page.locator('.map-panel')).toHaveAttribute('aria-busy', 'false');
  await expect(async () => {
    const box = (await map.boundingBox())!;
    const marker = (await page.locator('.map-point.start').boundingBox())!;
    expect(Math.abs(marker.x + marker.width / 2 - box.x - box.width / 2)).toBeLessThan(3);
    expect(Math.abs(marker.y + marker.height / 2 - box.y - box.height / 2)).toBeLessThan(3);
  }).toPass();
  expect(calls).toBe(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: info.outputPath(`location-${locale}.png`), fullPage: true });
});

test('a deferred location focus cannot replace a subsequently generated route view', async ({ page, context }) => {
  await context.grantPermissions(['geolocation']);
  await context.setGeolocation({ latitude: 32.8, longitude: 35, accuracy: 15 });
  let release!: () => void;
  const delayed = new Promise<void>(done => { release = done; });
  const style = structuredClone(basemap);
  const water = style.sources.water;
  if (water.type !== 'geojson') throw new Error('Expected GeoJSON fixture');
  const data = water.data;
  water.data = 'http://127.0.0.1:4173/slow-water.json';
  await page.route('**/*', async r => {
    const url = new URL(r.request().url());
    if (url.pathname === '/slow-water.json') { await delayed; return r.fulfill({ json: data }); }
    if (url.hostname === 'tiles.openfreemap.org') return r.fulfill({ json: style });
    if (url.pathname === '/health') return r.fulfill({ body: 'Healthy' });
    if (url.pathname.endsWith('/interpret')) return r.fulfill({ json: interpretation });
    if (url.pathname.endsWith('/candidates')) return r.fulfill({ json: candidates });
    if (url.hostname === '127.0.0.1') return r.continue();
    throw new Error(`Unexpected external request: ${url.origin}`);
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Use my location as start', exact: true }).click();
  await expect(page.getByLabel('Start latitude', { exact: true })).toHaveValue('32.800000');
  await fillField(page, 'Ride request', 'A 25 km road loop');
  // A new input deliberately invalidates focus too; acquire again to cover preparation itself.
  await page.getByRole('button', { name: 'Use my location as start', exact: true }).click();
  await expect(page.getByText('Start updated.', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Interpret request', exact: true }).click();
  await page.getByRole('button', { name: 'Generate routes', exact: true }).click();
  await expect(page.locator('.route-option')).toHaveCount(2);
  release();
  await expect(page.locator('.map-panel')).toHaveAttribute('aria-busy', 'false');
  const canvas = page.locator('canvas');
  await canvas.scrollIntoViewIfNeeded();
  await expect(async () => {
    const png = PNG.sync.read(await canvas.screenshot());
    let routePixels = 0, left = png.width, right = 0, top = png.height, bottom = 0;
    for (let i = 0; i < png.data.length; i += 4) {
      if (Math.abs(png.data[i] - 21) < 10 && Math.abs(png.data[i + 1] - 114) < 10 && Math.abs(png.data[i + 2] - 79) < 10) {
        routePixels++;
        const x = (i / 4) % png.width, y = Math.floor(i / 4 / png.width);
        left = Math.min(left, x); right = Math.max(right, x); top = Math.min(top, y); bottom = Math.max(bottom, y);
      }
    }
    // The unknown-surface pattern is sparse; also require route-sized coverage, not a marker.
    expect(routePixels).toBeGreaterThan(100);
    expect(right - left).toBeGreaterThan(100);
    expect(bottom - top).toBeGreaterThan(80);
  }).toPass();
});
