import { devices, expect, test, type Page } from '@playwright/test';
import { signedInTeacher, text, uniqueEmail, useLanguage } from './support.ts';

const language = 'mk' as const;

type Activity = { document: { format: string; objects: { id: string; type: string }[] } };

/**
 * R3-P4 acceptance: triangle → save → reopen. A teacher opens the geometry board from the toolbox, builds a triangle from
 * three clicks, saves it into the Activity Library, opens it again from the library and saves it again: the triangle
 * and its points are still in the construction. No CSP violations (JSXGraph runs without eval) and nothing completes to
 * another host. On a desktop and on a tablet.
 */
async function triangleSaveReopen(page: Page) {
  test.setTimeout(120_000);
  const problems: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error' && /Content Security Policy|EvalError/i.test(message.text())) problems.push(message.text());
  });
  page.on('requestfinished', (request) => {
    const host = new URL(request.url()).hostname;
    if (request.url().startsWith('http') && host !== '127.0.0.1' && host !== 'localhost') problems.push(`foreign: ${request.url()}`);
  });
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('geometry'));

  await page.goto(`/app/w/${workspaceId}/toolbox`);
  await page.getByRole('link', { name: text(language, 'toolbox.open', { name: 'Геометриска табла' }) }).click();
  await expect(page).toHaveURL(new RegExp(`/app/w/${workspaceId}/toolbox/geometry-board$`));
  const boardArea = page.getByRole('application');
  await expect(boardArea).toBeVisible({ timeout: 30_000 });

  // Three clicks with the triangle tool.
  await page.getByRole('toolbar', { name: text(language, 'boards.geometry.tools') }).getByRole('button', { name: text(language, 'boards.geometry.tool.triangle') }).click();
  const box = (await boardArea.boundingBox())!;
  for (const [fx, fy] of [[0.3, 0.7], [0.7, 0.7], [0.3, 0.3]] as const) {
    await page.mouse.click(box.x + box.width * fx, box.y + box.height * fy);
  }
  await expect(boardArea).toHaveAccessibleName(text(language, 'boards.geometry.board_other', { count: 4 }));

  await page.getByLabel(text(language, 'activities.titleField')).fill('Правоаголен триаголник');
  await page.getByRole('button', { name: text(language, 'boards.save') }).click();
  await expect(page.getByText(text(language, 'boards.saved'))).toBeVisible();
  await expect(page).toHaveURL(/\?activity=[0-9a-f-]{36}$/);
  const id = new URL(page.url()).searchParams.get('activity')!;
  const saved = (await (await page.request.get(`/api/w/${workspaceId}/activities/${id}`)).json()) as Activity;
  expect(saved.document.format).toBe('classhub.geometry');
  expect(saved.document.objects.map((o) => o.type)).toEqual(['point', 'point', 'point', 'triangle']);

  // Reopen from the library and save again: the construction came back.
  await page.goto(`/app/w/${workspaceId}/activities`);
  await page.getByRole('link', { name: text(language, 'activities.open', { title: 'Правоаголен триаголник' }) }).click();
  await expect(page.getByRole('application')).toHaveAccessibleName(text(language, 'boards.geometry.board_other', { count: 4 }), { timeout: 30_000 });
  await page.getByLabel(text(language, 'activities.titleField')).fill('Триаголник — повторување');
  await page.getByRole('button', { name: text(language, 'boards.save') }).click();
  await expect(page.getByText(text(language, 'boards.saved'))).toBeVisible();
  const again = (await (await page.request.get(`/api/w/${workspaceId}/activities/${id}`)).json()) as Activity;
  expect(again.document.objects.map((o) => o.type)).toEqual(['point', 'point', 'point', 'triangle']);

  expect(problems).toEqual([]);
}

test('triangle → save → reopen on a desktop', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'The geometry board is for desktops and tablets.');
  await triangleSaveReopen(page);
});

test('triangle → save → reopen on a tablet', async ({ browser }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'One tablet run is enough (it creates its own tablet context).');
  const tablet = await browser.newContext({ ...devices['iPad (gen 7)'], baseURL: testInfo.project.use.baseURL });
  try {
    await triangleSaveReopen(await tablet.newPage());
  } finally {
    await tablet.close();
  }
});
