import { devices, expect, test } from '@playwright/test';
import { openEmailLink, PASSWORD, signedInTeacher, text, uniqueEmail, useLanguage, xsrf } from './support.ts';

const PDF = Buffer.from('%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << >>\n%%EOF\n', 'ascii');

// R2-P9 acceptance: the teacher assigns homework; the student hands in an answer with a file on a phone; the teacher
// reviews it; the student sees the review and can no longer change the answer.
test('assign → submit → review: the student sees the teachers review', async ({ page, browser }) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('teacher'));
  const studentEmail = uniqueEmail('student');
  const headers = { 'X-XSRF-TOKEN': await xsrf(page.request) };
  const created = await page.request.post(`/api/w/${workspaceId}/students`, { data: { firstName: 'Марко', lastName: 'Стојанов', email: studentEmail }, headers });
  expect(created.status()).toBe(201);
  const studentId = ((await created.json()) as { id: string }).id;
  expect((await page.request.post(`/api/w/${workspaceId}/students/${studentId}/invitation`, { data: {}, headers })).status()).toBe(200);

  // The teacher creates the homework and assigns it.
  await page.goto(`/app/w/${workspaceId}/homework/new`);
  await page.getByLabel(text(language, 'homework.fields.title')).fill('Линеарни равенки');
  await page.getByLabel(text(language, 'homework.fields.description')).fill('Решете ги задачите 1–5.');
  await page.getByRole('button', { name: text(language, 'homework.create') }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Линеарни равенки' })).toBeVisible();
  await page.getByLabel(text(language, 'homework.assignments.toStudent')).selectOption(studentId);
  await page.getByRole('button', { name: text(language, 'homework.assignments.assign') }).first().click();
  const students = page.getByRole('list', { name: text(language, 'homework.assignments.title') });
  await expect(students.getByText(text(language, 'homework.status.Assigned'))).toBeVisible();
  const homeworkUrl = page.url();

  // The student accepts the invitation on a phone and hands in an answer with a file.
  const phoneContext = await browser.newContext({ ...devices['Pixel 7'] });
  const phone = await phoneContext.newPage();
  await useLanguage(phone, language);
  await openEmailLink(phone, language, studentEmail, '/student/invitation');
  await phone.getByLabel(text(language, 'auth.fields.password'), { exact: true }).fill(PASSWORD);
  await phone.getByLabel(text(language, 'auth.fields.confirmPassword')).fill(PASSWORD);
  await phone.getByRole('button', { name: text(language, 'studentPortal.invitation.create') }).click();
  await expect(phone).toHaveURL(new RegExp(`/student/w/${workspaceId}$`));
  await phone.getByRole('list', { name: text(language, 'homework.portal.title') }).getByRole('link', { name: /Линеарни равенки/ }).click();
  await expect(phone.getByText('Решете ги задачите 1–5.')).toBeVisible();
  await phone.getByLabel(text(language, 'homework.portal.text'), { exact: true }).fill('x = 4, y = −2');
  await phone.getByLabel(text(language, 'homework.portal.file')).setInputFiles({ name: 'Решение.pdf', mimeType: 'application/pdf', buffer: PDF });
  await phone.getByRole('button', { name: text(language, 'homework.portal.submit') }).click();
  await expect(phone.getByRole('button', { name: text(language, 'homework.portal.resubmit') })).toBeVisible();
  await expect(phone.getByRole('link', { name: text(language, 'homework.portal.ownFile', { name: 'Решение.pdf' }) })).toBeVisible();
  expect(await phone.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  // The teacher sees the answer, downloads the file and reviews it.
  await page.goto(homeworkUrl);
  await expect(students.getByText('x = 4, y = −2')).toBeVisible();
  const download = students.getByRole('link', { name: text(language, 'homework.downloadAnswer', { name: 'Марко Стојанов' }) });
  const file = await page.request.get((await download.getAttribute('href'))!);
  expect(file.status()).toBe(200);
  expect(Buffer.from(await file.body()).equals(PDF)).toBe(true);
  await page.getByLabel(text(language, 'homework.review.comment', { name: 'Марко Стојанов' })).fill('Точно, браво!');
  await page.getByRole('button', { name: text(language, 'homework.review.submit') }).click();
  await expect(students.getByText(text(language, 'homework.status.Reviewed'))).toBeVisible();

  // The student sees the review; the answer is final.
  await phone.reload();
  await expect(phone.getByText('Точно, браво!')).toBeVisible();
  await expect(phone.getByText(text(language, 'homework.portal.final'))).toBeVisible();
  await expect(phone.getByLabel(text(language, 'homework.portal.text'), { exact: true })).toHaveCount(0);
  await phoneContext.close();
});
