import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';
import { today } from '../../../teacher/lessons.ts';

const SERIES = `/api/w/${WORKSPACE_ID}/lesson-series`;

const series = (overrides: Record<string, unknown> = {}) => ({
  id: 'r1',
  kind: 'Individual',
  studentId: 's1',
  groupId: null,
  subjectId: null,
  title: 'Математика',
  topic: null,
  startDate: '2030-11-12',
  startTime: '17:00:00',
  durationMinutes: 60,
  intervalWeeks: 1,
  endDate: null as string | null,
  count: 10 as number | null,
  mode: 'Online',
  meetingUrl: null,
  location: null,
  timeZone: 'Europe/Skopje',
  replacedBySeriesId: null as string | null,
  skips: [{ date: '2030-11-26', reason: 'Overlap' }],
  createdAtUtc: '2030-11-01T09:00:00Z',
  updatedAtUtc: '2030-11-01T09:00:00Z',
  version: 2,
  ...overrides,
});

const occurrence = {
  id: 'l5',
  kind: 'Individual',
  groupId: null,
  groupName: null,
  participants: [{ studentId: 's1', firstName: 'Ана', lastName: 'Илиева' }],
  subjectId: null,
  title: 'Математика',
  topic: null,
  startUtc: '2030-12-10T16:00:00Z',
  endUtc: '2030-12-10T17:00:00Z',
  startLocal: '2030-12-10T17:00:00',
  endLocal: '2030-12-10T18:00:00',
  timeZone: 'Europe/Skopje',
  mode: 'Online',
  meetingUrl: null,
  location: null,
  status: 'Scheduled',
  cancellationReason: null,
  createdAtUtc: '2030-11-01T09:00:00Z',
  updatedAtUtc: '2030-11-01T09:00:00Z',
  version: 0,
  seriesId: 'r1',
  seriesIndex: 4,
  seriesException: false,
};

function seriesApi(current = series()) {
  return seriesApiWith(() => undefined, current);
}

function seriesApiWith(first: (call: FakeCall) => Response | Promise<Response> | undefined, current = series()) {
  const state = { series: { r1: current } as Record<string, ReturnType<typeof series>> };
  const handler = (call: FakeCall) => {
    if (call.path === `/api/w/${WORKSPACE_ID}/students` && call.method === 'GET') {
      return json(200, {
        items: [{ id: 's1', firstName: 'Ана', lastName: 'Илиева', email: null, phone: null, isMinor: false, subjectIds: [], archived: false, updatedAtUtc: '2030-11-01T09:00:00Z' }],
        total: 1,
        page: 1,
        pageSize: 20,
        activeCount: 1,
      });
    }
    if (call.path === `/api/w/${WORKSPACE_ID}/groups`) return json(200, []);
    if (call.path === `/api/w/${WORKSPACE_ID}/lessons/l5`) return json(200, occurrence);
    if (call.path === SERIES && call.method === 'POST') {
      state.series.r2 = series({ ...(call.body as object), id: 'r2', skips: [], version: 0 });
      return json(201, { series: state.series.r2, created: 8, conflicts: [] });
    }
    const match = /lesson-series\/(r\d)(\/end)?$/.exec(call.path);
    if (!match) return undefined;
    if (call.method === 'GET') return json(200, state.series[match[1]!]);
    if (call.method === 'PUT') {
      const body = call.body as { series: object };
      state.series.r3 = series({ ...body.series, id: 'r3', skips: [], version: 0 });
      state.series[match[1]!] = series({ ...state.series[match[1]!], replacedBySeriesId: 'r3' });
      return json(200, { series: state.series.r3, created: 5, conflicts: [] });
    }
    state.series[match[1]!] = series({ ...state.series[match[1]!], endDate: (call.body as { fromDate: string }).fromDate, version: 3 });
    return json(200, state.series[match[1]!]);
  };
  const api = fakeApi({ me: teacher(), extra: [first, handler] });
  return { api, state };
}

