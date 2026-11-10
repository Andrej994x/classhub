import { expect, test, type Page } from '@playwright/test';
import { grantPlatformOwner, signIn, signedInTeacher, teacherViaApi, text, totp, uniqueEmail, useLanguage } from './support.ts';

/**
 * Release 3 (R3-P8): the Teaching Toolbox end to end, through the UI.
 *  1. A teacher finds tools by subject and name, opens an external tool on its official site, builds a graph on the
 *     geometry board and a drawing on the drawing board, and keeps both in the Activity Library; there they filter,
 *     duplicate, rename, reopen and delete. The toolbox then shows the workspace's own usage.
 *  2. The platform owner adds a tool that teachers find and open at once, sees it in the platform usage totals, and
 *     deactivates it: teachers no longer see it.
 * The per-phase suites cover the rest (toolbox, drawing, geometry, pwa).
 */
// In Macedonian, the default: signing in switches the UI to the account's language.
const L = 'mk' as const;

const GEOMETRY = 'Геометриска табла';
const DRAWING = 'Табла за цртање';

/** The usage card's rows as "name value" lines. */
async function usageRows(page: Page) {
  const card = page.getByRole('region', { name: text(L, 'toolbox.usage.title') });
  await expect(card).toBeVisible();
  return (await card.locator('dl > div').allInnerTexts()).map((row) => row.replaceAll(/\s+/g, ' ').trim());
}

