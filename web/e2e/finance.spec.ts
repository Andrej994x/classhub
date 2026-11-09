import { expect, test } from '@playwright/test';
import { signedInTeacher, text, uniqueEmail, useLanguage, xsrf } from './support.ts';

// R2-P10 acceptance: the teacher assigns a package, a held lesson with the student present uses one lesson, a partial
// payment is recorded, and the balance and the remaining lessons are shown (also on the finance page).
test('package → held lesson consumes one lesson → partial payment → balance', async ({ page }) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('finance'));
  const headers = async () => ({ 'X-XSRF-TOKEN': await xsrf(page.request) });
  const created = await page.request.post(`/api/w/${workspaceId}/students`, { data: { firstName: 'Сара', lastName: 'Јовановска' }, headers: await headers() });
  expect(created.status()).toBe(201);
  const studentId = ((await created.json()) as { id: string }).id;

  // A custom package of 4 lessons for 4.800,00 MKD.
  await page.goto(`/app/w/${workspaceId}/students/${studentId}`);
  await page.getByRole('button', { name: text(language, 'finance.package.add') }).click();
  await page.getByLabel(text(language, 'finance.package.name')).fill('Пакет 4 часа');
  await page.getByLabel(text(language, 'finance.package.lessonCount')).fill('4');
  await page.getByLabel(text(language, 'finance.package.price')).fill('4800');
  await page.getByRole('button', { name: text(language, 'finance.package.assign'), exact: true }).click();
  const packages = page.getByRole('list', { name: text(language, 'finance.package.title') });
  await expect(packages.getByText(text(language, 'finance.package.remaining', { remaining: 4, total: 4 }))).toBeVisible();

  // A past lesson held with the student present.
  const lesson = await page.request.post(`/api/w/${workspaceId}/lessons`, {
    data: { kind: 'Individual', studentId, title: 'Англиски', startLocal: '2024-05-14T10:00:00', durationMinutes: 60, mode: 'Online' },
    headers: await headers(),
  });
  expect(lesson.status()).toBe(201);
  const { id: lessonId, version } = (await lesson.json()) as { id: string; version: number };
  expect((await page.request.post(`/api/w/${workspaceId}/lessons/${lessonId}/complete`, { data: { version }, headers: await headers() })).status()).toBe(200);

  // Finance processes the held lesson in the background: one lesson is used.
  await expect(async () => {
    await page.reload();
    await expect(packages.getByText(text(language, 'finance.package.remaining', { remaining: 3, total: 4 }))).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });

  // A partial payment.
  await page.getByRole('button', { name: text(language, 'finance.payment.add') }).click();
  await page.getByLabel(text(language, 'finance.payment.amount')).fill('2000');
  await page.getByRole('button', { name: text(language, 'finance.payment.record') }).last().click();
  const balances = page.getByRole('list', { name: text(language, 'finance.balances') });
  await expect(balances.getByText(/2[.,]800[.,]00/)).toBeVisible();
  await expect(page.getByRole('list', { name: text(language, 'finance.history.title') }).getByText(text(language, 'finance.history.kinds.LessonConsumed'))).toBeVisible();

  // The finance page shows the same.
  await page.goto(`/app/w/${workspaceId}/finance`);
  const students = page.getByRole('list', { name: text(language, 'finance.overview.title') });
  await expect(students.getByRole('link', { name: 'Сара Јовановска' })).toBeVisible();
  await expect(students.getByText(/2[.,]800[.,]00/)).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
