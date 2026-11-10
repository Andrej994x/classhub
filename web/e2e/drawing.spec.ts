import { devices, expect, test, type Page } from '@playwright/test';
import { signedInTeacher, text, uniqueEmail, useLanguage } from './support.ts';

const language = 'mk' as const;

type Activity = { id: string; title: string; document: { elements: { type: string }[] } };

const isForeign = (href: string) => {
  const url = new URL(href);
  return ['http:', 'https:'].includes(url.protocol) && url.hostname !== 'localhost' && url.hostname !== '127.0.0.1';
};

/**
 * Nothing leaves ClassHub: fonts and assets are self-hosted. Excalidraw always lists its CDN as a last fallback for
 * each font; the CSP blocks those attempts in the browser (they fail as "csp" and are never sent), so no request to
 * another host may complete, and every foreign attempt must have been blocked.
 */
function watchOrigins(page: Page) {
  const completed: string[] = [];
  const notBlocked: string[] = [];
  page.on('requestfinished', (request) => {
    if (isForeign(request.url())) completed.push(request.url());
  });
  page.on('requestfailed', (request) => {
    if (isForeign(request.url()) && request.failure()?.errorText !== 'csp') notBlocked.push(`${request.failure()?.errorText} ${request.url()}`);
  });
  return { completed, notBlocked };
}

/**
 * R3-P3 acceptance: draw → save → reopen. A teacher opens the drawing board from the toolbox, draws a rectangle, saves it
 * into the Activity Library, opens it again from the library and saves it again: the rectangle is still there, so the
 * board reopened the saved scene. On desktop and on a tablet.
 */
async function drawSaveReopen(page: Page) {
  test.setTimeout(120_000);
  const foreign = watchOrigins(page);
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('drawing'));

  await page.goto(`/app/w/${workspaceId}/toolbox`);
  await page.getByRole('link', { name: text(language, 'toolbox.open', { name: 'Табла за цртање' }) }).click();
  await expect(page).toHaveURL(new RegExp(`/app/w/${workspaceId}/toolbox/drawing-board$`));

  // Draw a rectangle on the canvas.
  const canvas = page.locator('.excalidraw canvas.interactive');
  await expect(canvas).toBeVisible({ timeout: 30_000 });
  await page.getByTitle(/Rectangle/).first().click();
  const box = (await canvas.boundingBox())!;
  await page.mouse.move(box.x + box.width * 0.3, box.y + box.height * 0.3);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 0.5, box.y + box.height * 0.5, { steps: 8 });
  await page.mouse.up();

  // Save it into the library.
  await page.getByLabel(text(language, 'activities.titleField')).fill('Агли во триаголник');
  await page.getByRole('button', { name: text(language, 'boards.save') }).click();
  await expect(page.getByText(text(language, 'boards.saved'))).toBeVisible();
  await expect(page).toHaveURL(/\?activity=[0-9a-f-]{36}$/);
  const id = new URL(page.url()).searchParams.get('activity')!;
  const saved = (await (await page.request.get(`/api/w/${workspaceId}/activities/${id}`)).json()) as Activity;
  expect(saved.document.elements.map((e) => e.type)).toEqual(['rectangle']);

  // Reopen from the library and save again: the rectangle came back with the scene.
  await page.goto(`/app/w/${workspaceId}/activities`);
  await page.getByRole('link', { name: text(language, 'activities.open', { title: 'Агли во триаголник' }) }).click();
  await expect(page.locator('.excalidraw canvas.interactive')).toBeVisible({ timeout: 30_000 });
  await page.getByLabel(text(language, 'activities.titleField')).fill('Агли — повторување');
  await page.getByRole('button', { name: text(language, 'boards.save') }).click();
  await expect(page.getByText(text(language, 'boards.saved'))).toBeVisible();
  const again = (await (await page.request.get(`/api/w/${workspaceId}/activities/${id}`)).json()) as Activity;
  expect(again.document.elements.map((e) => e.type)).toEqual(['rectangle']);
  expect(again.title).toBe('Агли — повторување');

  // The board's own font comes from ClassHub.
  const selfHosted = page.waitForResponse((response) => response.url().includes('/excalidraw-assets/fonts/Excalifont/') && response.ok());
  expect(await page.evaluate(async () => (await document.fonts.load('20px Excalifont', 'Аа')).length)).toBeGreaterThan(0);
  await selfHosted;
  expect(foreign.completed, 'requests to other hosts').toEqual([]);
  expect(foreign.notBlocked, 'foreign attempts the CSP did not block').toEqual([]);
}

test('draw → save → reopen on a desktop', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'The drawing board is for desktops and tablets.');
  await drawSaveReopen(page);
});

test('draw → save → reopen on a tablet', async ({ browser }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'One tablet run is enough (it creates its own tablet context).');
  const tablet = await browser.newContext({ ...devices['iPad (gen 7)'], baseURL: testInfo.project.use.baseURL });
  try {
    await drawSaveReopen(await tablet.newPage());
  } finally {
    await tablet.close();
  }
});
