import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, LEAD, problem, SUBJECT_MATH, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const BASE = `/api/w/${WORKSPACE_ID}/students`;

const student = (overrides: Record<string, unknown> = {}) => ({
  id: 's1',
  firstName: 'Марко',
  lastName: 'Стојанов',
  email: 'marko@example.test',
  phone: null,
  isMinor: false,
  guardianName: null,
  guardianEmail: null,
  guardianPhone: null,
  subjectIds: [] as string[],
  sourceLeadId: null,
  archived: false,
  createdAtUtc: '2026-11-05T09:00:00Z',
  updatedAtUtc: '2026-11-05T09:00:00Z',
  version: 0,
  accountLinked: false,
  invitation: null as { email: string; sentAtUtc: string; expiresAtUtc: string } | null,
  ...overrides,
});

function studentsApi() {
  const state = { students: [student()], notes: [] as { id: string; text: string; createdAtUtc: string }[] };
  const handler = (call: FakeCall) => {
    if (call.path === BASE && call.method === 'GET') {
      const q = new URLSearchParams(call.search).get('q') ?? '';
      const items = state.students.filter((s) => !q || `${s.firstName} ${s.lastName}`.includes(q));
      return json(200, { items, total: items.length, page: 1, pageSize: 20, activeCount: state.students.filter((s) => !s.archived).length });
    }
    if (call.path === BASE && call.method === 'POST') {
      const body = call.body as { email: string | null };
      if (body.email === 'taken@example.test') return problem(409, 'STUDENT_EMAIL_TAKEN');
      const created = student({ ...(call.body as object), id: 's2', version: 0 });
      state.students.push(created);
      return json(201, created);
    }
    if (call.path === `${BASE}/s1` && call.method === 'GET') return json(200, state.students[0]);
    if (call.path === `${BASE}/s2` && call.method === 'GET') return json(200, state.students.find((s) => s.id === 's2'));
    if (call.path === `${BASE}/s1` && call.method === 'PUT') {
      if ((call.body as { version: number }).version !== state.students[0]!.version) return problem(409, 'CONCURRENCY_CONFLICT');
      state.students[0] = student({ ...state.students[0], ...(call.body as object), version: state.students[0]!.version + 1 });
      return json(200, state.students[0]);
    }
    if (call.path === `${BASE}/s1/archive`) {
      state.students[0] = student({ ...state.students[0], archived: true, version: state.students[0]!.version + 1 });
      return json(200, state.students[0]);
    }
    if (call.path === `${BASE}/s1/notes` && call.method === 'GET') return json(200, state.notes);
    if (call.path === `${BASE}/s1/notes` && call.method === 'POST') {
      const note = { id: `n${state.notes.length}`, text: (call.body as { text: string }).text, createdAtUtc: '2026-11-05T10:00:00Z' };
      state.notes.unshift(note);
      return json(201, note);
    }
    if (call.path === `${BASE}/s1/invitation` && call.method === 'POST') {
      if (state.students[0]!.accountLinked) return problem(409, 'STUDENT_ALREADY_LINKED');
      const email = (call.body as { email: string | null }).email ?? state.students[0]!.email!;
      state.students[0] = student({ ...state.students[0], invitation: { email, sentAtUtc: '2026-11-05T10:00:00Z', expiresAtUtc: '2026-11-12T10:00:00Z' } });
      return json(200, state.students[0]);
    }
    if (call.path === `${BASE}/s1/invitation` && call.method === 'DELETE') {
      state.students[0] = student({ ...state.students[0], invitation: null });
      return json(200, state.students[0]);
    }
    if (call.path === `${BASE}/s1/account` && call.method === 'DELETE') {
      state.students[0] = student({ ...state.students[0], accountLinked: false, invitation: null });
      return json(200, state.students[0]);
    }
    if (call.path === `${BASE}/from-lead`) return json(200, { student: state.students[0], created: true });
    if (call.path === `/api/w/${WORKSPACE_ID}/groups` && call.method === 'GET') return json(200, []); // the student page's groups card
    return undefined;
  };
  const api = fakeApi({ me: teacher(), extra: [handler] });
  return { api, state };
}

