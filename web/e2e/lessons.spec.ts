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

// R2-P5 acceptance: weekly lessons are scheduled ahead from the editor, shown in the calendar, and the series can end.
test('a teacher schedules weekly lessons and ends the series', async ({ page }) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('series'));
  expect((await page.request.post(`/api/w/${workspaceId}/students`, {
    data: { firstName: 'Марко', lastName: 'Петров' },
    headers: { 'X-XSRF-TOKEN': await xsrf(page.request) },
  })).status()).toBe(201);
  // Two days ahead, so the start is "today or later" in any time zone.
  const start = new Date(Date.now() + 2 * 86_400_000).toISOString().slice(0, 10);

  await page.goto(`/app/w/${workspaceId}/lessons/new?date=${start}`);
  await page.getByLabel(text(language, 'lessons.fields.student'), { exact: true }).selectOption({ label: 'Марко Петров' });
  await page.getByLabel(text(language, 'lessons.fields.title')).fill('Англиски');
  await page.getByLabel(text(language, 'lessons.series.repeat')).selectOption('1');
  await page.getByLabel(text(language, 'lessons.series.countLabel')).fill('4');
  await page.getByRole('button', { name: text(language, 'lessons.series.create') }).click();

  await expect(page).toHaveURL(new RegExp(`/app/w/${workspaceId}/series/[0-9a-f-]{36}$`));
  await expect(page.getByText(text(language, 'lessons.series.created_other', { count: 4 }))).toBeVisible();
  await page.goto(`/app/w/${workspaceId}/calendar?view=month&date=${start}`);
  await expect(page.getByRole('button', { name: new RegExp(text(language, 'lessons.calendar.count_one', { count: 1 })) }).first()).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  await page.goBack();
  await page.getByRole('button', { name: text(language, 'lessons.series.end') }).click();
  await page.getByRole('button', { name: text(language, 'lessons.series.endYes') }).click();
  await expect(page.getByText(text(language, 'lessons.series.ended'))).toBeVisible();
});

// R2-P7 acceptance: a held lesson records attendance, and the student's history shows it.
test('a teacher marks a lesson as held with attendance and sees it in the history', async ({ page }) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('attendance'));
  const headers = async () => ({ 'X-XSRF-TOKEN': await xsrf(page.request) });
  const created = await page.request.post(`/api/w/${workspaceId}/students`, { data: { firstName: 'Лена', lastName: 'Ристова' }, headers: await headers() });
  const studentId = ((await created.json()) as { id: string }).id;
  const lesson = await page.request.post(`/api/w/${workspaceId}/lessons`, {
    data: { kind: 'Individual', studentId, title: 'Геометрија', startLocal: '2024-03-05T17:00:00', durationMinutes: 60, mode: 'Online' },
    headers: await headers(),
  });
  const lessonId = ((await lesson.json()) as { id: string }).id;

  await page.goto(`/app/w/${workspaceId}/lessons/${lessonId}`);
  await page.getByRole('button', { name: text(language, 'lessons.complete') }).click();
  await page.getByRole('group', { name: 'Лена Ристова' }).getByLabel(text(language, 'lessons.attendance.statuses.Excused')).check();
  await page.getByRole('button', { name: text(language, 'lessons.attendance.confirmHeld') }).click();
  await expect(page.getByText(text(language, 'lessons.statuses.Completed'), { exact: true })).toBeVisible();

  await page.goto(`/app/w/${workspaceId}/students/${studentId}`);
  const history = page.getByRole('list', { name: text(language, 'lessons.history.title') });
  await expect(history.getByText(`${text(language, 'lessons.statuses.Completed')} · ${text(language, 'lessons.attendance.statuses.Excused')}`)).toBeVisible();
});
