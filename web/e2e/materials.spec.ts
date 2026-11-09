import { devices, expect, test } from '@playwright/test';
import { openEmailLink, PASSWORD, signedInTeacher, text, uniqueEmail, useLanguage, xsrf } from './support.ts';

const PDF = Buffer.from('%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << >>\n%%EOF\n', 'ascii');

// R2-P8 acceptance: the teacher uploads a material and assigns it; the student downloads it from the portal, and nothing
// that was not assigned is reachable.
test('a teacher uploads and assigns a material that the student downloads in the portal', async ({ page, browser }) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('teacher'));
  const studentEmail = uniqueEmail('student');
  const headers = { 'X-XSRF-TOKEN': await xsrf(page.request) };
  const created = await page.request.post(`/api/w/${workspaceId}/students`, { data: { firstName: 'Marko', lastName: 'Stojanov', email: studentEmail }, headers });
  expect(created.status()).toBe(201);
  const studentId = ((await created.json()) as { id: string }).id;

  // Upload through the page.
  await page.goto(`/app/w/${workspaceId}/materials`);
  await expect(page.getByRole('heading', { level: 1, name: text(language, 'materials.title') })).toBeVisible();
  await page.getByLabel(text(language, 'materials.upload.file')).setInputFiles({ name: 'Linear equations.pdf', mimeType: 'application/pdf', buffer: PDF });
  await expect(page.getByLabel(text(language, 'materials.fields.title'))).toHaveValue('Linear equations');
  await page.getByRole('button', { name: text(language, 'materials.upload.submit') }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Linear equations' })).toBeVisible();
  const materialId = /\/materials\/([0-9a-f-]{36})$/.exec(page.url())![1]!;

  // A second material stays unassigned.
  const privateUpload = await page.request.post(`/api/w/${workspaceId}/materials`, {
    headers,
    multipart: { title: 'Only mine', file: { name: 'mine.pdf', mimeType: 'application/pdf', buffer: PDF } },
  });
  expect(privateUpload.status()).toBe(201);
  const privateId = ((await privateUpload.json()) as { id: string }).id;

  // Assign to the student.
  await page.getByLabel(text(language, 'materials.assignments.toStudent')).selectOption(studentId);
  await page.getByRole('button', { name: text(language, 'materials.assignments.assign') }).first().click();
  await expect(page.getByRole('list', { name: text(language, 'materials.assignments.title') }).getByRole('link', { name: /Marko Stojanov/ })).toBeVisible();

  // The student accepts the invitation on a phone and downloads the material.
  const invited = await page.request.post(`/api/w/${workspaceId}/students/${studentId}/invitation`, { data: {}, headers });
  expect(invited.status()).toBe(200);
  const phoneContext = await browser.newContext({ ...devices['Pixel 7'], acceptDownloads: true });
  const phone = await phoneContext.newPage();
  await useLanguage(phone, language);
  await openEmailLink(phone, language, studentEmail, '/student/invitation');
  await phone.getByLabel(text(language, 'auth.fields.password'), { exact: true }).fill(PASSWORD);
  await phone.getByLabel(text(language, 'auth.fields.confirmPassword')).fill(PASSWORD);
  await phone.getByRole('button', { name: text(language, 'studentPortal.invitation.create') }).click();
  await expect(phone).toHaveURL(new RegExp(`/student/w/${workspaceId}$`));

  const materials = phone.getByRole('list', { name: text(language, 'studentPortal.materials.title') });
  await expect(materials.getByText('Linear equations', { exact: true })).toBeVisible();
  await expect(materials.getByText('Only mine')).toHaveCount(0);
  const [download] = await Promise.all([
    phone.waitForEvent('download'),
    materials.getByRole('link', { name: text(language, 'studentPortal.materials.download', { title: 'Linear equations' }) }).click(),
  ]);
  expect(download.suggestedFilename()).toBe('Linear equations.pdf');
  const response = await phone.request.get(`/api/student/w/${workspaceId}/materials/${materialId}/file`);
  expect(response.status()).toBe(200);
  expect(Buffer.from(await response.body()).equals(PDF)).toBe(true);
  expect(response.headers()['x-content-type-options']).toBe('nosniff');
  expect(response.headers()['content-disposition']).toContain('attachment');
  expect(await phone.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  // Not assigned: not found for the student, and the teacher's routes stay closed.
  expect((await phone.request.get(`/api/student/w/${workspaceId}/materials/${privateId}/file`)).status()).toBe(404);
  expect((await phone.request.get(`/api/w/${workspaceId}/materials/${materialId}/file`)).status()).toBe(404);
  await phoneContext.close();
});
