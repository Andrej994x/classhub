import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, problem, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';
import { addMonths, monthGrid, startOfWeek, today } from '../../../teacher/lessons.ts';

const BASE = `/api/w/${WORKSPACE_ID}/lessons`;

const lesson = (overrides: Record<string, unknown> = {}) => ({
  id: 'l1',
  kind: 'Individual',
  groupId: null as string | null,
  groupName: null as string | null,
  participants: [{ studentId: 's1', firstName: 'Ана', lastName: 'Илиева' }],
  subjectId: null as string | null,
  title: 'Математика',
  topic: null as string | null,
  startUtc: '2030-11-12T16:00:00Z',
  endUtc: '2030-11-12T17:00:00Z',
  startLocal: '2030-11-12T17:00:00',
  endLocal: '2030-11-12T18:00:00',
  timeZone: 'Europe/Skopje',
  mode: 'Online',
  meetingUrl: null as string | null,
  location: null as string | null,
  status: 'Scheduled',
  cancellationReason: null as string | null,
  createdAtUtc: '2030-11-01T09:00:00Z',
  updatedAtUtc: '2030-11-01T09:00:00Z',
  version: 0,
  ...overrides,
});

function lessonsApi() {
  const state = { lessons: [lesson(), lesson({ id: 'l2', title: 'Физика', startLocal: '2030-11-14T09:00:00', endLocal: '2030-11-14T10:30:00', startUtc: '2030-11-14T08:00:00Z', endUtc: '2030-11-14T09:30:00Z' })] };
  const handler = (call: FakeCall) => {
    if (call.path === `/api/w/${WORKSPACE_ID}/students` && call.method === 'GET') {
      return json(200, {
        items: [{ id: 's1', firstName: 'Ана', lastName: 'Илиева', email: 'ana@example.test', phone: null, isMinor: false, subjectIds: [], archived: false, updatedAtUtc: '2030-11-01T09:00:00Z' }],
        total: 1,
        page: 1,
        pageSize: 20,
        activeCount: 1,
      });
    }
    if (call.path === `/api/w/${WORKSPACE_ID}/groups` && call.method === 'GET') return json(200, [{ id: 'g1', name: 'Алгебра 8', subjectId: null, memberCount: 2, archived: false, updatedAtUtc: '2030-11-01T09:00:00Z' }]);
    if (call.path === BASE && call.method === 'GET') {
      const params = new URLSearchParams(call.search);
      const from = params.get('from')!;
      const to = params.get('to')!;
      return json(200, state.lessons.filter((l) => l.startLocal.slice(0, 10) >= from && l.startLocal.slice(0, 10) <= to));
    }
    if (call.path === BASE && call.method === 'POST') {
      const body = call.body as { startLocal: string; title: string };
      if (body.startLocal === '2030-11-12T17:30:00') return problem(409, 'LESSON_OVERLAP');
      const created = lesson({ ...(call.body as object), id: 'l3', startLocal: body.startLocal });
      state.lessons.push(created);
      return json(201, created);
    }
    const match = /\/lessons\/(l\d)(\/\w+)?$/.exec(call.path);
    if (!match) return undefined;
    const index = state.lessons.findIndex((l) => l.id === match[1]);
    const current = state.lessons[index]!;
    if (!match[2] && call.method === 'GET') return json(200, current);
    if (!match[2] && call.method === 'PUT') {
      if ((call.body as { version: number }).version !== current.version) return problem(409, 'CONCURRENCY_CONFLICT');
      state.lessons[index] = lesson({ ...current, ...(call.body as object), version: current.version + 1 });
      return json(200, state.lessons[index]);
    }
    if (match[2] === '/cancel') {
      state.lessons[index] = lesson({ ...current, status: 'Cancelled', cancellationReason: (call.body as { reason: string }).reason, version: current.version + 1 });
      return json(200, state.lessons[index]);
    }
    return undefined;
  };
  const api = fakeApi({ me: teacher(), extra: [handler] });
  return { api, state };
}

describe('calendar dates', () => {
  it('computes weeks, months and today without time zone drift', () => {
    expect(startOfWeek('2030-11-14')).toBe('2030-11-11'); // Monday
    expect(startOfWeek('2030-11-17')).toBe('2030-11-11'); // Sunday belongs to the week before
    expect(monthGrid('2030-11-20')[0]).toBe('2030-10-28');
    expect(monthGrid('2030-11-20')).toHaveLength(42);
    expect(addMonths('2031-01-31', 1)).toBe('2031-02-28');
    expect(addMonths('2030-03-31', -1)).toBe('2030-02-28');
    // 23:30 UTC is already the next day in Skopje.
    expect(today('Europe/Skopje', new Date('2030-11-12T23:30:00Z'))).toBe('2030-11-13');
  });
});

describe('calendar', () => {
  it('shows the week in local time and moves between weeks and views', async () => {
    const user = userEvent.setup();
    const { api } = lessonsApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/calendar?view=week&date=2030-11-13`);

    const days = await screen.findByRole('list', { name: 'Денови' });
    expect(await within(days).findByRole('link', { name: /17:00–18:00 Математика/ })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/lessons/l1`);
    expect(within(days).getByRole('link', { name: /09:00–10:30 Физика/ })).toBeInTheDocument();
    expect(api.to(BASE, 'GET').some((c) => c.search === '?from=2030-11-11&to=2030-11-17')).toBe(true);
    expect(screen.getByText('Времињата се во Europe/Skopje')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Следно' }));
    await expect.poll(() => api.to(BASE, 'GET').some((c) => c.search === '?from=2030-11-18&to=2030-11-24')).toBe(true);
    expect(await screen.findAllByText('Нема часови')).toHaveLength(7);

    await user.click(screen.getByRole('button', { name: 'Месец' }));
    await expect.poll(() => api.to(BASE, 'GET').some((c) => c.search === '?from=2030-10-28&to=2030-12-08')).toBe(true);
    await user.click(await screen.findByRole('button', { name: /12 ноември: 1 час/ }));
    expect(await screen.findByRole('button', { name: 'Ден', pressed: true })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: /Математика/ })).toBeInTheDocument();
  });
});

