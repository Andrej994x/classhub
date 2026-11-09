import { devices, expect, test, type Page } from '@playwright/test';
import { openEmailLink, PASSWORD, signedInTeacher, text, uniqueEmail, useLanguage, xsrf } from './support.ts';

const language = 'mk' as const;

/** At phone width nothing may scroll sideways. */
async function expectNoHorizontalScroll(page: Page, what: string) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow, `${what} scrolls sideways by ${overflow}px`).toBeLessThanOrEqual(0);
}

type Lesson = { id: string; kind: string; title: string; startLocal: string; endLocal: string; mode: string; meetingUrl: string | null; version: number; participants: { studentId: string }[] };

/**
 * R2-P13: the whole Release 2 journey. A visitor books a lesson on the public page (a request) → the teacher confirms it
 * (the visitor becomes a student, the lesson is scheduled) → invites the student → assigns a package → the lesson is held
 * → one package lesson is used → a partial payment → homework. The student, on a phone, sees the held lesson with their
 * attendance and the homework; the teacher's dashboard shows the student and what is owed. Desktop and phone.
 */
test('a visitor becomes a student with a held lesson, a package, a payment and homework', async ({ page, browser }, testInfo) => {
  test.setTimeout(180_000);
  const phoneRun = testInfo.project.name === 'mobile';
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('release2'));
  const headers = async () => ({ 'X-XSRF-TOKEN': await xsrf(page.request) });
  const slug = `r2-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;

  // Setup through the API (each part has its own UI test): a public offer, a published site and booking every day.
  const catalog = (await (await page.request.get('/api/catalog')).json()) as { subjects: { id: string; code: string }[] };
  const math = catalog.subjects.find((s) => s.code === 'math')!.id;
  expect((await page.request.post(`/api/w/${workspaceId}/offers`, {
    data: { kind: 'SingleLesson', title: 'Час по математика', subjectId: math, durationMinutes: 60, priceMinor: 120_000, mode: 'Online', isActive: true, showOnPublicSite: true },
    headers: await headers(),
  })).status()).toBe(201);
  const draft = (await (await page.request.get(`/api/w/${workspaceId}/site`)).json()) as { draft: unknown };
  const saved = (await (await page.request.put(`/api/w/${workspaceId}/site`, {
    data: { slug, template: 'classic', palette: 'ocean', language: 'mk', draft: draft.draft, version: null },
    headers: await headers(),
  })).json()) as { version: number };
  expect((await page.request.post(`/api/w/${workspaceId}/site/publish`, { data: { version: saved.version }, headers: await headers() })).ok()).toBe(true);
  expect((await page.request.put(`/api/w/${workspaceId}/booking/settings`, {
    data: {
      enabled: true,
      slotStepMinutes: 60,
      minNoticeHours: 0,
      horizonDays: 30,
      workingHours: [0, 1, 2, 3, 4, 5, 6].map((day) => ({ dayOfWeek: day, start: '09:00:00', end: '17:00:00' })),
      timeOff: [],
      version: 0,
    },
    headers: await headers(),
  })).ok()).toBe(true);

  // 1. The visitor books a time on the public page (on a phone, without a session).
  const studentEmail = uniqueEmail('visitor');
  const visitor = await browser.newContext({ ...devices['Pixel 7'] });
  const visitorPage = await visitor.newPage();
  await visitorPage.goto(`/${slug}/book`);
  const day = visitorPage.getByRole('region', { name: 'Слободни термини' }).getByRole('list').first().getByRole('listitem').nth(1);
  await day.getByRole('button').first().click();
  await visitorPage.getByLabel('Име и презиме', { exact: true }).fill('Марија Илиевска');
  await visitorPage.getByLabel('Е-пошта', { exact: true }).fill(studentEmail);
  await visitorPage.getByRole('checkbox').nth(0).check();
  await visitorPage.getByRole('checkbox').nth(1).check();
  await visitorPage.getByRole('button', { name: 'Испрати барање за термин' }).click();
  await expect(visitorPage.getByText(/Барањето е испратено/)).toBeVisible();
  await visitor.close();

  // 2. The teacher confirms it: the visitor is now a student with a scheduled lesson.
  await page.goto(`/app/w/${workspaceId}/booking`);
  const request = page.getByRole('list', { name: text(language, 'booking.requests.title') }).getByRole('listitem', { name: /Марија Илиевска/ });
  await request.getByRole('button', { name: text(language, 'booking.requests.confirm') }).click();
  await expect(request.getByText(text(language, 'booking.statuses.Confirmed'))).toBeVisible();
  await request.getByRole('link', { name: text(language, 'booking.requests.openLesson') }).click();
  await expect(page).toHaveURL(/\/lessons\/[0-9a-f-]{36}$/);
  const lessonId = /\/lessons\/([0-9a-f-]{36})$/.exec(page.url())![1]!;
  const booked = (await (await page.request.get(`/api/w/${workspaceId}/lessons/${lessonId}`)).json()) as Lesson;
  const studentId = booked.participants[0]!.studentId;

  // 3. On the student's page: the invitation and a package of 4 lessons for 4.800,00 MKD.
  await page.goto(`/app/w/${workspaceId}/students/${studentId}`);
  await expect(page.getByLabel(text(language, 'students.account.email'))).toHaveValue(studentEmail);
  await page.getByRole('button', { name: text(language, 'students.account.invite') }).click();
  await expect(page.getByText(text(language, 'students.account.sent'))).toBeVisible();
  await page.getByRole('button', { name: text(language, 'finance.package.add') }).click();
  await page.getByLabel(text(language, 'finance.package.name')).fill('Пакет 4 часа');
  await page.getByRole('spinbutton', { name: text(language, 'finance.package.lessonCount') }).fill('4');
  await page.getByLabel(text(language, 'finance.package.price')).fill('4800');
  await page.getByRole('button', { name: text(language, 'finance.package.assign'), exact: true }).click();
  const packages = page.getByRole('list', { name: text(language, 'finance.package.title') });
  await expect(packages.getByText(text(language, 'finance.package.remaining', { remaining: 4, total: 4 }))).toBeVisible();
  if (phoneRun) await expectNoHorizontalScroll(page, 'the student page');

  // 4. Time passes (the lesson is moved to yesterday through the API), then the teacher marks it as held.
  const yesterday = new Date(Date.now() - 24 * 3600_000).toISOString().slice(0, 10);
  const moved = await page.request.put(`/api/w/${workspaceId}/lessons/${lessonId}`, {
    data: {
      kind: booked.kind,
      studentId,
      title: booked.title,
      startLocal: `${yesterday}T10:00:00`,
      durationMinutes: (Date.parse(booked.endLocal) - Date.parse(booked.startLocal)) / 60_000,
      mode: booked.mode,
      meetingUrl: booked.meetingUrl,
      version: booked.version,
    },
    headers: await headers(),
  });
  expect(moved.status(), await moved.text()).toBe(200);
  await page.goto(`/app/w/${workspaceId}/lessons/${lessonId}`);
  await page.getByRole('button', { name: text(language, 'lessons.complete') }).click();
  await page.getByRole('button', { name: text(language, 'lessons.attendance.confirmHeld') }).click();
  await expect(page.getByText(text(language, 'lessons.statuses.Completed'), { exact: true })).toBeVisible();

  // 5. Finance uses one package lesson in the background; then a partial payment of 2.000,00.
  await page.goto(`/app/w/${workspaceId}/students/${studentId}`);
  await expect(async () => {
    await page.reload();
    await expect(packages.getByText(text(language, 'finance.package.remaining', { remaining: 3, total: 4 }))).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });
  await page.getByRole('button', { name: text(language, 'finance.payment.add') }).click();
  await page.getByLabel(text(language, 'finance.payment.amount')).fill('2000');
  await page.getByRole('button', { name: text(language, 'finance.payment.record') }).last().click();
  await expect(page.getByRole('list', { name: text(language, 'finance.balances') }).getByText(/2[.,]800[.,]00/)).toBeVisible();

  // 6. Homework for the student.
  const homework = (await (await page.request.post(`/api/w/${workspaceId}/homework`, { data: { title: 'Дропки', description: 'Задачи 1–5.' }, headers: await headers() })).json()) as { id: string };
  expect((await page.request.post(`/api/w/${workspaceId}/homework/${homework.id}/assignments`, { data: { studentIds: [studentId] }, headers: await headers() })).ok()).toBe(true);

  // 7. The student creates the account from the invitation on a phone and sees their own lesson and homework.
  const phoneContext = await browser.newContext({ ...devices['Pixel 7'] });
  const phone = await phoneContext.newPage();
  await useLanguage(phone, language);
  await openEmailLink(phone, language, studentEmail, '/student/invitation');
  await phone.getByLabel(text(language, 'auth.fields.password'), { exact: true }).fill(PASSWORD);
  await phone.getByLabel(text(language, 'auth.fields.confirmPassword')).fill(PASSWORD);
  await phone.getByRole('button', { name: text(language, 'studentPortal.invitation.create') }).click();
  await expect(phone).toHaveURL(new RegExp(`/student/w/${workspaceId}$`));
  const past = phone.getByRole('list', { name: text(language, 'studentPortal.lessons.past') });
  await expect(past.getByText('Час по математика')).toBeVisible();
  await expect(past.getByText(text(language, 'lessons.attendance.statuses.Present'))).toBeVisible();
  await expect(phone.getByText(text(language, 'studentPortal.lessons.attendance', { present: 1, held: 1, absent: 0, excused: 0 }))).toBeVisible();
  await expect(phone.getByRole('list', { name: text(language, 'homework.portal.title') }).getByRole('link', { name: /Дропки/ })).toBeVisible();
  await expectNoHorizontalScroll(phone, 'the student portal');
  // Only their own: the teacher's workspace stays closed.
  expect((await phone.request.get(`/api/w/${workspaceId}/students`)).status()).toBe(404);
  await phoneContext.close();

  // 8. The teacher's dashboard: one active student who owes 2.800,00 and has 3 lessons left.
  await page.goto(`/app/w/${workspaceId}`);
  const tiles = page.getByRole('list', { name: text(language, 'dashboard.teaching.title') });
  await expect(tiles.getByRole('link', { name: new RegExp(`${text(language, 'dashboard.teaching.activeStudents')}\\s*1`) })).toBeVisible();
  await expect(tiles.getByRole('link', { name: new RegExp(`${text(language, 'dashboard.teaching.remainingLessons')}\\s*3`) })).toBeVisible();
  await expect(page.getByRole('list', { name: text(language, 'dashboard.teaching.owed') }).getByText(/2[.,]800[.,]00/)).toBeVisible();
  if (phoneRun) await expectNoHorizontalScroll(page, 'the dashboard');
});
