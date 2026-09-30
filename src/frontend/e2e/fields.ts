import type { Page } from '@playwright/test';

export async function fillField(page: Page, label: string, value: string) {
  const input = page.getByLabel(label, { exact: true });
  const details = input.locator('xpath=ancestor::details');
  if (await details.count() && await details.getAttribute('open') === null) await details.locator('summary').click();
  await input.fill(value);
}
