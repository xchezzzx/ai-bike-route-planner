import { expect, test, type Page } from '@playwright/test';
import { PNG } from 'pngjs';
import { readFile } from 'node:fs/promises';
import { basemap } from './basemap';
import { candidates, intent, interpretation, route } from '../tests/fixtures';

async function mockNetwork(page: Page, mapFails = false) {
  const posts: { path: string; body: unknown }[] = [];
  await page.route('**/*', async intercepted => {
    const request = intercepted.request();
    const url = new URL(request.url());
    if (url.hostname === 'tiles.openfreemap.org') {
      if (mapFails) await intercepted.abort('failed');
      else await intercepted.fulfill({ json: basemap });
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

test('manual A-B validates before generating and remains usable without map tiles', async ({ page }, testInfo) => {
  const posts = await mockNetwork(page, true);
  await page.goto('/');
  await expect(page.getByText('Map unavailable. Coordinates and GPX remain available.')).toBeVisible();
  await page.getByRole('radio', { name: 'Manual', exact: true }).check();
  await page.getByLabel('Route shape', { exact: true }).selectOption('pointToPoint');
  await page.getByLabel('Start latitude', { exact: true }).fill('32.08');
  await page.getByLabel('Start longitude', { exact: true }).fill('34.78');
  await page.getByLabel('Destination latitude', { exact: true }).fill('32.1');
  await page.getByLabel('Destination longitude', { exact: true }).fill('34.8');
  await page.getByLabel('Duration (min)', { exact: true }).fill('90');
  await page.getByRole('button', { name: 'Validate preferences', exact: true }).click();
  await expect(page.getByText('Ready to generate')).toBeVisible();
  expect(posts).toHaveLength(1);
  expect(posts[0].body).toMatchObject({ shape: 'pointToPoint', targetDurationSeconds: 5400 });
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
