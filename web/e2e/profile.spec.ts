import { expect, test } from '@playwright/test';
import { pngImage, signedInTeacher, text, uniqueEmail, useLanguage } from './support.ts';

// R1-P1 acceptance: the teacher completes the profile (incl. photo) and the checklist follows the data.
test('a teacher completes the profile with a photo and sees the checklist progress', async ({ page }) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('profile'));

  await page.goto('/app');
  await expect(page.getByText(text(language, 'onboarding.progress', { completed: 0, total: 5 }))).toBeVisible();
  await page.getByRole('link', { name: text(language, 'onboarding.steps.profile') }).click();
  await expect(page).toHaveURL(new RegExp(`/app/w/${workspaceId}/profile$`));

  await page.getByLabel(text(language, 'profile.photo.choose')).setInputFiles({ name: 'me.png', mimeType: 'image/png', buffer: pngImage(400, 400) });
  await expect(page.getByText(text(language, 'profile.photo.saved'))).toBeVisible();
  await expect(page.getByRole('img', { name: text(language, 'profile.photo.alt') })).toBeVisible();

  await page.getByLabel(text(language, 'profile.fields.firstName'), { exact: true }).fill('Ана');
  await page.getByLabel(text(language, 'profile.fields.lastName'), { exact: true }).fill('Петровска');
  await page.getByLabel(text(language, 'profile.fields.bio')).fill('Наставничка по математика со 10 години искуство.');
  await page.getByRole('radio', { name: text(language, 'profile.modes.Online'), exact: true }).check();
  await page.getByRole('checkbox', { name: 'Математика', exact: true }).check();
  await page.getByRole('checkbox', { name: 'Средно', exact: true }).check();
  await page.getByRole('checkbox', { name: 'Македонски', exact: true }).check();
  await page.getByLabel(text(language, 'profile.fields.country')).selectOption('MK');
  await page.getByRole('button', { name: text(language, 'profile.save') }).click();
  await expect(page.getByText(text(language, 'profile.saved'))).toBeVisible();

  await page.goto(`/app/w/${workspaceId}`);
  await expect(page.getByText(text(language, 'onboarding.progress', { completed: 2, total: 5 }))).toBeVisible();

  // Saved for real: a reload shows the stored profile.
  await page.goto(`/app/w/${workspaceId}/profile`);
  await expect(page.getByLabel(text(language, 'profile.fields.firstName'), { exact: true })).toHaveValue('Ана');
  await expect(page.getByRole('checkbox', { name: 'Математика', exact: true })).toBeChecked();
});
