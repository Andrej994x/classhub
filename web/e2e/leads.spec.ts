import { devices, expect, test } from '@playwright/test';
import { signedInTeacher, text, uniqueEmail, useLanguage, xsrf } from './support.ts';

// R1-P5 acceptance: a visitor sends a lesson request from the public site; the teacher gets it in the inbox.
test('a visitor request on the public site reaches the teacher inbox', async ({ page, browser }) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const email = uniqueEmail('leads');
  const { workspaceId } = await signedInTeacher(page, email);
  const slug = `lead-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;

  // Setup through the API: an offer and a published site.
  const headers = async () => ({ 'X-XSRF-TOKEN': await xsrf(page.request) });
  const catalog = (await (await page.request.get('/api/catalog')).json()) as { subjects: { id: string; code: string }[] };
  const math = catalog.subjects.find((s) => s.code === 'math')!.id;
  expect((await page.request.post(`/api/w/${workspaceId}/offers`, {
    data: { kind: 'Package', title: 'Пакет 10 часа', subjectId: math, durationMinutes: 60, priceMinor: 1_200_000, mode: 'Online', lessonCount: 10, isActive: true, showOnPublicSite: true },
    headers: await headers(),
  })).status()).toBe(201);
  const draft = (await (await page.request.get(`/api/w/${workspaceId}/site`)).json()) as { draft: unknown };
  const saved = (await (await page.request.put(`/api/w/${workspaceId}/site`, {
    data: { slug, template: 'classic', palette: 'ocean', language: 'mk', draft: draft.draft, version: null },
    headers: await headers(),
  })).json()) as { version: number };
  expect((await page.request.post(`/api/w/${workspaceId}/site/publish`, { data: { version: saved.version }, headers: await headers() })).ok()).toBe(true);

  // The visitor, on a phone and without a session, sends a lesson request.
  const visitor = await browser.newContext({ ...devices['Pixel 7'] });
  const phone = await visitor.newPage();
  await phone.goto(`/${slug}`);
  const form = phone.getByRole('form', { name: 'Испратете барање' });
  await expect(form).toBeVisible();
  await form.getByRole('radio', { name: 'Да побарам час' }).check();
  await form.getByLabel('Име и презиме', { exact: true }).fill('Марија Илиевска');
  await form.getByLabel('Е-пошта', { exact: true }).fill('marija@example.test');
  await form.getByLabel('Понуда (по желба)', { exact: true }).selectOption({ label: 'Пакет 10 часа' });
  await form.getByLabel(/^Порака/).fill('Подготовка за матура.');
  await form.getByRole('checkbox').nth(0).check();
  await form.getByRole('checkbox').nth(1).check();
  // Filled in faster than the minimum fill time: the island waits out the rest instead of being dropped as spam.
  await form.getByRole('button', { name: 'Испрати' }).click();
  await expect(phone.getByText(/Ви благодариме!/)).toBeVisible();
  await visitor.close();

  // The teacher was notified and finds the request in the inbox.
  await expect
    .poll(async () => ((await (await page.request.get(`/api/dev/mailbox?to=${encodeURIComponent(email)}`)).json()) as { template: string }[]).some((m) => m.template === 'leads.new'))
    .toBe(true);
  await page.goto(`/app/w/${workspaceId}/leads`);
  await expect(page.getByRole('heading', { level: 1, name: text(language, 'leads.title') })).toBeVisible();
  await page.getByRole('link', { name: /Марија Илиевска/ }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Марија Илиевска' })).toBeVisible();
  await expect(page.getByText('Пакет 10 часа')).toBeVisible();
  await expect(page.getByText('Подготовка за матура.')).toBeVisible();

  await page.getByRole('button', { name: text(language, 'leads.moveTo.Contacted') }).click();
  await expect(page.getByText(`${text(language, 'leads.status')}: ${text(language, 'leads.statuses.Contacted')}`)).toBeVisible();
  await page.getByLabel(text(language, 'leads.addNote')).fill('Јавив се, почнуваме во среда.');
  await page.getByRole('button', { name: text(language, 'leads.saveNote') }).click();
  await expect(page.getByRole('list', { name: text(language, 'leads.notes') }).getByText('Јавив се, почнуваме во среда.')).toBeVisible();

  // R2-P1: the request becomes a student (D-12), and the request now links to it.
  await page.getByRole('button', { name: text(language, 'leads.convert.action') }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Марија Илиевска' })).toBeVisible();
  await expect(page).toHaveURL(new RegExp(`/app/w/${workspaceId}/students/[0-9a-f-]{36}$`));
  await expect(page.getByLabel(text(language, 'students.fields.email'))).toHaveValue('marija@example.test');
  await page.getByRole('link', { name: text(language, 'students.back') }).click();
  await expect(page.getByRole('list', { name: text(language, 'students.title') }).getByRole('link', { name: /Марија Илиевска/ })).toBeVisible();
});
