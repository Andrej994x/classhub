import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, problem, SUBJECT_MATH, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const BASE = `/api/w/${WORKSPACE_ID}/groups`;
const STUDENTS = `/api/w/${WORKSPACE_ID}/students`;

const studentItem = (id: string, firstName: string, lastName: string) => ({
  id,
  firstName,
  lastName,
  email: null,
  phone: null,
  isMinor: false,
  subjectIds: [] as string[],
  archived: false,
  updatedAtUtc: '2026-11-05T09:00:00Z',
});

type Member = { studentId: string; firstName: string; lastName: string | null; studentArchived: boolean; addedAtUtc: string };

const group = (overrides: Record<string, unknown> = {}) => ({
  id: 'g1',
  name: 'Алгебра 8',
  subjectId: SUBJECT_MATH as string | null,
  description: 'Вторник 17ч' as string | null,
  archived: false,
  members: [] as Member[],
  createdAtUtc: '2026-11-05T09:00:00Z',
  updatedAtUtc: '2026-11-05T09:00:00Z',
  version: 0,
  ...overrides,
});

function groupsApi() {
  const students = [studentItem('s1', 'Ана', 'Илиева'), studentItem('s2', 'Борис', 'Трајков')];
  const state = { groups: [group()] };
  const find = (id: string) => state.groups.find((g) => g.id === id)!;
  const replace = (next: ReturnType<typeof group>) => {
    state.groups = state.groups.map((g) => (g.id === next.id ? next : g));
    return next;
  };
  const handler = (call: FakeCall) => {
    if (call.path === STUDENTS && call.method === 'GET') {
      const q = new URLSearchParams(call.search).get('q') ?? '';
      const items = students.filter((s) => !q || `${s.firstName} ${s.lastName}`.includes(q));
      return json(200, { items, total: items.length, page: 1, pageSize: 20, activeCount: students.length });
    }
    if (call.path === `${STUDENTS}/s1` && call.method === 'GET') {
      return json(200, { ...students[0], guardianName: null, guardianEmail: null, guardianPhone: null, sourceLeadId: null, createdAtUtc: '2026-11-05T09:00:00Z', version: 0 });
    }
    if (call.path === `${STUDENTS}/s1/notes` && call.method === 'GET') return json(200, []);
    if (call.path === BASE && call.method === 'GET') {
      const params = new URLSearchParams(call.search);
      const archived = params.get('archived') === 'true';
      const studentId = params.get('studentId');
      return json(
        200,
        state.groups
          .filter((g) => g.archived === archived && (!studentId || g.members.some((m) => m.studentId === studentId)))
          .map((g) => ({ id: g.id, name: g.name, subjectId: g.subjectId, memberCount: g.members.length, archived: g.archived, updatedAtUtc: g.updatedAtUtc })),
      );
    }
    if (call.path === BASE && call.method === 'POST') {
      const created = group({ ...(call.body as object), id: 'g2', version: 0, members: [] });
      state.groups.push(created);
      return json(201, created);
    }
    const match = /^.*\/groups\/(g\d)(\/.*)?$/.exec(call.path);
    if (!match) return undefined;
    const current = find(match[1]!);
    const rest = match[2] ?? '';
    if (rest === '' && call.method === 'GET') return json(200, current);
    if (rest === '' && call.method === 'PUT') {
      if ((call.body as { version: number }).version !== current.version) return problem(409, 'CONCURRENCY_CONFLICT');
      return json(200, replace(group({ ...current, ...(call.body as object), version: current.version + 1 })));
    }
    if (rest === '/archive') return json(200, replace(group({ ...current, archived: true, version: current.version + 1 })));
    if (rest === '/members' && call.method === 'POST') {
      const s = students.find((x) => x.id === (call.body as { studentId: string }).studentId)!;
      const member = { studentId: s.id, firstName: s.firstName, lastName: s.lastName, studentArchived: false, addedAtUtc: '2026-11-05T10:00:00Z' };
      return json(200, replace(group({ ...current, members: [...current.members, member] })));
    }
    if (rest.startsWith('/members/') && call.method === 'DELETE') {
      const studentId = rest.slice('/members/'.length);
      return json(200, replace(group({ ...current, members: current.members.filter((m) => m.studentId !== studentId) })));
    }
    return undefined;
  };
  const api = fakeApi({ me: teacher(), extra: [handler] });
  return { api, state };
}

