import { devices, expect, test } from '@playwright/test';
import { openEmailLink, PASSWORD, signedInTeacher, text, uniqueEmail, useLanguage, xsrf } from './support.ts';

// R2-P3 acceptance: the teacher invites a student, who creates an account from the email link and lands in the portal.
test('a teacher invites a student who creates an account and sees the student portal', async ({ page, browser }) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('teacher'));
  const studentEmail = uniqueEmail('student');
  const created = await page.request.post(`/api/w/${workspaceId}/students`, {
    data: { firstName: 'Марко', lastName: 'Стојанов', email: studentEmail },
    headers: { 'X-XSRF-TOKEN': await xsrf(page.request) },
  });
  expect(created.status()).toBe(201);
  const studentId = ((await created.json()) as { id: string }).id;

  // The teacher sends the invitation from the student's page.
  await page.goto(`/app/w/${workspaceId}/students/${studentId}`);
  await expect(page.getByLabel(text(language, 'students.account.email'))).toHaveValue(studentEmail);
  await page.getByRole('button', { name: text(language, 'students.account.invite') }).click();
  await expect(page.getByText(text(language, 'students.account.sent'))).toBeVisible();

  // The student, on a phone without a session, opens the link from the email and creates the account.
  const phoneContext = await browser.newContext({ ...devices['Pixel 7'] });
  const phone = await phoneContext.newPage();
  await useLanguage(phone, language);
  await openEmailLink(phone, language, studentEmail, '/student/invitation');
  await expect(phone.getByRole('heading', { level: 1, name: text(language, 'studentPortal.invitation.title') })).toBeVisible();
  await expect(phone.getByLabel(text(language, 'auth.fields.email'))).toHaveValue(studentEmail);
  await phone.getByLabel(text(language, 'auth.fields.password'), { exact: true }).fill(PASSWORD);
  await phone.getByLabel(text(language, 'auth.fields.confirmPassword')).fill(PASSWORD);
  await phone.getByRole('button', { name: text(language, 'studentPortal.invitation.create') }).click();

  await expect(phone).toHaveURL(new RegExp(`/student/w/${workspaceId}$`));
  await expect(phone.getByText(text(language, 'studentPortal.as', { name: 'Марко Стојанов' }))).toBeVisible();
  expect(await phone.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  // The student's account sees nothing of the teacher's workspace.
  expect((await phone.request.get(`/api/w/${workspaceId}/students`)).status()).toBe(404);
  await phoneContext.close();

  // The teacher sees that the student now has an account.
  await page.reload();
  await expect(page.getByText(text(language, 'students.account.linked'))).toBeVisible();
});
