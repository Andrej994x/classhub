import { devices, expect, test } from '@playwright/test';
import { signedInTeacher, text, uniqueEmail, useLanguage, xsrf } from './support.ts';

// R1-P7 acceptance: a visitor takes a test on a phone, sees the score, asks for a lesson with the result; the teacher
// sees the request with the shared result.
test('a visitor takes a test on a phone and the teacher sees the request with the result', async ({ page, browser }) => {
  const language = 'mk' as const;
  await useLanguage(page, language);
  const email = uniqueEmail('diagnostic');
  const { workspaceId } = await signedInTeacher(page, email);
  const slug = `dt-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;
  const headers = async () => ({ 'X-XSRF-TOKEN': await xsrf(page.request) });

  // Setup through the API: a published site and a published test.
  const catalog = (await (await page.request.get('/api/catalog')).json()) as { subjects: { id: string; code: string }[] };
  const math = catalog.subjects.find((s) => s.code === 'math')!.id;
  const draft = (await (await page.request.get(`/api/w/${workspaceId}/site`)).json()) as { draft: unknown };
  const site = (await (await page.request.put(`/api/w/${workspaceId}/site`, {
    data: { slug, template: 'modern', palette: 'forest', language: 'mk', draft: draft.draft, version: null },
    headers: await headers(),
  })).json()) as { version: number };
  expect((await page.request.post(`/api/w/${workspaceId}/site/publish`, { data: { version: site.version }, headers: await headers() })).ok()).toBe(true);
  const created = (await (await page.request.post(`/api/w/${workspaceId}/tests`, {
    data: {
      title: 'Дропки за почетници',
      subjectId: math,
      difficulty: 'Beginner',
      questions: [
        { text: 'Колку е 1/2 + 1/4?', points: 2, options: [{ text: '3/4', isCorrect: true }, { text: '2/6', isCorrect: false }] },
        { text: 'Кои броеви се прости?', points: 3, options: [{ text: '2', isCorrect: true }, { text: '3', isCorrect: true }, { text: '4', isCorrect: false }] },
      ],
    },
    headers: await headers(),
  })).json()) as { id: string; slug: string; version: number };
  expect((await page.request.post(`/api/w/${workspaceId}/tests/${created.id}/publish`, { data: { version: created.version }, headers: await headers() })).ok()).toBe(true);

  // The visitor on a phone: the site links the test; the test runs in the island.
  const visitor = await browser.newContext({ ...devices['Pixel 7'] });
  const phone = await visitor.newPage();
  await phone.goto(`/${slug}`);
  await phone.getByRole('link', { name: 'Дропки за почетници' }).click();
  await expect(phone.getByRole('heading', { level: 1, name: 'Дропки за почетници' })).toBeVisible();
  await phone.getByRole('radio', { name: '3/4' }).check();
  await phone.getByRole('checkbox', { name: '2', exact: true }).check();
  await phone.getByRole('button', { name: 'Заврши го тестот' }).click();
  await expect(phone.getByTestId('test-score')).toHaveText(/2 од 5 бода/);

  const form = phone.getByRole('form', { name: 'Побарајте час според резултатот' });
  await form.getByLabel('Име и презиме', { exact: true }).fill('Марија Илиевска');
  await form.getByLabel('Е-пошта', { exact: true }).fill('marija@example.test');
  await form.getByRole('checkbox').nth(0).check(); // consent
  await form.getByRole('checkbox').nth(1).check(); // age / guardian
  await form.getByRole('checkbox', { name: /Сподели го мојот резултат/ }).check();
  await form.getByRole('button', { name: 'Испрати' }).click();
  await expect(phone.getByText(/Ви благодариме!/)).toBeVisible();
  await visitor.close();

  // The teacher finds the request with the shared result, and the test's results.
  await page.goto(`/app/w/${workspaceId}/leads`);
  await page.getByRole('link', { name: /Марија Илиевска/ }).click();
  await expect(page.getByText(`${text(language, 'leads.types.Diagnostic')}`, { exact: false }).first()).toBeVisible();
  await expect(page.getByText(/2 од 5 бода \(споделено од посетителот\)/)).toBeVisible();
  await page.getByRole('link', { name: 'Дропки за почетници' }).click();
  await expect(page.getByTestId('test-results')).toContainText('Решаван 1 пат.');
});