describe('lesson editor', () => {
  it('schedules a lesson in local time for the chosen day', async () => {
    const user = userEvent.setup();
    const { api } = lessonsApi();
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/lessons/new?date=2030-11-15`);

    expect(await screen.findByLabelText('Датум')).toHaveValue('2030-11-15');
    await user.click(screen.getByRole('button', { name: 'Закажи час' }));
    expect(await screen.findByText('Изберете ученик.')).toBeInTheDocument();
    expect(api.to(BASE, 'POST')).toHaveLength(0);

    await user.selectOptions(screen.getByLabelText('Ученик'), 's1');
    await user.type(screen.getByLabelText('Наслов'), 'Дропки');
    await user.selectOptions(screen.getByLabelText('Траење'), '90');
    await user.type(screen.getByLabelText('Линк за онлајн средба'), 'https://meet.example.test/x');
    await user.click(screen.getByRole('button', { name: 'Закажи час' }));

    await expect.poll(() => router.state.location.pathname).toBe(`/app/w/${WORKSPACE_ID}/lessons/l3`);
    expect(await screen.findByRole('heading', { level: 1, name: 'Дропки' })).toBeInTheDocument(); // the new lesson opens for editing
    expect(api.to(BASE, 'POST')[0]).toMatchObject({
      body: { kind: 'Individual', studentId: 's1', groupId: null, title: 'Дропки', startLocal: '2030-11-15T17:00:00', durationMinutes: 90, mode: 'Online', meetingUrl: 'https://meet.example.test/x', location: null, version: null },
      xsrf: 'xsrf',
    });
  });

  it('explains an overlap and schedules a group lesson', async () => {
    const user = userEvent.setup();
    const { api } = lessonsApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/lessons/new?date=2030-11-12`);

    await user.click(await screen.findByLabelText('Група'));
    await user.selectOptions(await screen.findByLabelText('Група', { selector: 'select' }), 'g1');
    await user.type(screen.getByLabelText('Наслов'), 'Равенки');
    await user.clear(screen.getByLabelText('Почеток'));
    await user.type(screen.getByLabelText('Почеток'), '17:30');
    await user.click(screen.getByLabelText('Во живо'));
    await user.type(screen.getByLabelText('Локација'), 'Училница 3');
    await user.click(screen.getByRole('button', { name: 'Закажи час' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Веќе имате час во ова време.');
    expect(api.to(BASE, 'POST')[0]!.body).toMatchObject({ kind: 'Group', groupId: 'g1', studentId: null, mode: 'InPerson', location: 'Училница 3', meetingUrl: null });
  });

  it('keeps unsaved edits when the lesson changed elsewhere, and reports the conflict on save', async () => {
    const user = userEvent.setup();
    const { api, state } = lessonsApi();
    const { queryClient } = await renderRoute(`/app/w/${WORKSPACE_ID}/lessons/l1`);

    const title = await screen.findByLabelText('Наслов');
    expect(screen.getByLabelText('Ученик')).toHaveValue('s1');
    expect(screen.getByLabelText('Почеток')).toHaveValue('17:00');
    await user.clear(title);
    await user.type(title, 'Мое уредување');
    state.lessons[0] = lesson({ ...state.lessons[0], topic: 'Друго', version: 3 });
    await act(() => queryClient.invalidateQueries());
    await expect.poll(() => api.to(`${BASE}/l1`, 'GET').length).toBeGreaterThan(1);

    expect(screen.getByLabelText('Наслов')).toHaveValue('Мое уредување');
    await user.click(screen.getByRole('button', { name: 'Зачувај' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Часот е променет на друго место.');
    expect(api.to(`${BASE}/l1`, 'PUT')[0]!.body).toMatchObject({ title: 'Мое уредување', version: 0 });
  });

  it('cancels a future lesson with a reason, after which it is read-only', async () => {
    const user = userEvent.setup();
    const { api } = lessonsApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/lessons/l1`);

    expect(await screen.findByRole('button', { name: 'Означи како одржан' })).toBeDisabled(); // 2030: not started yet
    await user.click(screen.getByRole('button', { name: 'Откажи го часот' }));
    await user.type(screen.getByLabelText('Причина за откажување'), 'Болен');
    await user.click(screen.getAllByRole('button', { name: 'Откажи го часот' }).at(-1)!);

    expect(await screen.findByText('Откажан · Болен')).toBeInTheDocument();
    expect(api.to(`${BASE}/l1/cancel`)[0]!.body).toEqual({ version: 0, reason: 'Болен' });
    expect(screen.queryByRole('button', { name: 'Зачувај' })).toBeNull();
    expect(screen.getByLabelText('Наслов')).toBeDisabled();
  });
});

describe('the current month and week', () => {
  it('default to today in the workspace time zone', async () => {
    const { api } = lessonsApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/calendar`);
    const week = startOfWeek(today('Europe/Skopje'));
    await expect.poll(() => api.to(BASE, 'GET').some((c) => c.search.startsWith(`?from=${week}`))).toBe(true);
  });
});
