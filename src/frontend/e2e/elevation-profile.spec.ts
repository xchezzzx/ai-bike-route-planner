import { expect, test, type Page } from '@playwright/test';
import { PNG } from 'pngjs';
import { basemap } from './basemap';
import { fillField } from './fields';
import { candidates, interpretation, route } from '../tests/fixtures';
import { quantity, t } from '../src/i18n';
import type { GeneratedRoute, Locale } from '../src/types';

const elevated: GeneratedRoute = { ...route, ascentMeters: 180, descentMeters: 150,
  geometry: Array.from({ length: 21 }, (_, index) => ({ latitude: 32.08 + .025 * Math.sin(index * Math.PI / 10),
    longitude: 34.78 + .025 * Math.cos(index * Math.PI / 10), elevationMeters: index >= 8 && index <= 10 ? null : index === 0 ? -15 : index === 1 ? 0 : index * 5 })),
};
async function generate(page: Page, locale: Locale = 'en', fixture = elevated) {
  const requests: string[] = [];
  await page.route('**/*', async intercepted => {
    const url = new URL(intercepted.request().url());
    if (url.hostname === 'tiles.openfreemap.org') return intercepted.fulfill({ json: basemap });
    if (url.pathname === '/health') return intercepted.fulfill({ body: 'Healthy' });
    if (url.pathname.startsWith('/api/')) {
      requests.push(url.pathname);
      return intercepted.fulfill({ json: url.pathname.endsWith('/interpret') ? interpretation : {
        ...candidates, candidates: [{ ...candidates.candidates[0], route: fixture }, candidates.candidates[1]],
      } });
    }
    if (url.hostname === '127.0.0.1') return intercepted.continue();
    throw new Error(`Unexpected external request: ${url.origin}`);
  });
  await page.goto('/');
  await page.getByLabel('Language', { exact: true }).selectOption(locale);
  await fillField(page, t(locale, 'startLatitude'), '32.08');
  await fillField(page, t(locale, 'startLongitude'), '34.78');
  await fillField(page, t(locale, 'prompt'), 'A 25 km road loop');
  await page.getByRole('button', { name: t(locale, 'interpret'), exact: true }).click();
  await page.getByRole('button', { name: t(locale, 'generate'), exact: true }).click();
  await expect(page.locator('.route-option')).toHaveCount(2);
  return requests;
}
function coloredPixels(png: PNG) {
  let count = 0;
  for (let index = 0; index < png.data.length; index += 4) {
    if (Math.abs(png.data[index] - 21) < 12 && Math.abs(png.data[index + 1] - 114) < 12 && Math.abs(png.data[index + 2] - 79) < 12) count++;
  }
  return count;
}
for (const locale of ['en', 'ru', 'he'] as const) test(`elevation profile, compact sidebar and linked inspection (${locale})`, async ({ page, isMobile }, info) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  const requests = await generate(page, locale);
  const profile = page.getByRole('region', { name: t(locale, 'elevationProfile'), exact: true });
  const canvas = profile.locator('canvas');
  await canvas.scrollIntoViewIfNeeded();
  await expect(async () => expect(coloredPixels(PNG.sync.read(await canvas.screenshot()))).toBeGreaterThan(100)).toPass();
  await expect(profile.getByText(t(locale, 'elevationPartial'), { exact: true })).toBeVisible();
  await expect(profile.getByText(quantity(locale, -15, 'm'), { exact: true })).toBeVisible();
  expect(await page.locator('.elevation-profile').evaluate(element => element.previousElementSibling?.classList.contains('map-panel'))).toBe(true);
  expect(await page.locator('.route-options').evaluate(element => Boolean(element.closest('aside')))).toBe(true);
  const sidebar = (await page.locator('.controls').boundingBox())!;
  const map = (await page.locator('.map-panel').boundingBox())!;
  if (isMobile) expect(map.y).toBeGreaterThan(sidebar.y + sidebar.height - 1);
  else expect(sidebar.x).toBeGreaterThanOrEqual(map.x + map.width - 1);
  for (const option of await page.locator('.route-option').all()) await expect(option.getByRole('img')).toHaveCount(3);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);

  const box = (await canvas.boundingBox())!;
  if (isMobile) await page.touchscreen.tap(box.x + box.width * .75, box.y + box.height / 2);
  else await page.mouse.move(box.x + box.width * .75, box.y + box.height / 2);
  await expect(page.locator('.elevation-point')).toHaveCount(1);
  const slider = profile.getByRole('slider', { name: t(locale, 'trackPoint'), exact: true });
  await slider.focus();
  await page.keyboard.press('Home');
  await expect(slider).toHaveAttribute('aria-valuetext', expect.stringContaining('-15'));
  await page.keyboard.press('ArrowRight');
  await expect(slider).toHaveValue('1');
  await expect(slider).toHaveAttribute('aria-valuetext', expect.stringContaining('0'));
  await page.keyboard.press('End');
  await expect(slider).toHaveValue('20');
  await expect(page.locator('.elevation-point')).toHaveCount(1);
  await page.screenshot({ path: info.outputPath(`elevation-light-${locale}.png`), fullPage: true });
  await page.getByRole('combobox', { name: t(locale, 'theme'), exact: true }).selectOption('dark');
  await canvas.scrollIntoViewIfNeeded();
  await expect(async () => expect(coloredPixels(PNG.sync.read(await canvas.screenshot()))).toBeGreaterThan(100)).toPass();
  await expect(slider).toHaveValue('20');
  await expect(page.locator('.elevation-point')).toHaveCount(1);
  await page.screenshot({ path: info.outputPath(`elevation-dark-${locale}.png`), fullPage: true });
  await page.locator('.route-option input').nth(1).check();
  await expect(page.locator('.elevation-point')).toHaveCount(0);
  await expect(slider).toHaveValue('0');
  await slider.focus();
  await page.keyboard.press('End');
  await expect(page.locator('.elevation-point')).toHaveCount(1);
  await expect(slider).toHaveAttribute('aria-valuetext', expect.stringContaining(t(locale, 'unknown')));
  await page.keyboard.press('Escape');
  await expect(page.locator('.elevation-point')).toHaveCount(0);
  expect(requests).toHaveLength(2);
  await page.getByRole('button', { name: t(locale, 'clearStart'), exact: true }).click();
  await expect(profile).toHaveCount(0);
  await expect(page.locator('.elevation-point')).toHaveCount(0);
  expect(errors).toEqual([]);
});
test('keyboard inspection survives viewport leave and reentry until deliberate pointer input', async ({ page }) => {
  await generate(page, 'ru');
  const canvas = page.locator('.elevation-chart canvas');
  await canvas.scrollIntoViewIfNeeded();
  const box = (await canvas.boundingBox())!;
  await page.mouse.move(box.x + box.width * .75, box.y + box.height / 2);
  await expect(page.locator('.elevation-point')).toHaveCount(1);
  const slider = page.locator('.elevation-position');
  await slider.focus();
  await page.keyboard.press('End');
  await expect(slider).toHaveValue('20');
  await page.setViewportSize({ width: 1440, height: 600 });
  await expect(slider).toHaveValue('20');
  await expect(page.locator('.elevation-point')).toHaveCount(1);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await canvas.scrollIntoViewIfNeeded();
  await canvas.screenshot();
  await expect(slider).toHaveValue('20');
  const restored = (await canvas.boundingBox())!;
  await page.mouse.move(restored.x + restored.width * .25, restored.y + restored.height / 2);
  await expect(slider).not.toHaveValue('20');
  await expect(page.locator('.elevation-point')).toHaveCount(1);
});
test('missing elevations retain route switching and GPX', async ({ page }) => {
  const requests = await generate(page, 'en', route);
  const profile = page.getByRole('region', { name: 'Elevation profile', exact: true });
  await expect(profile.getByText('Elevation data is unavailable.', { exact: true })).toBeVisible();
  await expect(profile.locator('canvas')).toHaveCount(0);
  await expect(profile.getByRole('slider')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Download GPX', exact: true })).toBeEnabled();
  await page.locator('.route-option input').nth(1).check();
  await expect(profile.locator('canvas')).toHaveCount(1);
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download GPX', exact: true }).click();
  expect((await download).suggestedFilename()).toMatch(/\.gpx$/);
  expect(requests).toHaveLength(2);
});
test('large profiles remain interactive with exact endpoint inspection', async ({ page }) => {
  const dense = { ...elevated, geometry: Array.from({ length: 50000 }, (_, index) => ({ latitude: 32.08 + .01 * Math.sin(index / 1000),
    longitude: 34.78 + index / 5000000, elevationMeters: index % 5000 < 20 ? null : Math.sin(index / 50) * 100 })) };
  dense.geometry[dense.geometry.length - 1] = { ...dense.geometry[dense.geometry.length - 1], latitude: dense.geometry[0].latitude, longitude: dense.geometry[0].longitude };
  await generate(page, 'en', dense);
  const slider = page.getByRole('slider', { name: 'Track point', exact: true });
  await slider.focus();
  await page.keyboard.press('End');
  await expect(slider).toHaveValue('49999');
  await expect(page.locator('.elevation-point')).toHaveCount(1);
  await page.keyboard.press('Escape');
  await expect(page.locator('.elevation-point')).toHaveCount(0);
});