describe('recurring lessons', () => {
  it('schedules a weekly series from the lesson editor and opens it', async () => {
    const user = userEvent.setup();
    const { api } = seriesApi();
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/lessons/new?date=2030-11-12`);

    await screen.findByRole('option', { name: 'Ана Илиева' });
    await user.selectOptions(screen.getByLabelText('Ученик'), 's1');
    await user.type(screen.getByLabelText('Наслов'), 'Математика');
    await user.selectOptions(screen.getByLabelText('Повторување'), '1');
    await user.clear(screen.getByLabelText('Број на часови'));
    await user.type(screen.getByLabelText('Број на часови'), '8');
    await user.click(screen.getByRole('button', { name: 'Закажи повторливи часови' }));

    await expect.poll(() => router.state.location.pathname).toBe(`/app/w/${WORKSPACE_ID}/series/r2`);
    expect(api.to(SERIES, 'POST')[0]!.body).toMatchObject({ kind: 'Individual', studentId: 's1', startDate: '2030-11-12', startTime: '17:00:00', intervalWeeks: 1, count: 8, endDate: null });
    expect(await screen.findByText(/Закажани се 8 часа/)).toBeInTheDocument();
  });

  it('changes this and the following lessons, keeping the lessons left of a counted series', async () => {
    const user = userEvent.setup();
    const { api } = seriesApi();
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/lessons/l5`);

    expect(await screen.findByText('Овој час се повторува.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Види ја серијата' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/series/r1`);
    await user.click(screen.getByLabelText('Овој и следните часови'));
    await user.clear(screen.getByLabelText('Почеток'));
    await user.type(screen.getByLabelText('Почеток'), '18:00');
    await user.click(screen.getByRole('button', { name: 'Зачувај' }));

    await expect.poll(() => router.state.location.pathname).toBe(`/app/w/${WORKSPACE_ID}/series/r3`);
    expect(api.to(`${SERIES}/r1`, 'PUT')[0]!.body).toMatchObject({
      scope: 'ThisAndFollowing',
      fromLessonId: 'l5',
      version: 2,
      timeZone: 'Europe/Skopje',
      series: { startDate: '2030-12-10', startTime: '18:00:00', intervalWeeks: 1, count: 6, endDate: null },
    });
    expect(api.to(`/api/w/${WORKSPACE_ID}/lessons/l5`, 'PUT')).toHaveLength(0);
  });

  it('sends one end for a counted series that was ended early', async () => {
    const user = userEvent.setup();
    // 10 weekly lessons from 12 November would end on 14 January; the series was ended on 31 December.
    const { api } = seriesApi(series({ endDate: '2030-12-31', count: 10 }));
    await renderRoute(`/app/w/${WORKSPACE_ID}/lessons/l5`);

    await user.click(await screen.findByLabelText('Овој и следните часови'));
    await expect.poll(() => (screen.getByRole('button', { name: /Зачувај|Се вчитува/ }) as HTMLButtonElement).disabled).toBe(false);
    await user.click(screen.getByRole('button', { name: 'Зачувај' }));

    await expect.poll(() => api.to(`${SERIES}/r1`, 'PUT').length).toBe(1);
    expect(api.to(`${SERIES}/r1`, 'PUT')[0]!.body).toMatchObject({ series: { endDate: '2030-12-31', count: null } });
  });

  it('never falls back to changing only this lesson while the series is not loaded', async () => {
    const user = userEvent.setup();
    const { api } = fakeApiWithSlowSeries();
    await renderRoute(`/app/w/${WORKSPACE_ID}/lessons/l5`);

    await user.click(await screen.findByLabelText('Овој и следните часови'));
    const save = screen.getByRole('button', { name: /Зачувај|Се вчитува/ });
    expect(save).toBeDisabled();
    await user.click(save);
    expect(api.to(`/api/w/${WORKSPACE_ID}/lessons/l5`, 'PUT')).toHaveLength(0);
    expect(api.to(`${SERIES}/r1`, 'PUT')).toHaveLength(0);
  });

  it('shows the series with its skipped dates, applies a change to all future lessons and ends it', async () => {
    const user = userEvent.setup();
    const { api } = seriesApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/series/r1`);

    expect(await screen.findByText(/Секоја недела во вторник во 17:00, 60 мин\./)).toBeInTheDocument();
    const skipped = screen.getByRole('list', { name: 'Незакажани' });
    expect(within(skipped).getByText(/друг час во тоа време/)).toBeInTheDocument();

    // The next lesson is the first date (the series starts in 2030).
    expect(screen.getByLabelText('Од')).toHaveValue('2030-11-12');
    await user.selectOptions(screen.getByLabelText('Повторување'), '2');
    await user.click(screen.getByRole('button', { name: 'Примени на сите идни часови' }));
    await expect.poll(() => api.to(`${SERIES}/r1`, 'PUT').length).toBe(1);
    expect(api.to(`${SERIES}/r1`, 'PUT')[0]!.body).toMatchObject({ scope: 'AllFuture', fromLessonId: null, timeZone: 'Europe/Skopje', series: { startDate: '2030-11-12', intervalWeeks: 2, count: 10 } });
  });

  it('offers the earlier end when a counted series was ended early', async () => {
    seriesApi(series({ endDate: '2030-12-31', count: 10 }));
    await renderRoute(`/app/w/${WORKSPACE_ID}/series/r1`);

    expect(await screen.findByLabelText('Завршува')).toHaveValue('date');
    expect(screen.getByLabelText('Краен датум')).toHaveValue('2030-12-31');
  });

  it('ends a series after confirming, from today by default', async () => {
    const user = userEvent.setup();
    const { api } = seriesApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/series/r1`);

    await user.click(await screen.findByRole('button', { name: 'Заврши ја серијата' }));
    expect(api.to(`${SERIES}/r1/end`)).toHaveLength(0);
    await user.click(screen.getByRole('button', { name: 'Да, заврши' }));
    await expect.poll(() => api.to(`${SERIES}/r1/end`).length).toBe(1);
    expect(api.to(`${SERIES}/r1/end`)[0]!.body).toEqual({ fromDate: today('Europe/Skopje'), version: 2 });
    expect(await screen.findByText('Серијата завршува.')).toBeInTheDocument();
  });

  it('points from a replaced series to the one that continues it', async () => {
    seriesApi(series({ replacedBySeriesId: 'r9' }));
    await renderRoute(`/app/w/${WORKSPACE_ID}/series/r1`);

    expect(await screen.findByRole('link', { name: 'Отвори ја новата серија' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/series/r9`);
    expect(screen.queryByRole('button', { name: 'Примени на сите идни часови' })).toBeNull();
  });
});

/** The series request never answers (still loading), everything else as usual. */
function fakeApiWithSlowSeries() {
  return seriesApiWith((call) => (call.path === `${SERIES}/r1` && call.method === 'GET' ? new Promise<Response>(() => undefined) : undefined));
}
