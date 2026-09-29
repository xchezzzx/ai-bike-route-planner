import { expect, test, type Page } from '@playwright/test';
import { PNG } from 'pngjs';
import { readFile } from 'node:fs/promises';
import { basemap } from './basemap';
import { candidates, interpretation, route } from '../tests/fixtures';
import type { GeneratedRoute, RouteSegment, RouteSurface, RouteWayType } from '../src/types';
import { t } from '../src/i18n';

const coordinates = [[34.78,32.11],[34.84,32.11],[34.84,32.10],[34.78,32.10],[34.78,32.09],[34.84,32.09],
  [34.84,32.08],[34.78,32.08],[34.78,32.07],[34.84,32.07],[34.84,32.06],[34.78,32.06],[34.78,32.11]];
const surfaces: RouteSurface[] = ['asphalt','paved','unpaved','other','unknown'];
const ways: RouteWayType[] = ['cycleway','road','footway','stateRoad','path','track','street','steps','ferry','construction','unknown','road'];
const annotated = { ...route, geometry: coordinates.map(([longitude, latitude]) => ({ longitude, latitude, elevationMeters: null })),
  segments: ways.map((wayType, i): RouteSegment => ({ fromPointIndex: i, toPointIndex: i + 1, surface: surfaces[i % surfaces.length], wayType })),
};

async function setup(page: Page, metadata: 'valid' | 'missing' | 'invalid' = 'valid', fixture: GeneratedRoute = annotated) {
  let calls = 0; let empty = false; let failMap = false;
  const selected = { ...fixture, segments: metadata === 'missing' ? undefined : metadata === 'invalid' ? [{ ...annotated.segments[0], toPointIndex: 999 }] : fixture.segments };
  await page.route('**/*', async intercepted => {
    const url = new URL(intercepted.request().url());
    if (url.hostname === 'tiles.openfreemap.org') return failMap ? intercepted.abort() : intercepted.fulfill({ json: basemap });
    if (url.pathname === '/health') return intercepted.fulfill({ body: 'Healthy' });
    if (url.pathname.startsWith('/api/')) {
      calls++;
      return intercepted.fulfill({ json: url.pathname.endsWith('/interpret') ? interpretation : {
        ...candidates, candidates: empty ? [] : [{ ...candidates.candidates[0], route: selected }, candidates.candidates[1]],
        ...(empty ? {
          excludedCandidates: [{ seed: 1, distanceMeters: 40000, estimatedDurationSeconds: 4500,
            assessment: { ...candidates.candidates[0].assessment!, targetsMatched: false }, reasons: ['targets_not_met'] }],
          warnings: ['no_candidate_meets_requirements', 'candidates_excluded'],
        } : {}),
      } });
    }
    if (url.hostname === '127.0.0.1') return intercepted.continue();
    throw new Error(`Unexpected external request: ${url.origin}`);
  });
  await page.goto('/');
  await page.getByLabel('Start latitude', { exact: true }).fill('32.08');
  await page.getByLabel('Start longitude', { exact: true }).fill('34.78');
  await page.getByLabel('Ride request', { exact: true }).fill('A 25 km road loop');
  await page.getByRole('button', { name: 'Interpret request', exact: true }).click();
  await page.getByRole('button', { name: 'Generate routes', exact: true }).click();
  await expect(page.getByRole('radio', { name: 'Surface', exact: true })).toBeChecked();
  return { calls: () => calls, clear: () => { empty = true; }, mapFails: (value: boolean) => { failMap = value; } };
}

function topStroke(png: PNG) {
  const green = (x: number, y: number) => {
    const i = (y * png.width + x) * 4;
    return Math.abs(png.data[i] - 21) < 8 && Math.abs(png.data[i+1] - 114) < 8 && Math.abs(png.data[i+2] - 79) < 8;
  };
  for (let y = 20; y < png.height / 2; y++) {
    const xs = Array.from({ length: png.width }, (_, x) => x).filter(x => green(x,y));
    if (xs.length > 70 && xs.at(-1)! - xs[0] > 90) return { y, left: xs[0], right: xs.at(-1)! };
  }
  throw new Error('Expected a visible long green track segment');
}