describe('students', () => {
  it('lists the active students with the count and searches by name', async () => {
    const user = userEvent.setup();
    const { api } = studentsApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/students`);

    const list = await screen.findByRole('list', { name: 'Ученици' });
    expect(within(list).getByRole('link', { name: /Марко Стојанов/ })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/students/s1`);
    expect(screen.getByText(/1 активен\./)).toBeInTheDocument();

    await user.type(screen.getByLabelText('Барај по име, е-пошта, телефон или родител'), 'Ана');
    await expect.poll(() => api.to(BASE).some((c) => c.search.includes('q=%D0%90%D0%BD%D0%B0'))).toBe(true);
    expect(await screen.findByText('Нема ученици што одговараат.')).toBeInTheDocument();
  });

  it('adds a minor with the guardian and shows a duplicate email clearly', async () => {
    const user = userEvent.setup();
    const { api } = studentsApi();
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/students/new`);

    await user.click(await screen.findByRole('button', { name: 'Додај ученик' }));
    expect(screen.getByText('Внесете го името.')).toBeInTheDocument();
    expect(api.to(BASE, 'POST')).toHaveLength(0);

    await user.type(screen.getByLabelText('Име'), 'Лена');
    await user.type(screen.getByLabelText('Е-пошта (по желба)'), 'taken@example.test');
    await user.click(screen.getByLabelText('Ученикот е помлад од 18 години'));
    await user.type(screen.getByLabelText('Родител или старател'), 'Сања Петрова');
    await user.type(screen.getByLabelText('Телефон на родителот'), '+389 70 111 222');
    await user.click(screen.getByRole('checkbox', { name: 'Математика' }));
    await user.click(screen.getByRole('button', { name: 'Додај ученик' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Друг ученик во вашата евиденција веќе ја користи оваа е-пошта.');

    await user.clear(screen.getByLabelText('Е-пошта (по желба)'));
    await user.click(screen.getByRole('button', { name: 'Додај ученик' }));
    await expect.poll(() => router.state.location.pathname).toBe(`/app/w/${WORKSPACE_ID}/students/s2`);
    expect(api.to(BASE, 'POST').at(-1)!.body).toEqual({
      firstName: 'Лена',
      lastName: null,
      email: null,
      phone: null,
      isMinor: true,
      guardianName: 'Сања Петрова',
      guardianEmail: null,
      guardianPhone: '+389 70 111 222',
      subjectIds: [SUBJECT_MATH],
      version: null,
    });
  });

  it('edits with the loaded version, archives and keeps private notes', async () => {
    const user = userEvent.setup();
    const { api } = studentsApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/students/s1`);

    const last = await screen.findByLabelText('Презиме (по желба)');
    await user.clear(last);
    await user.type(last, 'Стојановски');
    await user.click(screen.getByRole('button', { name: 'Зачувај' }));
    await expect.poll(() => api.to(`${BASE}/s1`, 'PUT').length).toBe(1);
    expect(api.to(`${BASE}/s1`, 'PUT')[0]!.body).toMatchObject({ lastName: 'Стојановски', version: 0 });

    await user.type(await screen.findByLabelText('Нова белешка'), 'Тешкотии со дропки.');
    await user.click(screen.getByRole('button', { name: 'Зачувај белешка' }));
    expect(await screen.findByRole('list', { name: 'Приватни белешки' })).toHaveTextContent('Тешкотии со дропки.');

    await user.click(screen.getByRole('button', { name: 'Архивирај ученик' }));
    expect(await screen.findByRole('button', { name: 'Врати ученик' })).toBeInTheDocument();
    expect(api.to(`${BASE}/s1/archive`)[0]!.body).toEqual({ version: 1 });
  });

  it('keeps unsaved edits when another tab changed the student, and reports the conflict on save', async () => {
    const user = userEvent.setup();
    const { api, state } = studentsApi();
    const { queryClient } = await renderRoute(`/app/w/${WORKSPACE_ID}/students/s1`);

    const last = await screen.findByLabelText('Презиме (по желба)');
    await user.clear(last);
    await user.type(last, 'Мое уредување');
    await user.type(screen.getByLabelText('Нова белешка'), 'Недовршена белешка');

    // Another tab saved meanwhile; this tab refetches (for example after a reconnect).
    state.students[0] = student({ ...state.students[0], phone: '+389 70 999 999', version: 5 });
    await act(() => queryClient.invalidateQueries());
    await expect.poll(() => api.to(`${BASE}/s1`, 'GET').length).toBeGreaterThan(1);

    expect(screen.getByLabelText('Презиме (по желба)')).toHaveValue('Мое уредување');
    expect(screen.getByLabelText('Нова белешка')).toHaveValue('Недовршена белешка');
    await user.click(screen.getByRole('button', { name: 'Зачувај' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Ученикот е изменет на друго место.');
    expect(api.to(`${BASE}/s1`, 'PUT').at(-1)!.body).toMatchObject({ version: 0 });
  });

  it('starts editing from the current server state when a cached copy is older', async () => {
    const user = userEvent.setup();
    const { api, state } = studentsApi();
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/students/s1`);
    expect(await screen.findByLabelText('Презиме (по желба)')).toHaveValue('Стојанов');

    // Leave, another tab saves, come back: the cache still holds version 0.
    await act(() => router.navigate(`/app/w/${WORKSPACE_ID}/students`));
    await screen.findByRole('list', { name: 'Ученици' });
    state.students[0] = student({ ...state.students[0], lastName: 'Ново презиме', version: 3 });
    await act(() => router.navigate(`/app/w/${WORKSPACE_ID}/students/s1`));

    expect(await screen.findByLabelText('Презиме (по желба)')).toHaveValue('Ново презиме');
    await user.click(screen.getByRole('button', { name: 'Зачувај' }));
    await expect.poll(() => api.to(`${BASE}/s1`, 'PUT').length).toBe(1);
    expect(api.to(`${BASE}/s1`, 'PUT')[0]!.body).toMatchObject({ lastName: 'Ново презиме', version: 3 });
    expect(await screen.findByText('Зачувано.')).toBeInTheDocument();
  });

  it('invites the student to an account, withdraws it, and removes a linked account after confirming', async () => {
    const user = userEvent.setup();
    const { api, state } = studentsApi();
    const { queryClient } = await renderRoute(`/app/w/${WORKSPACE_ID}/students/s1`);

    const email = await screen.findByLabelText('Испрати ја поканата на');
    expect(email).toHaveValue('marko@example.test');
    await user.clear(email);
    await user.type(email, 'parent@example.test');
    await user.click(screen.getByRole('button', { name: 'Испрати покана' }));
    expect(await screen.findByText(/Поканата е испратена на parent@example\.test/)).toBeInTheDocument();
    expect(api.to(`${BASE}/s1/invitation`, 'POST')[0]).toMatchObject({ body: { email: 'parent@example.test' }, xsrf: 'xsrf' });
    expect(screen.getByRole('button', { name: 'Испрати нова покана' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Повлечи' }));
    await expect.poll(() => api.to(`${BASE}/s1/invitation`, 'DELETE').length).toBe(1);
    expect(await screen.findByRole('button', { name: 'Испрати покана' })).toBeInTheDocument();

    // The student accepted a new invitation (seen on the next refetch): the card offers to remove the access, after a confirmation.
    state.students[0] = student({ ...state.students[0], accountLinked: true });
    await act(() => queryClient.invalidateQueries());
    expect(await screen.findByText('Ученикот има сметка.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Отстрани пристап' }));
    expect(api.to(`${BASE}/s1/account`, 'DELETE')).toHaveLength(0);
    await user.click(screen.getByRole('button', { name: 'Да, отстрани пристап' }));
    await expect.poll(() => api.to(`${BASE}/s1/account`, 'DELETE').length).toBe(1);
    expect(await screen.findByRole('button', { name: 'Испрати покана' })).toBeInTheDocument();
  });

  it('turns a request into a student from the lead page', async () => {
    const user = userEvent.setup();
    const { api } = studentsApi();
    api.state.leads = [LEAD()];
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/leads/lead-1`);

    await user.click(await screen.findByRole('button', { name: 'Направи ученик' }));

    await expect.poll(() => router.state.location.pathname).toBe(`/app/w/${WORKSPACE_ID}/students/s1`);
    expect(api.to(`${BASE}/from-lead`)[0]).toMatchObject({ body: { leadId: 'lead-1' }, xsrf: 'xsrf' });
  });
});
