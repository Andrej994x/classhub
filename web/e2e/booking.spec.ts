import { devices, expect, test } from '@playwright/test';
import { signedInTeacher, text, uniqueEmail, useLanguage, xsrf } from './support.ts';

// R2-P6 acceptance: a visitor books a free time on a phone; the teacher confirms it into a lesson without conflicts.
test('a visitor books a lesson on a phone and the teacher confirms it', async ({ page, browser }) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const { workspaceId } = await signedInTeacher(page, uniqueEmail('booking'));
  const slug = `book-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;

  // Setup through the API: a public offer, a published site and booking every day 09:00–17:00.
  const headers = async () => ({ 'X-XSRF-TOKEN': await xsrf(page.request) });
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

  // The visitor, on a phone and without a session, books the first free time of the second day shown.
  const visitor = await browser.newContext({ ...devices['Pixel 7'] });
  const phone = await visitor.newPage();
  await phone.goto(`/${slug}`);
  await phone.getByRole('link', { name: 'Закажете час онлајн' }).click();
  await expect(phone).toHaveURL(new RegExp(`/${slug}/book$`));
  await expect(phone.getByText(/Сите времиња се во временската зона на наставникот: Europe\/Skopje/)).toBeVisible();
  const day = phone.getByRole('region', { name: 'Слободни термини' }).getByRole('list').first().getByRole('listitem').nth(1);
  await day.getByRole('button').first().click();
  await phone.getByLabel('Име и презиме', { exact: true }).fill('Марија Илиевска');
  await phone.getByLabel('Е-пошта', { exact: true }).fill(uniqueEmail('visitor'));
  await phone.getByRole('checkbox').nth(0).check();
  await phone.getByRole('checkbox').nth(1).check();
  await phone.getByRole('button', { name: 'Испрати барање за термин' }).click();
  await expect(phone.getByText(/Барањето е испратено/)).toBeVisible();
  expect(await phone.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await visitor.close();

  // The teacher confirms; the lesson is in the calendar.
  await page.goto(`/app/w/${workspaceId}/booking`);
  const requests = page.getByRole('list', { name: text(language, 'booking.requests.title') });
  const request = requests.getByRole('listitem', { name: /Марија Илиевска/ });
  await request.getByRole('button', { name: text(language, 'booking.requests.confirm') }).click();
  await expect(request.getByText(text(language, 'booking.statuses.Confirmed'))).toBeVisible();
  await request.getByRole('link', { name: text(language, 'booking.requests.openLesson') }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Час по математика' })).toBeVisible();
  await expect(page.getByText(text(language, 'lessons.statuses.Scheduled'))).toBeVisible();
});
