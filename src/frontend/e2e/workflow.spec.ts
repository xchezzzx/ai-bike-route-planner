import { expect, test, type Page } from '@playwright/test';
import { PNG } from 'pngjs';
import { readFile } from 'node:fs/promises';
import { basemap, cyclingBasemap } from './basemap';
import { candidates, intent, interpretation, route, refinement } from '../tests/fixtures';

async function mockNetwork(page: Page, mapFails = false, style = basemap) {
  const posts: { path: string; body: unknown }[] = [];
  await page.route('**/*', async intercepted => {
    const request = intercepted.request();
    const url = new URL(request.url());
    if (url.hostname === 'tiles.openfreemap.org') {
      if (mapFails) await intercepted.abort('failed');
      else await intercepted.fulfill({ json: style });
    } else if (url.pathname === '/health') await intercepted.fulfill({ body: 'Healthy' });
    else if (url.pathname.startsWith('/api/')) {
      const body = request.postDataJSON();
      posts.push({ path: url.pathname, body });
      const response = url.pathname.endsWith('/interpret') ? interpretation : url.pathname.endsWith('/validate') ? body : url.pathname.endsWith('/candidates') ? candidates : route;
      await intercepted.fulfill({ json: response });
    } else if (url.hostname === '127.0.0.1') await intercepted.continue();
    else throw new Error(`Unexpected external request: ${url.origin}`);
  });
  return posts;
}
async function prompt(page: Page) {
  await page.getByLabel('Start latitude', { exact: true }).fill('32.08');
  await page.getByLabel('Start longitude', { exact: true }).fill('34.78');
  await page.getByLabel('Ride request', { exact: true }).fill('A 25 km road loop');
  await page.getByRole('button', { name: 'Interpret request', exact: true }).click();
  await expect(page.getByText('Ready to generate', { exact: true })).toBeVisible();
}
async function generate(page: Page) {
  await prompt(page);
  await page.getByRole('button', { name: 'Generate routes', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Routes', exact: true })).toBeVisible();
}

for (const locale of ['en', 'ru', 'he'] as const) test(`quality selection and cleared no-match map (${locale})`, async ({ page }, testInfo) => {
  await mockNetwork(page);
  let empty = false;
  const excluded = [2, 3].map(seed => ({ seed, distanceMeters: 40000 + seed, estimatedDurationSeconds: 4500,
    assessment: { ...candidates.candidates[0].assessment!, targetsMatched: false }, reasons: ['targets_not_met'] }));
  await page.route('**/api/routes/candidates', r => r.fulfill({ json: { ...candidates,
    candidates: empty ? [] : [candidates.candidates[0]], excludedCandidates: excluded,
    warnings: empty ? ['no_candidate_meets_requirements', 'candidates_excluded'] : ['candidates_excluded'],
  } }));
  const labels = {
    en: { prepare: 'Interpret request', generate: 'Generate routes', excluded: 'Excluded routes', noMatch: 'No routes meet these requirements.', download: 'Download GPX' },
    ru: { prepare: 'Разобрать запрос', generate: 'Построить маршруты', excluded: 'Исключённые маршруты', noMatch: 'Подходящих маршрутов не найдено.', download: 'Скачать GPX' },
    he: { prepare: 'פירוש הבקשה', generate: 'יצירת מסלולים', excluded: 'מסלולים שנפסלו', noMatch: 'לא נמצאו מסלולים שעומדים בדרישות.', download: 'הורדת GPX' },
  }[locale];
  await page.goto('/');
  await prompt(page);
  if (locale !== 'en') {
    await page.getByLabel('Language', { exact: true }).selectOption(locale);
    await page.getByRole('button', { name: labels.prepare, exact: true }).click();
  }
  await page.getByRole('button', { name: labels.generate, exact: true }).click();
  await expect(page.locator('.route-option')).toHaveCount(1);
  await expect(page.locator('.quality-metrics').first()).toBeVisible();
  await page.getByText(labels.excluded, { exact: true }).click();
  await page.screenshot({ path: testInfo.outputPath(`quality-${locale}.png`), fullPage: true });
  const canvas = page.locator('.maplibregl-canvas');
  const greenPixels = async () => {
    const png = PNG.sync.read(await canvas.screenshot()); let green = 0;
    for (let i = 0; i < png.data.length; i += 4) if (png.data[i] < 45 && png.data[i + 1] > 80 && png.data[i + 1] < 150 && png.data[i + 2] < 110) green++;
    return green;
  };
  await expect.poll(greenPixels).toBeGreaterThan(100);
  const before = await greenPixels();
  empty = true;
  await page.getByRole('button', { name: labels.generate, exact: true }).click();
  await expect(page.getByText(labels.noMatch, { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: labels.download, exact: true })).toHaveCount(0);
  await expect(page.locator('.route-option')).toHaveCount(0);
  await expect.poll(greenPixels).toBeLessThan(before / 2);
  await expect(page.locator('html')).toHaveAttribute('dir', locale === 'he' ? 'rtl' : 'ltr');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const png = PNG.sync.read(await canvas.screenshot()); const colors = new Set<number>();
  for (let i = 0; i < png.data.length; i += 4) colors.add((png.data[i] << 16) | (png.data[i + 1] << 8) | png.data[i + 2]);
  expect(colors.size).toBeGreaterThan(10);
  await page.screenshot({ path: testInfo.outputPath(`no-match-${locale}.png`), fullPage: true });
});

for (const fallback of [false, true]) test(`AI refinement trace and selected GPX (fallback=${fallback})`, async ({ page }, testInfo) => {
  await mockNetwork(page);
  const calls: unknown[] = [];
  await page.route('**/api/routes/plan', async intercepted => {
    calls.push(intercepted.request().postDataJSON());
    await intercepted.fulfill({ json: fallback ? { ...refinement, advisorStatus: 'failed', advisorFailure: 'quota', search: { ...candidates, warnings: ['advisor_fallback'] } } : refinement });
  });
  await page.goto('/');
  await prompt(page);
  await expect(page.getByRole('checkbox', { name: 'AI refinement', exact: true })).not.toBeChecked();
  await page.getByRole('checkbox', { name: 'AI refinement', exact: true }).check();
  expect(calls).toHaveLength(0);
  await page.getByRole('button', { name: 'Generate routes', exact: true }).click();
  await expect(page.getByText(fallback ? 'AI unavailable; ordinary search retained' : 'AI-guided search completed', { exact: true })).toBeVisible();
  await page.getByText('Search details', { exact: true }).click();
  await expect(page.getByText('Duplicate route', { exact: true })).toBeVisible();
  await page.getByRole('radio', { name: /Route 2/ }).check();
  const downloaded = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download GPX', exact: true }).click();
  expect(await readFile((await (await downloaded).path())!, 'utf8')).toBe(candidates.candidates[1].route.gpx);
  expect(calls).toEqual([intent]);
  await page.screenshot({ path: testInfo.outputPath('refinement-en.png'), fullPage: true });
  await page.getByLabel('Language', { exact: true }).selectOption('he');
  await page.getByRole('button', { name: 'פירוש הבקשה', exact: true }).click();
  await expect(page.getByRole('checkbox', { name: 'עידון באמצעות AI', exact: true })).toBeChecked();
  await page.getByRole('button', { name: 'יצירת מסלולים', exact: true }).click();
  await expect(page.getByRole('region', { name: 'מסלולים', exact: true })).toBeVisible();
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await page.getByText('פרטי החיפוש', { exact: true }).click();
  await expect(page.getByText('מסלול כפול', { exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('refinement-he.png'), fullPage: true });
});

for (const subclass of ['cycleway', 'footway'] as const) test(`map distinguishes ${subclass} even when bicycle=yes`, async ({ page }, testInfo) => {
  await mockNetwork(page, false, cyclingBasemap(subclass));
  await page.goto('/');
  await expect(page.getByRole('region', { name: 'Route map', exact: true })).toHaveAttribute('aria-busy', 'false');
  for (let i = 0; i < 4; i++) await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
  const canvas = page.locator('.maplibregl-canvas');
  const count = async () => {
    const { data } = PNG.sync.read(await canvas.screenshot());
    let blue = 0, white = 0;
    for (let i = 0; i < data.length; i += 4) {
      if (data[i] < 80 && data[i + 1] > 60 && data[i + 1] < 140 && data[i + 2] > 200) blue++;
      if (data[i] > 250 && data[i + 1] > 250 && data[i + 2] > 250) white++;
    }
    return { blue, white };
  };
  await expect.poll(async () => (await count())[subclass === 'cycleway' ? 'blue' : 'white']).toBeGreaterThan(100);
  if (subclass === 'footway') expect((await count()).blue).toBe(0);
  await page.screenshot({ path: testInfo.outputPath(`${subclass}.png`), fullPage: true });
});

test('prompt confirmation, real canvas, route selection, fit and selected GPX', async ({ page }, testInfo) => {
  const posts = await mockNetwork(page);
  await page.goto('/');
  await expect(page.getByRole('region', { name: 'Route map', exact: true })).toHaveAttribute('aria-busy', 'false');
  await expect(page.getByRole('link', { name: 'OpenFreeMap', exact: true })).toHaveCount(1);
  await prompt(page);
  expect(posts).toHaveLength(1);
  await expect(page.getByRole('region', { name: 'Routes', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Generate routes', exact: true }).click();
  await expect(page.getByRole('radio', { name: /Route 1/ })).toBeChecked();
  await page.getByRole('button', { name: 'Fit selected route', exact: true }).click();
  const canvas = page.locator('.maplibregl-canvas');
  await expect(canvas).toBeVisible();
  const pixels = PNG.sync.read(await canvas.screenshot());
  let green = 0;
  for (let i = 0; i < pixels.data.length; i += 4) {
    if (pixels.data[i] < 45 && pixels.data[i + 1] > 80 && pixels.data[i + 1] < 150 && pixels.data[i + 2] < 110) green++;
  }
  expect(green).toBeGreaterThan(100);
  // Click the visible alternate geometry, not a test hook into the map instance.
  let alternate: { x: number; y: number } | undefined;
  for (let y = pixels.height - 35; y > 30 && !alternate; y--) {
    for (let x = 30; x < pixels.width - 50; x++) {
      const index = (y * pixels.width + x) * 4;
      const [r, g, b] = pixels.data.subarray(index, index + 3);
      if (r > 150 && r < 230 && g > 80 && g < 175 && b > g + 8 && b < 195) { alternate = { x, y }; break; }
    }
  }
  expect(alternate).toBeDefined();
  await canvas.click({ position: alternate! });
  await expect(page.getByRole('radio', { name: /Route 2/ })).toBeChecked();
  await page.getByRole('radio', { name: /Route 1/ }).check();
  await page.getByRole('radio', { name: /Route 2/ }).check();
  await expect(page.getByText('120 m', { exact: true })).toBeVisible();
  const downloadEvent = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download GPX', exact: true }).click();
  const download = await downloadEvent;
  expect(await readFile((await download.path())!, 'utf8')).toBe(candidates.candidates[1].route.gpx);
  expect(posts.map(post => post.path)).toEqual(['/api/route-intents/interpret', '/api/routes/candidates']);
  expect(posts[1].body).toEqual(intent);
  await page.screenshot({ path: testInfo.outputPath('results-full.png'), fullPage: true });
});

for (const hasTarget of [false, true]) test(`manual A-B (target=${hasTarget}) validates before generating and remains usable without map tiles`, async ({ page }, testInfo) => {
  const posts = await mockNetwork(page, true);
  await page.goto('/');
  await expect(page.getByText('Map unavailable. Coordinates and GPX remain available.')).toBeVisible();
  await page.getByRole('radio', { name: 'Manual', exact: true }).check();
  await page.getByLabel('Route shape', { exact: true }).selectOption('pointToPoint');
  await expect(page.getByRole('option', { name: 'Gravel', exact: true })).toHaveJSProperty('disabled', true);
  await expect(page.getByRole('option', { name: 'Minimize climbs', exact: true })).toHaveJSProperty('disabled', true);
  await page.getByLabel('Cycling profile', { exact: true }).press('End');
  await expect(page.getByLabel('Cycling profile', { exact: true })).toHaveValue('road');
  await expect(page.getByRole('button', { name: 'Generate routes', exact: true })).toHaveAccessibleDescription('Request not validated');
  await page.getByLabel('Start latitude', { exact: true }).fill('32.08');
  await page.getByLabel('Start longitude', { exact: true }).fill('34.78');
  await page.getByLabel('Destination latitude', { exact: true }).fill('32.1');
  await page.getByLabel('Destination longitude', { exact: true }).fill('34.8');
  if (hasTarget) await page.getByLabel('Duration (min)', { exact: true }).fill('90');
  await page.getByRole('button', { name: 'Validate preferences', exact: true }).click();
  await expect(page.getByText('Ready to generate')).toBeVisible();
  expect(posts).toHaveLength(1);
  expect(posts[0].body).toMatchObject({ shape: 'pointToPoint' });
  if (hasTarget) expect(posts[0].body).toHaveProperty('targetDurationSeconds', 5400);
  else expect(posts[0].body).not.toHaveProperty('targetDurationSeconds');
  expect(posts[0].body).not.toHaveProperty('targetDistanceMeters');
  await page.getByRole('button', { name: 'Generate routes', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Download GPX', exact: true })).toBeEnabled();
  expect(posts[1].path).toBe('/api/routes/generate');
  await page.screenshot({ path: testInfo.outputPath('map-failure-full.png'), fullPage: true });
  await page.getByLabel('Language', { exact: true }).selectOption('he');
  await expect(page.locator('.maplibregl-canvas')).toHaveAttribute('aria-label', 'מפה אינטראקטיבית');
});

test('provider failures are translated and do not expose raw provider details', async ({ page }) => {
  await mockNetwork(page);
  await page.route('**/api/routes/candidates', intercepted => intercepted.fulfill({ status: 503, json: { code: 'routing_rate_limited', detail: 'private-provider-content' } }));
  await page.goto('/');
  await prompt(page);
  await page.getByRole('button', { name: 'Generate routes', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Routing provider rate limit reached. Retry later.');
  await expect(page.getByRole('alert')).not.toContainText('private-provider-content');
  await expect(page.getByRole('button', { name: 'Download GPX', exact: true })).toHaveCount(0);
});

test('changing coordinates while route generation is pending cannot restore stale results', async ({ page }) => {
  await mockNetwork(page);
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  let arrived!: () => void;
  const started = new Promise<void>(resolve => { arrived = resolve; });
  await page.route('**/api/routes/candidates', async intercepted => {
    arrived(); await gate;
    await intercepted.fulfill({ json: candidates }).catch(() => {});
  });
  await page.goto('/');
  await prompt(page);
  await page.getByRole('button', { name: 'Generate routes', exact: true }).click();
  await started;
  await page.getByLabel('Start latitude', { exact: true }).fill('32.09');
  release();
  await expect(page.getByRole('button', { name: 'Generate routes', exact: true })).toBeDisabled();
  await expect(page.getByRole('region', { name: 'Routes', exact: true })).toHaveCount(0);
});

test('Hebrew RTL has usable coordinates, translated warnings and no horizontal overflow', async ({ page }, testInfo) => {
  await mockNetwork(page);
  await page.goto('/');
  await page.getByLabel('Language', { exact: true }).selectOption('he');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await page.getByLabel('קו רוחב של התחלה', { exact: true }).fill('32.08');
  await page.getByLabel('קו אורך של התחלה', { exact: true }).fill('34.78');
  await page.getByLabel('בקשת רכיבה', { exact: true }).fill('מסלול כביש מעגלי של 25 ק״מ');
  await page.getByRole('button', { name: 'פירוש הבקשה', exact: true }).click();
  await page.getByRole('button', { name: 'יצירת מסלולים', exact: true }).click();
  await expect(page.getByText('נתוני גובה אינם זמינים.')).toBeVisible();
  await expect(page.getByLabel('קו רוחב של התחלה', { exact: true })).toHaveAttribute('dir', 'ltr');
  await expect(page.getByText(route.attribution)).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('hebrew-full.png'), fullPage: true });
});

test('map click selects coordinates and a new selection clears completed routes', async ({ page }) => {
  await mockNetwork(page);
  await page.goto('/');
  await expect(page.getByRole('region', { name: 'Route map', exact: true })).toHaveAttribute('aria-busy', 'false');
  await page.locator('.maplibregl-canvas').click({ position: { x: 120, y: 130 } });
  await expect(page.getByLabel('Start latitude', { exact: true })).not.toHaveValue('');
  await generate(page);
  await page.getByRole('button', { name: 'Reset map view', exact: true }).click();
  await page.locator('.maplibregl-canvas').click({ position: { x: 80, y: 60 } });
  await expect(page.getByRole('button', { name: 'Download GPX', exact: true })).toHaveCount(0);
});
