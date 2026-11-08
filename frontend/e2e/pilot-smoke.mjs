import { readFile } from 'node:fs/promises';
import { chromium } from '@playwright/test';

// Called only by the owned deployment harness. No fixtures, credentials or
// browser artifacts are written into a public report.
const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
if (!/^https:\/\/127\.0\.0\.1:\d+$/.test(config.base) || !/^[A-Za-z0-9+/]{43}=$/.test(config.spki))
  throw new Error('Refusing an unverified pilot browser target');
const browser = await chromium.launch({ headless: true, args: [`--ignore-certificate-errors-spki-list=${config.spki}`] });
try {
  const context = await browser.newContext({ locale: 'he-IL', timezoneId: 'Asia/Jerusalem' });
  await context.addCookies(config.cookies);
  const page = await context.newPage();
  await page.goto(`${config.base}/scheduling?period=${config.period}`);
  await page.getByRole('region', { name: 'לוח סידור עבודה שבועי', exact: true }).waitFor();
  if (!(await page.evaluate(() => window.isSecureContext))) throw new Error('Pilot page must be a secure context');
  if (await page.locator('.scheduling-page').getAttribute('dir') !== 'rtl') throw new Error('RTL must be preserved');
  if (await page.getByRole('region', { name: 'לוח סידור עבודה שבועי', exact: true }).locator('tbody tr').count() !== 51)
    throw new Error('All scheduling rows must render');
  await context.close();
} finally { await browser.close(); }