test('a teacher uses the toolbox, both boards and the Activity Library', async ({ page, context }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'The boards are for desktops and tablets (the phone note is checked in pwa.spec.ts).');
  test.setTimeout(180_000);
  // No code runs outside the CSP (the geometry board parses terms, never evaluates them) and nothing completes to
  // another host. Excalidraw's font fallbacks to its CDN are blocked by font-src as designed (drawing.spec.ts).
  const problems: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error' && /EvalError|script-src|unsafe-eval/i.test(message.text())) problems.push(message.text());
  });
  page.on('requestfinished', (request) => {
    const url = new URL(request.url());
    if (url.protocol.startsWith('http') && !['127.0.0.1', 'localhost'].includes(url.hostname)) problems.push(`foreign: ${request.url()}`);
  });
  await context.route('https://www.geogebra.org/**', (route) => route.fulfill({ status: 200, contentType: 'text/html', body: '<title>GeoGebra</title>' }));
  await useLanguage(page, L);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('r3-journey'));
  const toolbox = `/app/w/${workspaceId}/toolbox`;

  // 1. Find tools: by subject, then by name; open GeoGebra on its official site.
  await page.goto(toolbox);
  expect(await usageRows(page)).toEqual([]);
  await expect(page.getByText(text(L, 'toolbox.usage.none'))).toBeVisible();
  const tools = page.getByRole('list', { name: text(L, 'toolbox.tools') });
  await page.getByLabel(text(L, 'toolbox.subject')).selectOption({ label: 'Математика' });
  await expect(tools.getByRole('article', { name: 'GeoGebra' })).toBeVisible();
  await expect(tools.getByRole('article', { name: GEOMETRY })).toBeVisible(); // general tools stay in every subject
  await page.getByRole('searchbox', { name: text(L, 'toolbox.search') }).fill('geogebra');
  await expect(tools.getByRole('article')).toHaveCount(1);
  const [popup] = await Promise.all([page.waitForEvent('popup'), tools.getByRole('link', { name: new RegExp(text(L, 'toolbox.open', { name: 'GeoGebra' })) }).click()]);
  await popup.waitForLoadState();
  expect(popup.url()).toBe('https://www.geogebra.org/classic');
  await popup.close();

  // 2. A graph on the geometry board, saved with a subject.
  await page.getByRole('searchbox', { name: text(L, 'toolbox.search') }).fill('');
  await page.getByLabel(text(L, 'toolbox.subject')).selectOption({ label: text(L, 'toolbox.allSubjects') });
  await tools.getByRole('link', { name: text(L, 'toolbox.open', { name: GEOMETRY }) }).click();
  await expect(page.getByRole('application')).toBeVisible({ timeout: 30_000 });
  const graphs = page.getByRole('form', { name: text(L, 'boards.geometry.functions.title') });
  await graphs.getByLabel('f(x) =').fill('sin(x)');
  await graphs.getByRole('button', { name: text(L, 'boards.geometry.functions.add') }).click();
  await expect(page.getByRole('list', { name: text(L, 'boards.geometry.functions.list') })).toContainText('f(x) = sin(x)');
  await page.getByLabel(text(L, 'activities.titleField')).fill('Синусоида');
  await page.getByLabel(text(L, 'toolbox.subject')).selectOption({ label: 'Математика' });
  await page.getByRole('button', { name: text(L, 'boards.save') }).click();
  await expect(page.getByText(text(L, 'boards.saved'))).toBeVisible();

  // 3. A drawing on the drawing board.
  await page.goto(toolbox);
  await tools.getByRole('link', { name: text(L, 'toolbox.open', { name: DRAWING }) }).click();
  const canvas = page.locator('.excalidraw canvas.interactive');
  await expect(canvas).toBeVisible({ timeout: 30_000 });
  await page.getByTitle(/Rectangle/).first().click();
  const box = (await canvas.boundingBox())!;
  await page.mouse.move(box.x + box.width * 0.3, box.y + box.height * 0.3);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 0.5, box.y + box.height * 0.5, { steps: 8 });
  await page.mouse.up();
  await page.getByLabel(text(L, 'activities.titleField')).fill('Мапа на училницата');
  await page.getByRole('button', { name: text(L, 'boards.save') }).click();
  await expect(page.getByText(text(L, 'boards.saved'))).toBeVisible();

  // 4. The Activity Library: filter, duplicate, rename, reopen, delete.
  await page.goto(`/app/w/${workspaceId}/activities`);
  const library = page.getByRole('list', { name: text(L, 'activities.title') });
  await expect(library.getByRole('article')).toHaveCount(2);
  await page.getByLabel(text(L, 'activities.board')).selectOption({ label: text(L, 'activities.boards.geometry-board') });
  await expect(library.getByRole('article')).toHaveCount(1);
  const sine = library.getByRole('article', { name: 'Синусоида', exact: true });
  await expect(sine).toContainText('Математика');
  await sine.getByRole('button', { name: text(L, 'activities.duplicate') }).click();
  const copy = library.getByRole('article', { name: 'Синусоида (копија)', exact: true });
  await expect(copy).toBeVisible();
  await copy.getByRole('button', { name: text(L, 'activities.renameButton') }).click();
  const rename = library.getByRole('form', { name: text(L, 'activities.rename', { title: 'Синусоида (копија)' }) });
  await rename.getByLabel(text(L, 'activities.titleField')).fill('Синусоида — домашна');
  await rename.getByRole('button', { name: text(L, 'activities.save') }).click();
  const homework = library.getByRole('article', { name: 'Синусоида — домашна' });
  await expect(homework).toBeVisible();
  await homework.getByRole('link', { name: text(L, 'activities.open', { title: 'Синусоида — домашна' }) }).click();
  await expect(page.getByRole('list', { name: text(L, 'boards.geometry.functions.list') })).toContainText('f(x) = sin(x)', { timeout: 30_000 });
  await expect(page.getByLabel(text(L, 'activities.titleField'))).toHaveValue('Синусоида — домашна');
  await page.goto(`/app/w/${workspaceId}/activities`);
  const reopened = library.getByRole('article', { name: 'Синусоида — домашна' });
  await reopened.getByRole('button', { name: text(L, 'activities.delete') }).click();
  await reopened.getByRole('button', { name: text(L, 'activities.deleteConfirm') }).click();
  await expect(library.getByRole('article')).toHaveCount(2);
  await page.getByRole('searchbox', { name: text(L, 'activities.search') }).fill('мапа');
  await expect(library.getByRole('article')).toHaveCount(1);
  await expect(library.getByRole('article', { name: 'Мапа на училницата' })).toBeVisible();

  // 5. The workspace's own usage: the external tool and both boards opened once; one graph created, reused twice
  // (duplicated and reopened); one drawing created.
  await page.goto(toolbox);
  expect(await usageRows(page)).toEqual(
    expect.arrayContaining([
      `GeoGebra ${text(L, 'toolbox.usage.opens_one', { count: 1 })}`,
      `${GEOMETRY} ${text(L, 'toolbox.usage.opens_one', { count: 1 })}`,
      `${DRAWING} ${text(L, 'toolbox.usage.opens_one', { count: 1 })}`,
      `${GEOMETRY} ${text(L, 'toolbox.usage.activities', { created: 1, reused: 2 })}`,
      `${DRAWING} ${text(L, 'toolbox.usage.activities', { created: 1, reused: 0 })}`,
    ]),
  );
  expect(problems).toEqual([]);
});

