import { devices, expect, test, type Page } from '@playwright/test';
import { signedInTeacher, text, uniqueEmail, useLanguage } from './support.ts';

const language = 'mk' as const;

type Activity = { document: { format: string; version?: number; objects: { id: string; type: string; x?: number; y?: number; term?: string }[] } };

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
  await boardArea.scrollIntoViewIfNeeded();
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

  // Move the whole triangle by dragging its inside: all three points move, and that is what is saved.
  await page.getByRole('toolbar', { name: text(language, 'boards.geometry.tools') }).getByRole('button', { name: text(language, 'boards.geometry.tool.move') }).click();
  // The saved message above the board moved it: measure it again.
  await boardArea.scrollIntoViewIfNeeded();
  const now = (await boardArea.boundingBox())!;
  const inside = { x: now.x + now.width * 0.43, y: now.y + now.height * 0.57 };
  await page.mouse.move(inside.x, inside.y);
  await page.mouse.down();
  await page.mouse.move(inside.x + now.width * 0.1, inside.y, { steps: 10 });
  await page.mouse.up();
  await page.getByRole('button', { name: text(language, 'boards.save') }).click();
  await expect(page.getByText(text(language, 'boards.saved'))).toBeVisible();
  const dragged = (await (await page.request.get(`/api/w/${workspaceId}/activities/${id}`)).json()) as Activity;
  const shifts = dragged.document.objects.filter((o) => o.type === 'point').map((o, i) => [o.x! - saved.document.objects[i]!.x!, o.y! - saved.document.objects[i]!.y!]);
  expect(shifts).toHaveLength(3);
  for (const [dx, dy] of shifts) {
    expect(dx).toBeGreaterThan(0.5);
    expect(dx).toBeCloseTo(shifts[0]![0]!, 1);
    expect(dy).toBeCloseTo(shifts[0]![1]!, 1);
  }

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

/**
 * R3-P5 acceptance: plot → save → reopen. A function typed as a term of x is plotted (by ClassHub's safe parser, no eval:
 * no CSP violation), saved in format version 2, reopened from the library and saved again unchanged.
 */
test('plot → save → reopen a function graph', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'The geometry board is for desktops and tablets.');
  const problems: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error' && /Content Security Policy|EvalError/i.test(message.text())) problems.push(message.text());
  });
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('graphs'));
  await page.goto(`/app/w/${workspaceId}/toolbox/geometry-board`);
  await expect(page.getByRole('application')).toBeVisible({ timeout: 30_000 });

  const graphs = page.getByRole('form', { name: text(language, 'boards.geometry.functions.title') });
  await graphs.getByLabel('f(x) =').fill('alert(1)');
  await graphs.getByRole('button', { name: text(language, 'boards.geometry.functions.add') }).click();
  await expect(page.getByText(text(language, 'boards.geometry.functions.invalid'))).toBeVisible();
  await graphs.getByLabel('f(x) =').fill('x^2 - 2');
  await graphs.getByRole('button', { name: text(language, 'boards.geometry.functions.add') }).click();
  await expect(page.getByRole('list', { name: text(language, 'boards.geometry.functions.list') })).toContainText('f(x) = x^2 - 2');
  await expect(page.getByRole('application')).toHaveAccessibleName(text(language, 'boards.geometry.board_one', { count: 1 }));
  // The graph is drawn on the board (an SVG path from JSXGraph).
  await expect(page.locator('.jxgbox svg path[stroke="#7c3aed"]')).toBeVisible();

  await page.getByLabel(text(language, 'activities.titleField')).fill('Парабола');
  await page.getByRole('button', { name: text(language, 'boards.save') }).click();
  await expect(page.getByText(text(language, 'boards.saved'))).toBeVisible();
  const id = new URL(page.url()).searchParams.get('activity')!;
  const saved = (await (await page.request.get(`/api/w/${workspaceId}/activities/${id}`)).json()) as Activity;
  expect(saved.document.version).toBe(2);
  expect(saved.document.objects).toEqual([{ id: 'f', type: 'function', term: 'x^2 - 2' }]);

  await page.goto(`/app/w/${workspaceId}/activities`);
  await page.getByRole('link', { name: text(language, 'activities.open', { title: 'Парабола' }) }).click();
  await expect(page.getByRole('list', { name: text(language, 'boards.geometry.functions.list') })).toContainText('f(x) = x^2 - 2', { timeout: 30_000 });
  await page.getByLabel(text(language, 'activities.titleField')).fill('Парабола — повторување');
  await page.getByRole('button', { name: text(language, 'boards.save') }).click();
  await expect(page.getByText(text(language, 'boards.saved'))).toBeVisible();
  const again = (await (await page.request.get(`/api/w/${workspaceId}/activities/${id}`)).json()) as Activity;
  expect(again.document.objects).toEqual([{ id: 'f', type: 'function', term: 'x^2 - 2' }]);
  expect(problems).toEqual([]);
});
