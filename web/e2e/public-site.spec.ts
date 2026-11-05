import { devices, expect, test } from '@playwright/test';
import { signedInTeacher, text, uniqueEmail, useLanguage } from './support.ts';

// R1-P4 acceptance: the teacher publishes the site, an anonymous visitor opens it on a phone, unpublishing → 404.
test('a published site is visible to an anonymous phone visitor until it is unpublished', async ({ page, browser }) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('site'));
  const slug = `e2e-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;

  await page.goto(`/app/w/${workspaceId}/site`);
  const address = page.getByLabel(text(language, 'site.fields.slug'));
  await address.fill(slug);
  await page.getByLabel(text(language, 'site.fields.headline'), { exact: true }).fill('Математика без страв');
  await page.getByRole('button', { name: text(language, 'site.save') }).click();
  await expect(page.getByText(text(language, 'site.saved'))).toBeVisible();

  const card = page.getByTestId('publish-card');
  await card.getByRole('button', { name: text(language, 'site.publish.publish'), exact: true }).click();
  await expect(card.getByText(text(language, 'site.publish.published'))).toBeVisible();
  await expect(card.getByRole('link', { name: new RegExp(`classhub\\.app/${slug}`) })).toHaveAttribute('href', `/${slug}`);

  // A visitor on a phone: no session, server-rendered page, the island is interactive, no CSP violations.
  const visitor = await browser.newContext({ ...devices['Pixel 7'] });
  const phone = await visitor.newPage();
  const violations: string[] = [];
  phone.on('console', (message) => {
    if (message.type() === 'error') violations.push(message.text());
  });
  const response = await phone.goto(`/${slug}`);
  expect(response?.status()).toBe(200);
  await expect(phone.getByRole('heading', { level: 1, name: 'Математика без страв' })).toBeVisible();
  await expect(phone).toHaveTitle(/Математика без страв/);
  await expect(phone.locator('link[rel="canonical"]')).toHaveAttribute('href', new RegExp(`/${slug}$`));
  await expect(phone.getByRole('button', { name: 'Копирај линк' })).toBeVisible();
  expect(await phone.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(violations).toEqual([]);

  // Unpublish (with confirmation) → the visitor gets a "not found" page that is not indexed.
  await card.getByRole('button', { name: text(language, 'site.publish.unpublish') }).click();
  await card.getByRole('button', { name: text(language, 'site.publish.unpublishConfirm') }).click();
  await expect(card.getByText(text(language, 'site.publish.notPublished'))).toBeVisible();
  const gone = await phone.goto(`/${slug}`);
  expect(gone?.status()).toBe(404);
  await expect(phone.getByRole('heading', { level: 1, name: 'Страницата не е пронајдена' })).toBeVisible();
  await expect(phone.locator('meta[name="robots"]')).toHaveAttribute('content', 'noindex');
  await visitor.close();
});

test('the landing page is server-rendered and links into the app', async ({ page }) => {
  await useLanguage(page, 'mk'); // the SPA keeps the visitor's chosen language
  const response = await page.goto('/');
  expect(response?.status()).toBe(200);
  await expect(page.locator('html')).toHaveAttribute('lang', 'mk');
  await page.getByRole('link', { name: 'Креирајте бесплатна страница' }).click();
  await expect(page).toHaveURL(/\/register$/);
  await expect(page.getByRole('heading', { level: 1, name: text('mk', 'auth.register.title') })).toBeVisible();
});
