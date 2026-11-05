import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const ADMIN = teacher({ email: 'admin@example.com', platformRoles: ['PlatformAdmin'] });
const MFA = { enabled: true, sessionVerified: true, required: true, recoveryCodesLeft: 10 };

const report = (overrides: Record<string, unknown> = {}) => ({
  id: 'r1',
  createdAtUtc: '2026-11-05T10:00:00Z',
  status: 'Open',
  target: 'section',
  reason: 'misleading',
  details: 'Fake diplomas',
  reporterEmail: 'reporter@example.test',
  workspaceId: WORKSPACE_ID,
  siteSlug: 'ana',
  sectionType: 'faq',
  testId: null,
  testTitle: null,
  targetHidden: false,
  openReportsForTarget: 2,
  resolvedAtUtc: null,
  resolvedByUserId: null,
  resolutionNote: null,
  ...overrides,
});

function moderationApi() {
  const state = {
    reports: [report()],
    hidden: [{ workspaceId: WORKSPACE_ID, target: 'test', siteSlug: 'ana', sectionType: null, testId: 't1', testTitle: 'Алгебра', reason: 'Copied questions', hiddenAtUtc: '2026-11-05T09:00:00Z' }],
    catalog: {
      categories: [{ id: 'c1', code: 'math-science', nameMk: 'Математика', nameEn: 'Math', sortOrder: 1, isActive: true, categoryId: null }],
      subjects: [{ id: 's1', code: 'math', nameMk: 'Математика', nameEn: 'Mathematics', sortOrder: 1, isActive: true, categoryId: 'c1' }],
      levels: [{ id: 'l1', code: 'secondary', nameMk: 'Средно', nameEn: 'Secondary', sortOrder: 1, isActive: true, categoryId: null }],
    },
  };
  const handler = (call: FakeCall) => {
    if (call.path === '/api/auth/mfa') return json(200, MFA);
    if (call.path === '/api/admin/reports') {
      const status = new URLSearchParams(call.search).get('status') ?? '';
      const items = state.reports.filter((r) => !status || r.status === status);
      return json(200, { items, total: items.length, page: 1, pageSize: 20 });
    }
    if (call.path === '/api/admin/reports/r1/hide') {
      state.reports = [report({ status: 'Resolved', targetHidden: true, resolvedAtUtc: '2026-11-05T11:00:00Z', resolutionNote: (call.body as { reason: string }).reason })];
      return json(200, state.reports[0]);
    }
    if (call.path === '/api/admin/moderation/hidden') return json(200, state.hidden);
    if (call.path === '/api/admin/moderation/restore') {
      state.hidden = [];
      return new Response(null, { status: 204 });
    }
    if (call.path === '/api/admin/catalog' && call.method === 'GET') return json(200, state.catalog);
    if (call.path === '/api/admin/catalog/subjects/s1' && call.method === 'PUT') {
      const body = call.body as { isActive: boolean };
      state.catalog.subjects = [{ ...state.catalog.subjects[0]!, isActive: body.isActive }];
      return json(200, state.catalog.subjects[0]);
    }
    if (call.path === '/api/admin/catalog/levels' && call.method === 'POST') {
      const body = call.body as { code: string; nameMk: string; nameEn: string };
      const created = { id: 'l2', code: body.code, nameMk: body.nameMk, nameEn: body.nameEn, sortOrder: 0, isActive: true, categoryId: null };
      state.catalog.levels = [...state.catalog.levels, created];
      return json(200, created);
    }
    return undefined;
  };
  return { api: fakeApi({ me: ADMIN, extra: [handler] }), state };
}

