import { fillField } from "./fields";
import { expect, test, type Page } from '@playwright/test';
import { PNG } from 'pngjs';
import { readFile } from 'node:fs/promises';
import type { StyleSpecification } from 'maplibre-gl';
import { basemap } from './basemap';
import { candidates, interpretation } from '../tests/fixtures';

const dark: StyleSpecification = { ...basemap, layers: basemap.layers.map(layer => layer.type === 'background'
  ? { ...layer, paint: { 'background-color': '#17191b' } } : layer.type === 'fill'
    ? { ...layer, paint: { 'fill-color': '#28343a' } } : layer.type === 'line' ? { ...layer, paint: { 'line-color': '#47514d', 'line-width': 5 } } : layer) };

async function setup(page: Page) {
  const styles: string[] = []; let calls = 0; let failDark = false;
  await page.route('**/*', async r => {
    const url = new URL(r.request().url());
    if (url.hostname === 'tiles.openfreemap.org') {
      styles.push(url.pathname);
      if (url.pathname.endsWith('/dark') && failDark) return r.abort();
      return r.fulfill({ json: url.pathname.endsWith('/dark') ? dark : basemap });
    }
    if (url.pathname === '/health') return r.fulfill({ body: 'Healthy' });
    if (url.pathname.startsWith('/api/')) {
      calls++;
      return r.fulfill({ json: url.pathname.endsWith('/interpret') ? interpretation : candidates });
    }
    if (url.hostname === '127.0.0.1') return r.continue();
    throw new Error(`Unexpected external request: ${url.origin}`);
  });
  return { styles, calls: () => calls, fail: (value: boolean) => { failDark = value; } };
}

function greenPixels(png: PNG) {
  const pixels = new Set<number>();
  for (let i = 0; i < png.data.length; i += 4) {
    if (Math.abs(png.data[i] - 21) < 10 && Math.abs(png.data[i + 1] - 114) < 10 && Math.abs(png.data[i + 2] - 79) < 10) pixels.add(i / 4);
  }
  return pixels;
}