describe('groups', () => {
  it('lists the active groups with their member counts', async () => {
    const { state } = groupsApi();
    state.groups = [group({ members: [{ studentId: 's1', firstName: 'Ана', lastName: 'Илиева', studentArchived: false, addedAtUtc: '2026-11-05T10:00:00Z' }] })];
    await renderRoute(`/app/w/${WORKSPACE_ID}/groups`);

    const list = await screen.findByRole('list', { name: 'Групи' });
    const link = within(list).getByRole('link', { name: /Алгебра 8/ });
    expect(link).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/groups/g1`);
    expect(link).toHaveTextContent('1 ученик');
  });

  it('creates a group and opens it', async () => {
    const user = userEvent.setup();
    const { api } = groupsApi();
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/groups/new`);

    await user.click(await screen.findByRole('button', { name: 'Создај група' }));
    expect(await screen.findByText('Внесете име на групата.')).toBeInTheDocument();
    expect(api.to(BASE, 'POST')).toHaveLength(0);

    await user.type(screen.getByLabelText('Име на групата'), 'Физика 8');
    await user.click(screen.getByRole('button', { name: 'Создај група' }));

    await expect.poll(() => router.state.location.pathname).toBe(`/app/w/${WORKSPACE_ID}/groups/g2`);
    expect(api.to(BASE, 'POST')[0]).toMatchObject({ body: { name: 'Физика 8', subjectId: null, description: null, version: null }, xsrf: 'xsrf' });
  });

  it('adds and removes members without disturbing the open details form', async () => {
    const user = userEvent.setup();
    const { api } = groupsApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/groups/g1`);

    const nameField = await screen.findByLabelText('Име на групата');
    await user.clear(nameField);
    await user.type(nameField, 'Алгебра 8А');

    await user.selectOptions(screen.getByLabelText('Ученик за додавање'), 's1');
    await user.click(screen.getByRole('button', { name: 'Додај во групата' }));
    const members = await screen.findByRole('list', { name: 'Ученици во групата' });
    expect(within(members).getByRole('link', { name: 'Ана Илиева' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/students/s1`);
    expect(api.to(`${BASE}/g1/members`, 'POST')[0]!.body).toEqual({ studentId: 's1' });
    // A student already in the group is not offered again.
    expect(within(screen.getByLabelText('Ученик за додавање')).queryByRole('option', { name: 'Ана Илиева' })).toBeNull();

    // The unsaved name survived the member change and saves with the loaded version.
    expect(screen.getByLabelText('Име на групата')).toHaveValue('Алгебра 8А');
    await user.click(screen.getByRole('button', { name: 'Зачувај' }));
    await expect.poll(() => api.to(`${BASE}/g1`, 'PUT').length).toBe(1);
    expect(api.to(`${BASE}/g1`, 'PUT')[0]!.body).toMatchObject({ name: 'Алгебра 8А', subjectId: SUBJECT_MATH, version: 0 });
    expect(await screen.findByText('Зачувано.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Отстрани го/ја Ана Илиева од групата' }));
    expect(await screen.findByText('Сè уште нема ученици во групата.')).toBeInTheDocument();
    expect(api.to(`${BASE}/g1/members/s1`, 'DELETE')).toHaveLength(1);
  });

  it('keeps showing a saved subject that is no longer in the catalog', async () => {
    const user = userEvent.setup();
    const { api, state } = groupsApi();
    state.groups[0] = group({ subjectId: '33333333-0000-4000-8000-0000000000ff' });
    await renderRoute(`/app/w/${WORKSPACE_ID}/groups/g1`);

    const subject = await screen.findByLabelText('Предмет');
    await expect.poll(() => (subject as HTMLSelectElement).selectedOptions[0]?.textContent).toBe('Тековен предмет (повеќе не се нуди)');
    await user.click(screen.getByRole('button', { name: 'Зачувај' }));
    await expect.poll(() => api.to(`${BASE}/g1`, 'PUT').length).toBe(1);
    expect(api.to(`${BASE}/g1`, 'PUT')[0]!.body).toMatchObject({ subjectId: '33333333-0000-4000-8000-0000000000ff' });
  });

  it('searches students for the picker', async () => {
    const user = userEvent.setup();
    const { api } = groupsApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/groups/g1`);

    await user.type(await screen.findByLabelText('Пронајди ученик'), 'Бор');
    await expect.poll(() => api.to(STUDENTS).some((c) => new URLSearchParams(c.search).get('q') === 'Бор')).toBe(true);
    await expect.poll(() => within(screen.getByLabelText('Ученик за додавање')).queryByRole('option', { name: 'Ана Илиева' })).toBeNull();
    expect(within(screen.getByLabelText('Ученик за додавање')).getByRole('option', { name: 'Борис Трајков' })).toBeInTheDocument();
  });

  it('keeps unsaved edits when another tab changed the group, and reports the conflict on save', async () => {
    const user = userEvent.setup();
    const { api, state } = groupsApi();
    const { queryClient } = await renderRoute(`/app/w/${WORKSPACE_ID}/groups/g1`);

    const nameField = await screen.findByLabelText('Име на групата');
    await user.clear(nameField);
    await user.type(nameField, 'Мое уредување');

    state.groups[0] = group({ ...state.groups[0], description: 'Друг термин', version: 4 });
    await act(() => queryClient.invalidateQueries());
    await expect.poll(() => api.to(`${BASE}/g1`, 'GET').length).toBeGreaterThan(1);

    expect(screen.getByLabelText('Име на групата')).toHaveValue('Мое уредување');
    await user.click(screen.getByRole('button', { name: 'Зачувај' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Групата е променета на друго место.');
  });

  it('archives with the current version and shows the groups on the student page', async () => {
    const user = userEvent.setup();
    const { api, state } = groupsApi();
    state.groups[0] = group({ members: [{ studentId: 's1', firstName: 'Ана', lastName: 'Илиева', studentArchived: false, addedAtUtc: '2026-11-05T10:00:00Z' }] });
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/groups/g1`);

    await user.click(await screen.findByRole('button', { name: 'Архивирај ја групата' }));
    expect(await screen.findByRole('button', { name: 'Врати ја групата' })).toBeInTheDocument();
    expect(api.to(`${BASE}/g1/archive`)[0]!.body).toEqual({ version: 0 });
    // An archived group takes no new members.
    expect(screen.queryByLabelText('Ученик за додавање')).toBeNull();

    // The student page lists the active groups the student is in.
    state.groups.push(group({ id: 'g3', name: 'Физика 8', members: [{ studentId: 's1', firstName: 'Ана', lastName: 'Илиева', studentArchived: false, addedAtUtc: '2026-11-05T10:00:00Z' }] }));
    await act(() => router.navigate(`/app/w/${WORKSPACE_ID}/students/s1`));
    const ofStudent = await screen.findByRole('list', { name: 'Групи' });
    expect(within(ofStudent).getByRole('link', { name: 'Физика 8' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/groups/g3`);
    expect(within(ofStudent).queryByRole('link', { name: 'Алгебра 8' })).toBeNull(); // archived
    expect(api.to(BASE, 'GET').some((c) => new URLSearchParams(c.search).get('studentId') === 's1')).toBe(true);
  });
});
