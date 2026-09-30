import { fillField } from "./fields";
import { expect, test } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { basemap } from './basemap';
import { candidates, interpretation } from '../tests/fixtures';
import { t } from '../src/i18n';

for (const locale of ['en', 'ru', 'he'] as const) test(`canonical track names follow selection and GPX (${locale})`, async ({ page }, info) => {
  const routes = structuredClone(candidates);
  const names = ['Tel-Aviv-loop-road-25', 'Long-Settlement-Name-Near-The-Starting-Point-Another-Settlement-Near-The-Finish-road-28'];
  routes.candidates.forEach((candidate, index) => {
    candidate.route.name = names[index];
    candidate.route.gpx = `<?xml version="1.0"?><gpx><trk><name>${names[index]}</name></trk></gpx>`;
  });
  await page.route('**/*', async r => {
    const url = new URL(r.request().url());
    if (url.hostname === 'tiles.openfreemap.org') return r.fulfill({ json: basemap });
    if (url.pathname === '/health') return r.fulfill({ body: 'Healthy' });
    if (url.pathname.endsWith('/interpret')) return r.fulfill({ json: interpretation });
    if (url.pathname.endsWith('/candidates')) return r.fulfill({ json: routes });
    if (url.hostname === '127.0.0.1') return r.continue();
    throw new Error(`Unexpected external request: ${url.origin}`);
  });
  await page.goto('/');
  await page.getByLabel('Language', { exact: true }).selectOption(locale);
  await fillField(page, t(locale, 'startLatitude'), '32.08');
  await fillField(page, t(locale, 'startLongitude'), '34.78');
  await fillField(page, t(locale, 'prompt'), 'A 25 km road loop');
  await page.getByRole('button', { name: t(locale, 'interpret'), exact: true }).click();
  await page.getByRole('button', { name: t(locale, 'generate'), exact: true }).click();
  for (const [index, name] of names.entries()) {
    await page.getByRole('radio', { name: new RegExp(name) }).check();
    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: t(locale, 'download'), exact: true }).click();
    const file = await download;
    expect(file.suggestedFilename()).toBe(`${name}.gpx`);
    expect(await readFile((await file.path())!, 'utf8')).toBe(routes.candidates[index].route.gpx);
  }
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  for (const option of await page.locator('.route-option').all()) {
    const parent = (await option.boundingBox())!;
    for (const child of await option.locator('small, b').all()) {
      const box = (await child.boundingBox())!;
      expect(box.x).toBeGreaterThanOrEqual(parent.x);
      expect(box.x + box.width).toBeLessThanOrEqual(parent.x + parent.width);
    }
  }
  await page.screenshot({ path: info.outputPath(`track-names-${locale}.png`), fullPage: true });
});