describe('moderation', () => {
  it('shows a report with the reporter for staff and hides the content with a reason', async () => {
    const user = userEvent.setup();
    const { api } = moderationApi();
    await renderRoute('/admin/reports');

    const card = await screen.findByRole('article');
    expect(card).toHaveTextContent('Лажни или заведувачки информации');
    expect(card).toHaveTextContent('Fake diplomas');
    expect(card).toHaveTextContent('reporter@example.test');
    expect(card).toHaveTextContent('2 отворени пријави за оваа содржина');
    expect(within(card).getByRole('link', { name: 'Страница' })).toHaveAttribute('href', '/ana');

    const hide = within(card).getByRole('button', { name: 'Сокриј содржина' });
    expect(hide).toBeDisabled();
    await user.type(within(card).getByLabelText('Причина (ја гледа наставникот)'), 'Misleading claims');
    await user.click(hide);

    await expect.poll(() => api.to('/api/admin/reports/r1/hide').length).toBe(1);
    expect(api.to('/api/admin/reports/r1/hide')[0]).toMatchObject({ body: { reason: 'Misleading claims' }, xsrf: 'xsrf' });
    // The Open filter no longer lists it.
    expect(await screen.findByText('Нема пријави.')).toBeInTheDocument();
  });

  it('shows hidden content again with a reason', async () => {
    const user = userEvent.setup();
    const { api } = moderationApi();
    await renderRoute('/admin/moderation');

    const list = await screen.findByRole('list', { name: 'Сокриена содржина' });
    expect(list).toHaveTextContent('Алгебра');
    expect(list).toHaveTextContent('Copied questions');
    await user.type(within(list).getByLabelText('Причина за повторно прикажување'), 'Questions rewritten');
    await user.click(within(list).getByRole('button', { name: 'Прикажи повторно' }));

    await expect.poll(() => api.to('/api/admin/moderation/restore').length).toBe(1);
    expect(api.to('/api/admin/moderation/restore')[0]!.body).toEqual({ workspaceId: WORKSPACE_ID, target: 'test', sectionType: null, testId: 't1', reason: 'Questions rewritten' });
    expect(await screen.findByText('Ништо не е сокриено.')).toBeInTheDocument();
  });

  it('deactivates a subject and adds a level in the catalog', async () => {
    const user = userEvent.setup();
    const { api } = moderationApi();
    await renderRoute('/admin/catalog');

    const subjects = await screen.findByRole('list', { name: 'Предмети' });
    await user.click(within(subjects).getByRole('button', { name: 'Деактивирај' }));
    await expect.poll(() => api.to('/api/admin/catalog/subjects/s1', 'PUT').length).toBe(1);
    expect(api.to('/api/admin/catalog/subjects/s1', 'PUT')[0]!.body).toMatchObject({ isActive: false, code: null, nameMk: 'Математика' });
    expect(await within(subjects).findByRole('button', { name: 'Активирај' })).toBeInTheDocument();

    const add = screen.getAllByRole('form', { name: 'Додај' })[2]!; // levels
    await user.type(within(add).getByLabelText('Код'), 'university');
    await user.type(within(add).getByLabelText('Име (македонски)'), 'Факултет');
    await user.type(within(add).getByLabelText('Име (англиски)'), 'University');
    await user.click(within(add).getByRole('button', { name: 'Додај' }));
    await expect.poll(() => api.to('/api/admin/catalog/levels', 'POST').length).toBe(1);
    expect(api.to('/api/admin/catalog/levels', 'POST')[0]!.body).toMatchObject({ code: 'university', nameEn: 'University', isActive: true });
  });
});

describe('teacher moderation notices', () => {
  it('tells the teacher which section was hidden and why, without the reporter', async () => {
    const api = fakeApi({ me: teacher() });
    api.state.site = {
      ...api.state.site,
      exists: true,
      version: 1,
      isPublished: true,
      moderation: { siteHidden: false, siteReason: null, siteHiddenAtUtc: null, sections: [{ type: 'faq', reason: 'Misleading claims', hiddenAtUtc: '2026-11-05T11:00:00Z' }] },
    } as typeof api.state.site;
    await renderRoute(`/app/w/${WORKSPACE_ID}/site`);

    const notice = await screen.findByText('Тимот на ClassHub сокри дел од вашата јавна страница од посетителите.');
    const alert = notice.closest('[role="status"]')!;
    expect(alert).toHaveTextContent('Misleading claims');
    expect(alert).not.toHaveTextContent('@');
  });
});
