import { expect, test, type Page } from '@playwright/test';
import { signedInTeacher, text, uniqueEmail, useLanguage } from './support.ts';

/**
 * R3-P7 acceptance: ClassHub installs as a web app. The service worker controls only the SPA areas (never the public
 * pages), keeps only static files, never API data; offline, a page navigation shows the offline page that says what
 * was on screen may be out of date. The manifests and icons are served. On a phone the newer pages have no horizontal
 * scroll and the boards say they are best on a larger screen.
 */

async function controlled(page: Page) {
  await page.waitForFunction(() => navigator.serviceWorker?.controller !== null, undefined, { timeout: 30_000 });
}

test('the service worker controls the app areas only and never keeps API data', async ({ page, context }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop', 'browser behaviour, checked once');
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('pwa'));

  await page.goto(`/app/w/${workspaceId}/toolbox`);
  await expect(page.getByRole('searchbox', { name: text(language, 'toolbox.search') })).toBeVisible();
  await controlled(page);
  const origin = new URL(page.url()).origin;
  const scopes = await page.evaluate(async () => (await navigator.serviceWorker.getRegistrations()).map((r) => r.scope).toSorted());
  expect(scopes).toEqual([`${origin}/app/`, `${origin}/student/`]);

  // The public pages are never controlled.
  await page.goto('/');
  expect(await page.evaluate(() => navigator.serviceWorker.controller)).toBeNull();

  // Back in the app: offline, a navigation shows the offline page instead of a stale copy.
  await page.goto(`/app/w/${workspaceId}/activities`);
  await controlled(page);
  await context.setOffline(true);
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Нема интернет врска' })).toBeVisible();
  await expect(page.getByText('Она што го гледавте претходно можеби не е ажурно.')).toBeVisible();
  await expect(page.getByText('What you saw before may be out of date.')).toBeVisible();
  // API data is never answered from a cache.
  expect(await page.evaluate(() => fetch('/api/auth/me').then(() => 'answered', () => 'failed'))).toBe('failed');

  await context.setOffline(false);
  await page.getByRole('link', { name: 'Обиди се повторно' }).click();
  await expect(page).toHaveURL(new RegExp(`/app/w/${workspaceId}/activities$`));
  await expect(page.getByRole('heading', { level: 1, name: text(language, 'activities.title') })).toBeVisible();

  // Launching the installed app (its start URL) while offline also gets the offline page.
  await context.setOffline(true);
  await page.goto('/app/');
  await expect(page.getByRole('heading', { name: 'No internet connection' })).toBeVisible();
  await context.setOffline(false);

  // Only the offline page and hashed build assets are cached.
  const cached = await page.evaluate(async () => {
    const urls: string[] = [];
    for (const key of await caches.keys()) {
      const cache = await caches.open(key);
      urls.push(...(await cache.keys()).map((request) => new URL(request.url).pathname));
    }
    return urls;
  });
  expect(cached).toContain('/dist/offline.html');
  expect(cached.filter((path) => path !== '/dist/offline.html' && !path.startsWith('/dist/assets/'))).toEqual([]);
});

test('the manifests and icons are served, in the language of the UI', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop', 'server files, checked once');
  for (const [path, lang] of [['/dist/manifest.webmanifest', 'mk'], ['/dist/manifest.en.webmanifest', 'en']] as const) {
    const response = await request.get(path);
    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toContain('application/manifest+json');
    const manifest = (await response.json()) as { lang: string; start_url: string; display: string; icons: { src: string; sizes: string }[] };
    // The start URL must lie inside the worker's /app/ scope, so a launch from the home screen gets the offline page.
    expect(manifest).toMatchObject({ lang, start_url: '/app/', display: 'standalone' });
    expect(manifest.icons.map((icon) => icon.sizes)).toEqual(['192x192', '512x512', '512x512']);
    for (const icon of manifest.icons) {
      const image = await request.get(icon.src);
      expect(image.status()).toBe(200);
      expect(image.headers()['content-type']).toBe('image/png');
    }
  }
  expect((await request.get('/app/')).status()).toBe(200);
  const worker = await request.get('/dist/sw.js');
  expect(worker.headers()['service-worker-allowed']).toBe('/');

  await useLanguage(page, 'en');
  await page.goto('/login');
  await expect(page.locator('link[rel="manifest"]')).toHaveAttribute('href', '/dist/manifest.en.webmanifest');
  await page.getByRole('combobox', { name: text('en', 'language.label') }).selectOption('mk');
  await expect(page.locator('link[rel="manifest"]')).toHaveAttribute('href', '/dist/manifest.webmanifest');
});

test('the newer pages fit a phone and the boards suggest a larger screen', async ({ page }, testInfo) => {
  test.setTimeout(120_000);
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('pwa-mobile'));
  const phone = testInfo.project.name === 'mobile';
  const note = page.getByTestId('large-screen-note');

  for (const path of ['toolbox', 'activities', 'toolbox/drawing-board', 'toolbox/geometry-board']) {
    await page.goto(`/app/w/${workspaceId}/${path}`);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    if (path.startsWith('toolbox/')) {
      const board = path.endsWith('drawing-board') ? page.locator('.excalidraw canvas.interactive') : page.getByRole('application');
      await expect(board).toBeVisible({ timeout: 30_000 });
      if (phone) await expect(note).toHaveText(text(language, 'pwa.largeScreen'));
      else await expect(note).toBeHidden();
    }
    if (phone) expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), path).toBe(true);
  }
});
