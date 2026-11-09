import { expect, test } from '@playwright/test';
import { signedInTeacher, text, uniqueEmail, useLanguage } from './support.ts';

// R3-P1 acceptance: a teacher searches the toolbox and opens an external tool on its official site, in a new tab that
// cannot reach back to ClassHub (no opener). The external site is answered locally: tests never leave the machine.
test('a teacher finds GeoGebra in the toolbox and opens it in a new tab', async ({ page, context }, testInfo) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('toolbox'));
  await context.route('https://www.geogebra.org/**', (route) => route.fulfill({ status: 200, contentType: 'text/html', body: '<title>GeoGebra</title><p>GeoGebra</p>' }));

  await page.goto(`/app/w/${workspaceId}`);
  if (testInfo.project.name === 'mobile') await page.getByRole('button', { name: text(language, 'common.openMenu') }).click();
  await page.getByRole('link', { name: text(language, 'nav.toolbox') }).first().click();
  await expect(page).toHaveURL(new RegExp(`/app/w/${workspaceId}/toolbox$`));
  await page.getByRole('searchbox', { name: text(language, 'toolbox.search') }).fill('GeoGebra');
  const tools = page.getByRole('list', { name: text(language, 'toolbox.tools') });
  await expect(tools.getByRole('article')).toHaveCount(1);
  const card = tools.getByRole('article', { name: 'GeoGebra' });
  const open = card.getByRole('link', { name: new RegExp(text(language, 'toolbox.open', { name: 'GeoGebra' })) });
  await expect(open).toHaveAttribute('rel', 'noopener noreferrer');

  const [popup] = await Promise.all([page.waitForEvent('popup'), open.click()]);
  await popup.waitForLoadState();
  expect(popup.url()).toBe('https://www.geogebra.org/classic');
  expect(await popup.evaluate(() => window.opener)).toBeNull();
  await popup.close();
  if (testInfo.project.name === 'mobile') expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