for (const locale of ['en', 'ru', 'he'] as const) test(`theme preserves route, camera and segment selection (${locale})`, async ({ page }, info) => {
  const network = await setup(page);
  await page.goto('/');
  await page.getByLabel('Language', { exact: true }).selectOption(locale);
  const labels = { en: ['Theme', 'Start latitude', 'Start longitude', 'Ride request', 'Interpret request', 'Generate routes', 'Track segments', 'Segment', 'Download GPX', 'Zoom in', 'Segment details'],
    ru: ['Тема', 'Широта старта', 'Долгота старта', 'Запрос на поездку', 'Разобрать запрос', 'Построить маршруты', 'Участки трека', 'Участок', 'Скачать GPX', 'Приблизить', 'Сведения об участке'],
    he: ['ערכת נושא', 'קו רוחב של התחלה', 'קו אורך של התחלה', 'בקשת רכיבה', 'פירוש הבקשה', 'יצירת מסלולים', 'מקטעי המסלול', 'מקטע', 'הורדת GPX', 'הגדלה', 'פרטי המקטע'] }[locale];
  const theme = page.getByRole('combobox', { name: labels[0], exact: true });
  await expect(theme).toHaveValue('system');
  await fillField(page, labels[1], '32.08');
  await fillField(page, labels[2], '34.78');
  await fillField(page, labels[3], 'A 25 km road loop');
  await page.getByRole('button', { name: labels[4], exact: true }).click();
  await page.getByRole('button', { name: labels[5], exact: true }).click();
  await page.getByText(labels[6], { exact: true }).click();
  await page.getByRole('combobox', { name: labels[7], exact: true }).selectOption('0');
  const canvas = page.locator('canvas');
  await canvas.scrollIntoViewIfNeeded();
  let before!: Set<number>;
  await expect(async () => { before = greenPixels(PNG.sync.read(await canvas.screenshot())); expect(before.size).toBeGreaterThan(50); }).toPass();
  // Move the camera so an accidental fitBounds on theme change is observable.
  const box = (await canvas.boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down(); await page.mouse.move(box.x + box.width / 2 + 35, box.y + box.height / 2 + 25, { steps: 15 }); await page.mouse.up();
  await expect(async () => { before = greenPixels(PNG.sync.read(await canvas.screenshot())); expect(before.size).toBeGreaterThan(50); }).toPass();
  const calls = network.calls();
  for (const value of ['dark', 'light', 'dark']) {
    await theme.selectOption(value);
    await expect(page.locator('html')).toHaveAttribute('data-theme', value);
    await expect.poll(() => network.styles.at(-1)).toBe(`/styles/${value === 'dark' ? 'dark' : 'liberty'}`);
    await expect(page.locator('.map-panel')).toHaveAttribute('aria-busy', 'false');
    await expect(async () => {
      const after = greenPixels(PNG.sync.read(await canvas.screenshot()));
      expect([...before].filter(pixel => after.has(pixel)).length / before.size).toBeGreaterThan(.8);
    }).toPass();
    await expect(page.locator('.segment-detail')).toBeVisible();
    await expect(page.getByRole('combobox', { name: labels[7], exact: true })).toHaveValue('0');
    await expect(page.getByRole('textbox', { name: labels[3], exact: true })).toHaveValue('A 25 km road loop');
    await expect(page.getByLabel(labels[1], { exact: true })).toHaveValue('32.08');
    expect(network.calls()).toBe(calls);
  }
  await expect(page.locator('body')).toHaveCSS('color', 'rgb(237, 241, 242)');
  const contrasts = await page.evaluate(() => {
    const rgb = (value: string) => value.match(/[\d.]+/g)!.map(Number);
    const luminance = (color: number[]) => color.slice(0, 3).map(n => n / 255).map(n => n <= .04045 ? n / 12.92 : ((n + .055) / 1.055) ** 2.4).reduce((s, n, i) => s + n * [.2126, .7152, .0722][i], 0);
    return [...document.querySelectorAll('h1, .controls dt, .controls .primary:not(:disabled), .safety, .segment-data-status')].map(element => {
      let parent: Element | null = element;
      while (parent && getComputedStyle(parent).backgroundColor === 'rgba(0, 0, 0, 0)') parent = parent.parentElement;
      const fg = luminance(rgb(getComputedStyle(element).color));
      const bg = luminance(rgb(getComputedStyle(parent!).backgroundColor));
      return (Math.max(fg, bg) + .05) / (Math.min(fg, bg) + .05);
    });
  });
  for (const ratio of contrasts) expect(ratio).toBeGreaterThanOrEqual(4.5);
  const borderContrast = await page.getByRole('textbox', { name: labels[1], exact: true }).evaluate(element => {
    const css = getComputedStyle(element);
    const luminance = (value: string) => value.match(/[\d.]+/g)!.slice(0, 3).map(Number).map(n => n / 255)
      .map(n => n <= .04045 ? n / 12.92 : ((n + .055) / 1.055) ** 2.4).reduce((sum, n, i) => sum + n * [.2126, .7152, .0722][i], 0);
    const border = luminance(css.borderTopColor); const fill = luminance(css.backgroundColor);
    return (Math.max(border, fill) + .05) / (Math.min(border, fill) + .05);
  });
  expect(borderContrast).toBeGreaterThanOrEqual(3);
  await page.screenshot({ path: info.outputPath(`dark-${locale}.png`), fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await expect(page.locator('.maplibregl-ctrl-attrib')).toBeVisible();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: labels[8], exact: true }).click();
  expect(await readFile((await (await download).path())!, 'utf8')).toBe(candidates.candidates[0].route.gpx);
});

test('system theme follows OS, explicit choice persists and overrides OS', async ({ page }) => {
  await setup(page); await page.emulateMedia({ colorScheme: 'dark' }); await page.goto('/');
  const theme = page.getByRole('combobox', { name: 'Theme', exact: true });
  await expect(theme).toHaveValue('system');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.emulateMedia({ colorScheme: 'light' });
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await theme.selectOption('dark'); await page.reload();
  await expect(theme).toHaveValue('dark');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.emulateMedia({ colorScheme: 'dark' }); await page.emulateMedia({ colorScheme: 'light' });
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
});

test('failed dark basemap retries in the selected theme', async ({ page }) => {
  const network = await setup(page); await page.goto('/');
  await expect(page.locator('.map-panel')).toHaveAttribute('aria-busy', 'false');
  network.fail(true);
  await page.getByRole('combobox', { name: 'Theme', exact: true }).selectOption('dark');
  await expect(page.getByRole('button', { name: 'Retry map', exact: true })).toBeVisible();
  network.fail(false); await page.getByRole('button', { name: 'Retry map', exact: true }).click();
  await expect(page.locator('.map-panel')).toHaveAttribute('aria-busy', 'false');
  await expect(page.getByRole('button', { name: 'Retry map', exact: true })).toHaveCount(0);
  expect(network.styles.at(-1)).toBe('/styles/dark');
});

test('rapid theme changes ignore a delayed obsolete basemap', async ({ page }) => {
  await setup(page);
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  let requested = false;
  await page.route('**/styles/dark', async r => { requested = true; await gate; await r.fulfill({ json: dark }); });
  await page.goto('/');
  await expect(page.locator('.map-panel')).toHaveAttribute('aria-busy', 'false');
  const theme = page.getByRole('combobox', { name: 'Theme', exact: true });
  await theme.selectOption('dark'); await expect.poll(() => requested).toBe(true);
  await theme.selectOption('light'); release();
  await expect(page.locator('.map-panel')).toHaveAttribute('aria-busy', 'false');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await expect(async () => {
    const png = PNG.sync.read(await page.locator('canvas').screenshot());
    expect(png.data[0]).toBeGreaterThan(100);
  }).toPass();
  await expect(page.getByRole('button', { name: 'Retry map', exact: true })).toHaveCount(0);
});

test('theme remains usable when browser storage is blocked', async ({ page }) => {
  await setup(page);
  await page.addInitScript(() => { Object.defineProperty(window, 'localStorage', { get() { throw new DOMException('Storage blocked', 'SecurityError'); } }); });
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await page.getByRole('combobox', { name: 'Theme', exact: true }).selectOption('dark');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(page.locator('.map-panel')).toHaveAttribute('aria-busy', 'false');
  expect(errors).toEqual([]);
});

test('obsolete initial style cannot remove the active theme deadline', async ({ page }) => {
  await setup(page); await page.clock.install();
  let releaseLight!: () => void; let releaseDark!: () => void;
  const lightGate = new Promise<void>(resolve => { releaseLight = resolve; });
  const darkGate = new Promise<void>(resolve => { releaseDark = resolve; });
  let lightRequested = false; let darkRequested = false;
  let lightCancelled = false;
  page.on('requestfailed', request => { if (request.url().endsWith('/styles/liberty')) lightCancelled = true; });
  await page.route('**/styles/liberty', async r => { lightRequested = true; await lightGate; await r.fulfill({ json: basemap }); });
  await page.route('**/styles/dark', async r => { darkRequested = true; await darkGate; await r.fulfill({ json: dark }); });
  try {
    await page.goto('/'); await expect.poll(() => lightRequested).toBe(true);
    await page.getByRole('combobox', { name: 'Theme', exact: true }).selectOption('dark');
    await expect.poll(() => darkRequested).toBe(true);
    releaseLight();
    await expect.poll(async () => lightCancelled || await page.locator('.map-panel').getAttribute('aria-busy') === 'false').toBe(true);
    await page.clock.fastForward(21000);
    await expect(page.getByRole('button', { name: 'Retry map', exact: true })).toBeVisible();
  } finally { releaseLight(); releaseDark(); }
});

test('parsed style with stalled basemap data keeps loading and then offers retry', async ({ page }) => {
  await setup(page); await page.clock.install(); await page.emulateMedia({ colorScheme: 'dark' });
  let release!: () => void; let requested = false;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const stalled: StyleSpecification = { ...dark, sources: { ...dark.sources, roads: { type: 'geojson', data: 'https://tiles.openfreemap.org/slow.geojson' } } };
  await page.route('**/styles/dark', r => r.fulfill({ json: stalled }));
  await page.route('**/slow.geojson', async r => { requested = true; await gate; await r.fulfill({ json: { type: 'FeatureCollection', features: [] } }); });
  try {
    await page.goto('/'); await expect.poll(() => requested).toBe(true);
    await expect(page.locator('.map-panel')).toHaveAttribute('aria-busy', 'true');
    await page.clock.fastForward(21000);
    await expect(page.getByRole('button', { name: 'Retry map', exact: true })).toBeVisible();
  } finally { release(); }
});
