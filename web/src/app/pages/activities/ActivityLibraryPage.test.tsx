import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, problem, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const BASE = `/api/w/${WORKSPACE_ID}/activities`;
const item = (id: string, title: string, overrides: Record<string, unknown> = {}) => ({
  id,
  title,
  subjectId: null,
  toolType: 'drawing-board',
  schemaVersion: 1,
  sizeBytes: 120,
  updatedAtUtc: '2026-11-10T09:00:00Z',
  version: 2,
  ...overrides,
});
const document = { elements: [{ type: 'rectangle' }] };

function libraryApi({ boards = [] as string[], duplicate = 'ok' as 'ok' | 'limit' } = {}) {
  const items = [item('a1', 'Агли во триаголник'), item('a2', 'Кругови', { toolType: 'geometry-board' })];
  return fakeApi({
    me: teacher(),
    extra: [
      (call: FakeCall) => {
        if (call.path === `/api/w/${WORKSPACE_ID}/toolbox`) {
          const tools = boards.map((key) => ({ id: key, code: key, nameMk: key, nameEn: key, descriptionMk: '', descriptionEn: '', kind: 'BuiltIn', category: 'Drawing', url: null, builtInKey: key, subjectIds: [], isFeatured: false, licenseNoteMk: null, licenseNoteEn: null }));
          return json(200, { tools, categories: [] });
        }
        if (call.path === BASE && call.method === 'GET') {
          const q = new URLSearchParams(call.search).get('q')?.toLowerCase();
          const found = items.filter((i) => !q || i.title.toLowerCase().includes(q));
          return json(200, { items: found, total: found.length, page: 1, pageSize: 20 });
        }
        if (call.path === `${BASE}/a1` && call.method === 'GET') return json(200, { ...items[0], document, createdAtUtc: '2026-11-09T09:00:00Z' });
        if (call.path === `${BASE}/a1` && call.method === 'PUT') return json(200, { ...items[0], ...(call.body as object), version: 3 });
        if (call.path === `${BASE}/a1/duplicate`) return duplicate === 'ok' ? json(201, { ...items[0], id: 'a3' }) : problem(422, 'PLAN_LIMIT_REACHED');
        if (call.path === `${BASE}/a1/delete`) return new Response(null, { status: 204 });
        return undefined;
      },
    ],
  });
}

describe('activity library paging', () => {
  it('moves back to the last page that still exists after deleting the only activity of the last page', async () => {
    const user = userEvent.setup();
    let items = Array.from({ length: 21 }, (_, i) => item(`p${i}`, `Активност ${i + 1}`));
    fakeApi({
      me: teacher(),
      extra: [
        (call: FakeCall) => {
          if (call.path === `/api/w/${WORKSPACE_ID}/toolbox`) return json(200, { tools: [], categories: [] });
          if (call.path === BASE && call.method === 'GET') {
            const page = Number(new URLSearchParams(call.search).get('page'));
            return json(200, { items: items.slice((page - 1) * 20, page * 20), total: items.length, page, pageSize: 20 });
          }
          if (call.path === `${BASE}/p20/delete`) {
            items = items.filter((i) => i.id !== 'p20');
            return new Response(null, { status: 204 });
          }
          return undefined;
        },
      ],
    });
    await renderRoute(`/app/w/${WORKSPACE_ID}/activities`);

    await user.click(await screen.findByRole('button', { name: 'Следна' }));
    const last = await screen.findByRole('article', { name: 'Активност 21' });
    await user.click(within(last).getByRole('button', { name: 'Избриши' }));
    await user.click(within(last).getByRole('button', { name: 'Да, избриши' }));

    // Back on page 1 with its 20 activities (not an empty page without controls).
    expect(await screen.findByRole('article', { name: 'Активност 1' })).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Библиотека на активности' })).getAllByRole('article')).toHaveLength(20);
  });
});

describe('activity library', () => {
  it('lists, searches and renames with the listed version and the unchanged document', async () => {
    const user = userEvent.setup();
    const api = libraryApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/activities`);

    const list = await screen.findByRole('list', { name: 'Библиотека на активности' });
    const angles = within(list).getByRole('article', { name: 'Агли во триаголник' });
    expect(angles).toHaveTextContent('Табла за цртање');
    expect(within(list).getByRole('article', { name: 'Кругови' })).toHaveTextContent('Геометриска табла');
    // The boards have not shipped yet: no open link.
    expect(within(angles).queryByRole('link')).not.toBeInTheDocument();
    expect(angles).toHaveTextContent('Таблата наскоро ќе биде достапна');

    await user.type(screen.getByRole('searchbox', { name: 'Пребарај активности' }), 'круг');
    await expect.poll(() => within(screen.getByRole('list', { name: 'Библиотека на активности' })).queryAllByRole('article').length).toBe(1);
    await user.clear(screen.getByRole('searchbox', { name: 'Пребарај активности' }));

    const row = await screen.findByRole('article', { name: 'Агли во триаголник' });
    await user.click(within(row).getByRole('button', { name: 'Преименувај' }));
    const form = screen.getByRole('form', { name: 'Преименувај Агли во триаголник' });
    await user.clear(within(form).getByLabelText('Наслов'));
    await user.type(within(form).getByLabelText('Наслов'), 'Агли — повторување');
    await user.click(within(form).getByRole('button', { name: 'Зачувај' }));
    await expect.poll(() => api.to(`${BASE}/a1`, 'PUT').length).toBe(1);
    expect(api.to(`${BASE}/a1`, 'PUT')[0]!.body).toEqual({ title: 'Агли — повторување', subjectId: null, toolType: 'drawing-board', schemaVersion: 1, document, version: 2 });
  });

  it('duplicates, shows the plan limit, deletes after confirming, and opens activities of shipped boards', async () => {
    const user = userEvent.setup();
    const api = libraryApi({ boards: ['drawing-board'], duplicate: 'limit' });
    await renderRoute(`/app/w/${WORKSPACE_ID}/activities`);

    const row = await screen.findByRole('article', { name: 'Агли во триаголник' });
    expect(within(row).getByRole('link', { name: 'Отвори Агли во триаголник' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/toolbox/drawing-board?activity=a1`);
    expect(within(screen.getByRole('article', { name: 'Кругови' })).queryByRole('link')).not.toBeInTheDocument();

    await user.click(within(row).getByRole('button', { name: 'Удвои' }));
    expect(await within(row).findByRole('alert')).toBeInTheDocument();

    await user.click(within(row).getByRole('button', { name: 'Избриши' }));
    expect(api.to(`${BASE}/a1/delete`, 'POST')).toHaveLength(0);
    await user.click(within(row).getByRole('button', { name: 'Да, избриши' }));
    await expect.poll(() => api.to(`${BASE}/a1/delete`, 'POST').length).toBe(1);
    expect(api.to(`${BASE}/a1/delete`, 'POST')[0]!.body).toEqual({ version: 2 });
  });
});
