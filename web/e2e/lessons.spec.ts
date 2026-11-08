import { expect, test } from '@playwright/test';
import { signedInTeacher, text, uniqueEmail, useLanguage, xsrf } from './support.ts';

// R2-P4 acceptance: the teacher schedules a lesson from the calendar, sees it there, and an overlapping one is refused.
test('a teacher schedules a lesson and the calendar refuses an overlap', async ({ page }) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('lessons'));
  expect((await page.request.post(`/api/w/${workspaceId}/students`, {
    data: { firstName: 'Ана', lastName: 'Илиева' },
    headers: { 'X-XSRF-TOKEN': await xsrf(page.request) },
  })).status()).toBe(201);

  const day = '2030-11-13';
  await page.goto(`/app/w/${workspaceId}/calendar?view=week&date=${day}`);
  await page.getByRole('link', { name: text(language, 'lessons.add') }).click();
  await expect(page.getByLabel(text(language, 'lessons.fields.date'))).toHaveValue(day);
  await page.getByLabel(text(language, 'lessons.fields.student'), { exact: true }).selectOption({ label: 'Ана Илиева' });
  await page.getByLabel(text(language, 'lessons.fields.title')).fill('Дропки');
  await page.getByLabel(text(language, 'lessons.fields.time')).fill('17:00');
  await page.getByRole('button', { name: text(language, 'lessons.create') }).click();
  await expect(page).toHaveURL(new RegExp(`/app/w/${workspaceId}/lessons/[0-9a-f-]{36}$`));

  await page.getByRole('link', { name: text(language, 'lessons.back') }).click();
  const days = page.getByRole('list', { name: text(language, 'lessons.calendar.days') });
  await expect(days.getByRole('link', { name: /17:00–18:00 Дропки/ })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  // Half an hour later overlaps the first lesson.
  await page.getByRole('link', { name: text(language, 'lessons.add') }).click();
  await page.getByLabel(text(language, 'lessons.fields.student'), { exact: true }).selectOption({ label: 'Ана Илиева' });
  await page.getByLabel(text(language, 'lessons.fields.title')).fill('Повторување');
  await page.getByLabel(text(language, 'lessons.fields.time')).fill('17:30');
  await page.getByRole('button', { name: text(language, 'lessons.create') }).click();
  await expect(page.getByRole('alert')).toHaveText(text(language, 'errors.codes.LESSON_OVERLAP'));

  await page.goto(`/app/w/${workspaceId}/calendar?view=month&date=${day}`);
  await expect(page.getByRole('button', { name: new RegExp(text(language, 'lessons.calendar.count_one', { count: 1 })) })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