for (const locale of ['en','ru','he'] as const) test(`track patterns, gap taps and keyboard details (${locale})`, async ({ page }, info) => {
  const network = await setup(page);
  const canvas = page.locator('canvas');
  await canvas.scrollIntoViewIfNeeded();
  let before!: PNG;
  await expect(async () => { before = PNG.sync.read(await canvas.screenshot()); topStroke(before); }).toPass();
  const stroke = topStroke(before); const calls = network.calls();
  await page.getByRole('radio', { name: 'Road type', exact: true }).check();
  let gap: { x: number; y: number } | undefined;
  await expect(async () => {
    const after = PNG.sync.read(await canvas.screenshot());
    // The top edge is asphalt/cycleway: switching changes a solid stroke to long dashes, not its position.
    let whites = 0; let colored = 0;
    for (let x = stroke.left + 12; x < stroke.right - 12; x++) {
      const i = (stroke.y * after.width + x) * 4;
      if (after.data[i] > 240 && after.data[i+1] > 240 && after.data[i+2] > 240) { whites++; gap = { x, y: stroke.y }; }
      if (after.data[i] < 40 && after.data[i+1] > 95 && after.data[i+1] < 130) colored++;
    }
    expect(whites).toBeGreaterThan(5); expect(colored).toBeGreaterThan(30);
  }).toPass();
  const start = await page.getByLabel('Start latitude', { exact: true }).inputValue();
  await canvas.click({ position: gap! });
  await expect(page.getByRole('region', { name: 'Segment details' })).toContainText('Cycleway');
  await expect(page.getByLabel('Start latitude', { exact: true })).toHaveValue(start);
  await page.getByRole('radio', { name: 'Surface', exact: true }).check();
  await page.getByText('Track segments', { exact: true }).click();
  await page.getByRole('combobox', { name: 'Segment', exact: true }).focus();
  await page.keyboard.press('ArrowDown');
  await expect(page.getByRole('region', { name: 'Segment details' })).toContainText('Paved');
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download GPX', exact: true }).click();
  expect(await readFile((await (await download).path())!, 'utf8')).toBe(route.gpx);
  expect(network.calls()).toBe(calls);

  // Changing locale intentionally clears planner results; regenerate through localized controls.
  const labels = { en: ['Interpret request','Generate routes','Surface','Road type','Track segments'],
    ru: ['Разобрать запрос','Построить маршруты','Покрытие','Тип дороги','Участки трека'],
    he: ['פירוש הבקשה','יצירת מסלולים','פני השטח','סוג הדרך','מקטעי המסלול'] }[locale];
  if (locale !== 'en') {
    await page.getByLabel('Language', { exact: true }).selectOption(locale);
    await page.getByRole('button', { name: labels[0], exact: true }).click();
    await page.getByRole('button', { name: labels[1], exact: true }).click();
  }
  await page.getByRole('radio', { name: labels[2], exact: true }).check();
  await canvas.scrollIntoViewIfNeeded();
  await page.screenshot({ path: info.outputPath(`surface-${locale}.png`), fullPage: true });
  await page.getByRole('radio', { name: labels[3], exact: true }).check();
  await page.screenshot({ path: info.outputPath(`ways-${locale}.png`), fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await expect(page.locator('.maplibregl-ctrl-attrib')).toBeVisible();
  await page.locator('.route-option input').nth(1).check();
  await expect(page.locator('.segment-detail')).toHaveCount(0);
  network.clear();
  await page.getByRole('button', { name: labels[1], exact: true }).click();
  await expect(page.getByText(t(locale, 'noMatches'), { exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.locator('.segment-controls')).toHaveCount(0);
});

for (const mode of ['Surface', 'Road type'] as const) test(`dense intervals retain gaps in ${mode}`, async ({ page }) => {
  const count = 200;
  const fixture: GeneratedRoute = { ...annotated,
    geometry: [
      ...Array.from({ length: count }, (_, i) => ({ longitude: 34.78 + .06 * i / count, latitude: 32.11, elevationMeters: null })),
      ...annotated.geometry.slice(1),
    ],
    segments: [
      ...Array.from({ length: count }, (_, i): RouteSegment => ({ fromPointIndex: i, toPointIndex: i + 1,
        surface: mode === 'Surface' ? 'unpaved' : i % 2 ? 'asphalt' : 'paved',
        wayType: mode === 'Road type' ? 'cycleway' : i % 2 ? 'road' : 'track',
      })),
      ...annotated.segments.slice(1).map(s => ({ ...s, fromPointIndex: s.fromPointIndex + count - 1, toPointIndex: s.toPointIndex + count - 1 })),
    ],
  };
  await setup(page, 'valid', fixture);
  await page.getByRole('radio', { name: mode, exact: true }).check();
  const canvas = page.locator('canvas');
  await expect(async () => {
    const png = PNG.sync.read(await canvas.screenshot()); const stroke = topStroke(png);
    let whites = 0;
    for (let x = stroke.left + 12; x < stroke.right - 12; x++) {
      const i = (stroke.y * png.width + x) * 4;
      if (png.data[i] > 240 && png.data[i + 1] > 240 && png.data[i + 2] > 240) whites++;
    }
    expect(whites).toBeGreaterThan(5);
  }).toPass({ timeout: 5000 });
  await page.getByText('Track segments', { exact: true }).click();
  await page.getByRole('combobox', { name: 'Segment', exact: true }).selectOption('99');
  await expect(page.getByRole('region', { name: 'Segment details' })).toContainText(mode === 'Surface' ? 'Road' : 'Asphalt');
});

for (const metadata of ['missing','invalid'] as const) test(`legacy or malformed segment data keeps GPX usable (${metadata})`, async ({ page }) => {
  await setup(page, metadata);
  await expect(page.locator('.segment-controls [role=status]')).toContainText('unavailable');
  await expect(page.locator('.segment-legend')).toContainText('Unknown surface');
  await expect(page.locator('.segment-legend')).not.toContainText('Asphalt');
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download GPX', exact: true }).click();
  expect(await readFile((await (await download).path())!, 'utf8')).toBe(route.gpx);
});