test('the platform owner adds a tool teachers use, sees it in the usage totals and deactivates it', async ({ page, browser }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'Staff pages are checked on phones in admin.spec.ts.');
  test.setTimeout(150_000);
  const ownerEmail = uniqueEmail('r3-owner');
  const code = `fractions-${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
  const name = `Fractions lab ${code.slice(-6)}`;
  const nameMk = `Лабораторија за дропки ${code.slice(-6)}`; // what teachers see in Macedonian

  // The owner (granted with the CLI) enrolls the authenticator on the first visit to the staff area.
  await teacherViaApi(page.request, ownerEmail);
  await grantPlatformOwner(ownerEmail);
  await page.context().clearCookies();
  await useLanguage(page, L);
  await page.goto('/admin/toolbox');
  await signIn(page, L, ownerEmail);
  await page.getByRole('button', { name: text(L, 'admin.mfa.start') }).click();
  const key = (await page.getByLabel(text(L, 'admin.mfa.key')).textContent())!.trim();
  await page.getByLabel(text(L, 'admin.mfa.code')).fill(totp(key));
  await page.getByRole('button', { name: text(L, 'admin.mfa.confirm') }).click();
  await page.getByRole('button', { name: text(L, 'admin.mfa.codesDone') }).click();
  await page.goto('/admin/toolbox');

  // Add an external tool on an approved site, for mathematics.
  const add = page.getByRole('form', { name: text(L, 'admin.toolbox.add') });
  await add.getByLabel(text(L, 'admin.toolbox.code')).fill(code);
  await add.getByLabel(text(L, 'admin.toolbox.nameMk')).fill(nameMk);
  await add.getByLabel(text(L, 'admin.toolbox.nameEn')).fill(name);
  await add.getByLabel(text(L, 'admin.toolbox.descriptionMk')).fill('Дропки со модели.');
  await add.getByLabel(text(L, 'admin.toolbox.descriptionEn')).fill('Fractions with models.');
  await add.getByLabel(text(L, 'admin.toolbox.url')).fill('https://phet.colorado.edu/en/simulations/fractions-intro');
  await add.getByRole('checkbox', { name: 'Математика' }).check();
  await add.getByRole('button', { name: text(L, 'admin.toolbox.create') }).click();
  await expect(add.getByText(text(L, 'admin.toolbox.saved'))).toBeVisible();

  // A teacher finds it and opens it on its official site.
  const teacherContext = await browser.newContext();
  const teacher = await teacherContext.newPage();
  await teacherContext.route('https://phet.colorado.edu/**', (route) => route.fulfill({ status: 200, contentType: 'text/html', body: '<title>PhET</title>' }));
  await useLanguage(teacher, L);
  const { workspaceId } = await signedInTeacher(teacher, uniqueEmail('r3-teacher'));
  await teacher.goto(`/app/w/${workspaceId}/toolbox`);
  await teacher.getByRole('searchbox', { name: text(L, 'toolbox.search') }).fill(nameMk);
  const card = teacher.getByRole('list', { name: text(L, 'toolbox.tools') }).getByRole('article', { name: nameMk });
  const [popup] = await Promise.all([teacher.waitForEvent('popup'), card.getByRole('link', { name: new RegExp(text(L, 'toolbox.open', { name: nameMk })) }).click()]);
  await popup.waitForLoadState();
  expect(popup.url()).toBe('https://phet.colorado.edu/en/simulations/fractions-intro');
  await popup.close();

  // The platform totals count the open, by tool code only.
  await page.goto('/admin/usage');
  const opened = page.getByRole('table', { name: text(L, 'admin.usage.tools') }).getByRole('row', { name: code });
  await expect(opened.getByRole('cell')).toHaveText(['1']);

  // Deactivated: teachers no longer see it.
  await page.goto('/admin/toolbox');
  await page.getByRole('button', { name: text(L, 'admin.toolbox.edit', { name }) }).click();
  const edit = page.getByRole('form', { name: text(L, 'admin.toolbox.edit', { name }) });
  await edit.getByRole('checkbox', { name: text(L, 'admin.toolbox.active') }).uncheck();
  await edit.getByRole('button', { name: text(L, 'admin.toolbox.save') }).click();
  await expect(page.getByRole('list', { name: text(L, 'admin.toolbox.title') }).getByRole('listitem').filter({ hasText: code })).toContainText(text(L, 'admin.toolbox.inactive'));
  await teacher.reload();
  await teacher.getByRole('searchbox', { name: text(L, 'toolbox.search') }).fill(nameMk);
  await expect(teacher.getByText(text(L, 'toolbox.none'))).toBeVisible();
  await teacherContext.close();
});
