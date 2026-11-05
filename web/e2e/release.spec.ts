import { devices, expect, test, type Page } from '@playwright/test';
import { openEmailLink, register, signIn, text, uniqueEmail, useLanguage } from './support.ts';

const language = 'mk' as const;

/** At phone width nothing may scroll sideways (R1-P12 mobile pass). */
async function expectNoHorizontalScroll(page: Page, what: string) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow, `${what} scrolls sideways by ${overflow}px`).toBeLessThanOrEqual(0);
}

/**
 * R1-P12: the whole Release 1 journey through the UI, as a teacher would do it: sign up → confirm the email →
 * profile → offer → publish the site → a visitor's lesson request arrives in the inbox. Checked on desktop and phone.
 */
test('a new teacher goes from sign-up to a published page and receives a visitor request', async ({ page, browser }, testInfo) => {
  test.setTimeout(120_000);
  const phoneRun = testInfo.project.name === 'mobile';
  await useLanguage(page, language);
  const email = uniqueEmail('journey');

  await register(page, language, email);
  await openEmailLink(page, language, email, '/verify-email');
  await expect(page.getByText(text(language, 'auth.verify.done'))).toBeVisible();
  await page.getByRole('link', { name: text(language, 'auth.verify.continue') }).click();
  await signIn(page, language, email);
  await expect(page).toHaveURL(/\/app\/w\/[0-9a-f-]{36}$/);
  const workspaceId = /\/app\/w\/([0-9a-f-]{36})$/.exec(page.url())![1]!;
  if (phoneRun) await expectNoHorizontalScroll(page, 'the dashboard');

  // Profile.
  await page.goto(`/app/w/${workspaceId}/profile`);
  await page.getByLabel(text(language, 'profile.fields.firstName'), { exact: true }).fill('Елена');
  await page.getByLabel(text(language, 'profile.fields.lastName'), { exact: true }).fill('Трајковска');
  await page.getByLabel(text(language, 'profile.fields.bio')).fill('Часови по математика за основно и средно образование.');
  await page.getByRole('checkbox', { name: 'Математика', exact: true }).check();
  await page.getByRole('button', { name: text(language, 'profile.save') }).click();
  await expect(page.getByText(text(language, 'profile.saved'))).toBeVisible();
  if (phoneRun) await expectNoHorizontalScroll(page, 'the profile');

  // Offer.
  await page.goto(`/app/w/${workspaceId}/offers/new`);
  await page.getByLabel(text(language, 'offers.fields.title')).fill('Час по алгебра');
  await page.getByLabel(text(language, 'offers.fields.subject')).selectOption({ label: 'Математика' });
  await page.getByLabel(text(language, 'offers.fields.price')).fill('900');
  await page.getByRole('button', { name: text(language, 'offers.save') }).click();
  await expect(page).toHaveURL(new RegExp(`/app/w/${workspaceId}/offers$`));
  await expect(page.getByText('Час по алгебра')).toBeVisible();
  if (phoneRun) await expectNoHorizontalScroll(page, 'the offers');

  // Site: address, headline, publish.
  const slug = `journey-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;
  await page.goto(`/app/w/${workspaceId}/site`);
  await page.getByLabel(text(language, 'site.fields.slug')).fill(slug);
  await page.getByLabel(text(language, 'site.fields.headline'), { exact: true }).fill('Математика со Елена');
  await page.getByRole('button', { name: text(language, 'site.save') }).click();
  await expect(page.getByText(text(language, 'site.saved'))).toBeVisible();
  const card = page.getByTestId('publish-card');
  await card.getByRole('button', { name: text(language, 'site.publish.publish'), exact: true }).click();
  await expect(card.getByText(text(language, 'site.publish.published'))).toBeVisible();
  if (phoneRun) await expectNoHorizontalScroll(page, 'the site editor');

  // A visitor on a phone, without a session, finds the offer and asks for a lesson.
  const visitor = await browser.newContext({ ...devices['Pixel 7'] });
  const phone = await visitor.newPage();
  const errors: string[] = [];
  phone.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  expect((await phone.goto(`/${slug}`))?.status()).toBe(200);
  await expect(phone.getByRole('heading', { level: 1, name: 'Математика со Елена' })).toBeVisible();
  await expect(phone.getByText('Час по алгебра')).toBeVisible();
  await expectNoHorizontalScroll(phone, 'the public page');
  const form = phone.getByRole('form', { name: 'Испратете барање' });
  await form.getByRole('radio', { name: 'Да побарам час' }).check();
  await form.getByLabel('Име и презиме', { exact: true }).fill('Петар Николов');
  await form.getByLabel('Е-пошта', { exact: true }).fill('petar@example.test');
  await form.getByLabel('Понуда (по желба)', { exact: true }).selectOption({ label: 'Час по алгебра' });
  await form.getByLabel(/^Порака/).fill('Сакам подготовка за тест.');
  await form.getByRole('checkbox').nth(0).check();
  await form.getByRole('checkbox').nth(1).check();
  await form.getByRole('button', { name: 'Испрати' }).click();
  await expect(phone.getByText(/Ви благодариме!/)).toBeVisible();
  expect(errors).toEqual([]);
  await visitor.close();

  // The teacher sees it: dashboard count and the inbox entry.
  await page.goto(`/app/w/${workspaceId}/leads`);
  await page.getByRole('link', { name: /Петар Николов/ }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Петар Николов' })).toBeVisible();
  await expect(page.getByText('Час по алгебра')).toBeVisible();
  if (phoneRun) await expectNoHorizontalScroll(page, 'the request');
});

/** R1-P12 SEO checks on a published sample page: the HTML search engines and social cards see. */
test('a published page has complete SEO metadata, a sitemap entry and crawler rules', async ({ page, request }) => {
  await useLanguage(page, language);
  const email = uniqueEmail('seo');
  await register(page, language, email);
  await openEmailLink(page, language, email, '/verify-email');
  await page.getByRole('link', { name: text(language, 'auth.verify.continue') }).click();
  await signIn(page, language, email);
  await expect(page).toHaveURL(/\/app\/w\/[0-9a-f-]{36}$/);
  const workspaceId = /\/app\/w\/([0-9a-f-]{36})$/.exec(page.url())![1]!;
  const slug = `seo-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;
  await page.goto(`/app/w/${workspaceId}/site`);
  await page.getByLabel(text(language, 'site.fields.slug')).fill(slug);
  await page.getByLabel(text(language, 'site.fields.headline'), { exact: true }).fill('Англиски јазик со Мила');
  await page.getByRole('button', { name: text(language, 'site.save') }).click();
  await expect(page.getByText(text(language, 'site.saved'))).toBeVisible();
  await page.getByTestId('publish-card').getByRole('button', { name: text(language, 'site.publish.publish'), exact: true }).click();
  await expect(page.getByTestId('publish-card').getByText(text(language, 'site.publish.published'))).toBeVisible();

  const response = await request.get(`/${slug}`);
  expect(response.status()).toBe(200);
  expect(response.headers()['content-type']).toContain('text/html');
  const html = await response.text();
  expect(html.trimStart().toLowerCase().startsWith('<!doctype html>')).toBe(true);
  expect(html).toMatch(/<html[^>]* lang="mk"/);
  expect(html).toMatch(/<meta charset="utf-8"/i);
  expect(html).toMatch(/<meta name="viewport" content="[^"]*width=device-width/);
  expect(html).not.toMatch(/<meta name="robots" content="[^"]*noindex/);

  const visitor = await page.context().browser()!.newContext();
  const seo = await visitor.newPage();
  await seo.goto(`/${slug}`);
  await expect(seo).toHaveTitle(/Англиски јазик со Мила/);
  const description = await seo.locator('meta[name="description"]').getAttribute('content');
  expect(description?.length ?? 0).toBeGreaterThan(20);
  expect(description!.length).toBeLessThanOrEqual(160);
  await expect(seo.locator('link[rel="canonical"]')).toHaveAttribute('href', new RegExp(`^https?://[^/]+/${slug}$`));
  await expect(seo.locator('meta[property="og:title"]')).toHaveAttribute('content', /Англиски јазик со Мила/);
  await expect(seo.locator('meta[property="og:type"]')).toHaveAttribute('content', /.+/);
  await expect(seo.locator('meta[property="og:url"]')).toHaveAttribute('content', new RegExp(`/${slug}$`));
  await expect(seo.locator('meta[name="twitter:card"]')).toHaveAttribute('content', /.+/);
  await expect(seo.locator('h1')).toHaveCount(1);
  // Every image has a text alternative; every link has a name.
  expect(await seo.locator('img:not([alt])').count()).toBe(0);
  expect(await seo.locator('a').evaluateAll((links) => links.filter((a) => !(a.textContent?.trim() || a.getAttribute('aria-label'))).length)).toBe(0);
  // Structured data parses and describes the teacher.
  const jsonLd = await seo.locator('script[type="application/ld+json"]').allTextContents();
  expect(jsonLd.length).toBeGreaterThan(0);
  const data = jsonLd.map((block) => JSON.parse(block) as Record<string, unknown>);
  expect(JSON.stringify(data)).toContain('schema.org');
  await visitor.close();

  // Sitemap (well-formed XML with the page) and robots (the app areas are not crawled, the sitemap is announced).
  const sitemap = await request.get('/sitemap.xml');
  expect(sitemap.status()).toBe(200);
  expect(sitemap.headers()['content-type']).toContain('xml');
  const xml = await sitemap.text();
  expect(xml).toMatch(/^<\?xml version="1\.0" encoding="utf-8"\?>/i);
  expect(xml).toContain('http://www.sitemaps.org/schemas/sitemap/0.9');
  expect(xml).toContain(`/${slug}</loc>`);
  const robots = await (await request.get('/robots.txt')).text();
  for (const area of ['/app', '/admin', '/api']) expect(robots).toContain(`Disallow: ${area}`);
  expect(robots).toMatch(/Sitemap: https?:\/\/[^\s]+\/sitemap\.xml/);

  // Unknown pages answer 404 and are not indexed.
  const missing = await request.get(`/${slug}-missing`);
  expect(missing.status()).toBe(404);
  expect(await missing.text()).toMatch(/<meta name="robots" content="noindex"/);
});
