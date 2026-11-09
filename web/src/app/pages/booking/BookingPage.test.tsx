import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, problem, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const BASE = `/api/w/${WORKSPACE_ID}/booking`;

const request = (overrides: Record<string, unknown> = {}) => ({
  id: 'b1',
  leadId: 'lead-1',
  name: 'Марија Илиевска',
  email: 'marija@example.test',
  phone: null,
  offerId: 'o1',
  offerTitle: 'Час по математика',
  startUtc: '2030-11-12T08:00:00Z',
  endUtc: '2030-11-12T09:00:00Z',
  startLocal: '2030-11-12T09:00:00',
  endLocal: '2030-11-12T10:00:00',
  timeZone: 'Europe/Skopje',
  status: 'Pending',
  lessonId: null as string | null,
  reason: null as string | null,
  createdAtUtc: '2030-11-01T09:00:00Z',
  version: 0,
  ...overrides,
});

const SETTINGS = {
  enabled: false,
  slotStepMinutes: 30,
  minNoticeHours: 12,
  horizonDays: 30,
  workingHours: [{ dayOfWeek: 1, start: '09:00:00', end: '12:00:00' }],
  timeOff: [],
  timeZone: 'Europe/Skopje',
  version: 1,
};

function bookingApi({ confirm = (): Response => json(200, request({ status: 'Confirmed', lessonId: 'l1', version: 1 })) } = {}) {
  const state = { requests: [request(), request({ id: 'b2', name: 'Борис Трајков', status: 'Confirmed', lessonId: 'l2' })] };
  const handler = (call: FakeCall) => {
    if (call.path === `${BASE}/settings` && call.method === 'GET') return json(200, SETTINGS);
    if (call.path === `${BASE}/settings` && call.method === 'PUT') return json(200, { ...SETTINGS, ...(call.body as object), version: 2 });
    if (call.path === `${BASE}/requests`) {
      const page = Number(new URLSearchParams(call.search).get('page') ?? '1');
      return json(200, { items: page === 1 ? state.requests : [], total: state.requests.length, page, pageSize: 20 });
    }
    if (call.path === `${BASE}/requests/b1/confirm`) return confirm();
    if (call.path === `${BASE}/requests/b1/decline`) {
      state.requests[0] = request({ status: 'Declined', reason: (call.body as { reason: string | null }).reason, version: 1 });
      return json(200, state.requests[0]);
    }
    if (call.path === `${BASE}/requests/b2/cancel`) return json(200, request({ id: 'b2', status: 'Cancelled', version: 1 }));
    return undefined;
  };
  return fakeApi({ me: teacher(), extra: [handler] });
}

describe('booking page', () => {
  it('lists requests and confirms one', async () => {
    const user = userEvent.setup();
    const api = bookingApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/booking`);

    const list = await screen.findByRole('list', { name: 'Барања за термин' });
    const pending = within(list).getByRole('listitem', { name: /Марија Илиевска/ });
    expect(pending).toHaveTextContent('09:00–10:00 · Час по математика');
    expect(within(pending).getByRole('link', { name: 'Марија Илиевска' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/leads/lead-1`);
    await user.click(within(pending).getByRole('button', { name: 'Потврди' }));

    await expect.poll(() => api.to(`${BASE}/requests/b1/confirm`).length).toBe(1);
    expect(api.to(`${BASE}/requests/b1/confirm`)[0]).toMatchObject({ body: { version: 0 }, xsrf: 'xsrf' });
  });

  it('explains a conflict and declines with a message instead', async () => {
    const user = userEvent.setup();
    const api = bookingApi({ confirm: () => problem(409, 'LESSON_OVERLAP') });
    await renderRoute(`/app/w/${WORKSPACE_ID}/booking`);

    const pending = within(await screen.findByRole('list', { name: 'Барања за термин' })).getByRole('listitem', { name: /Марија Илиевска/ });
    await user.click(within(pending).getByRole('button', { name: 'Потврди' }));
    expect(await within(pending).findByRole('alert')).toHaveTextContent('Веќе имате час во ова време.');

    await user.click(within(pending).getByRole('button', { name: 'Одбиј' }));
    await user.type(within(pending).getByLabelText('Порака до посетителот (по желба)'), 'Зафатено');
    await user.click(within(pending).getByRole('button', { name: 'Одбиј го барањето' }));
    await expect.poll(() => api.to(`${BASE}/requests/b1/decline`).length).toBe(1);
    expect(api.to(`${BASE}/requests/b1/decline`)[0]!.body).toEqual({ version: 0, reason: 'Зафатено' });
  });

  it('cancels a confirmed booking only with a reason', async () => {
    const user = userEvent.setup();
    const api = bookingApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/booking`);

    const confirmed = within(await screen.findByRole('list', { name: 'Барања за термин' })).getByRole('listitem', { name: /Борис Трајков/ });
    expect(within(confirmed).getByRole('link', { name: 'Отвори го часот' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/lessons/l2`);
    await user.click(within(confirmed).getByRole('button', { name: 'Откажи' }));
    expect(within(confirmed).getByRole('button', { name: 'Откажи го часот' })).toBeDisabled();
    await user.type(within(confirmed).getByLabelText('Причина за откажување'), 'Болест');
    await user.click(within(confirmed).getByRole('button', { name: 'Откажи го часот' }));
    await expect.poll(() => api.to(`${BASE}/requests/b2/cancel`).length).toBe(1);
    expect(api.to(`${BASE}/requests/b2/cancel`)[0]!.body).toEqual({ version: 0, reason: 'Болест' });
  });

  it('pages through many requests', async () => {
    const user = userEvent.setup();
    const handler = (call: FakeCall) => {
      if (call.path === `${BASE}/settings`) return json(200, SETTINGS);
      if (call.path !== `${BASE}/requests`) return undefined;
      const page = Number(new URLSearchParams(call.search).get('page') ?? '1');
      return json(200, { items: [request({ id: `b${page}`, name: page === 1 ? 'Прва' : 'Втора' })], total: 25, page, pageSize: 20 });
    };
    const api = fakeApi({ me: teacher(), extra: [handler] });
    await renderRoute(`/app/w/${WORKSPACE_ID}/booking`);

    expect(await screen.findByText('Страница 1 од 2')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Следна' }));
    expect(await screen.findByRole('listitem', { name: /Втора/ })).toBeInTheDocument();
    expect(api.to(`${BASE}/requests`).some((c) => new URLSearchParams(c.search).get('page') === '2')).toBe(true);
  });

  it('saves working hours, days off and the rules with the loaded version', async () => {
    const user = userEvent.setup();
    const api = bookingApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/booking`);

    await user.click(await screen.findByLabelText('Посетителите можат да закажуваат на мојата јавна страница'));
    await user.click(screen.getByRole('button', { name: 'Додај време во среда' }));
    await user.click(screen.getByRole('button', { name: 'Додај неработни денови' }));
    await user.type(screen.getByLabelText('Неработно од'), '2030-12-24');
    await user.type(screen.getByLabelText('Неработно до'), '2030-12-26');
    await user.click(screen.getByRole('button', { name: 'Зачувај' }));

    await expect.poll(() => api.to(`${BASE}/settings`, 'PUT').length).toBe(1);
    expect(api.to(`${BASE}/settings`, 'PUT')[0]!.body).toEqual({
      enabled: true,
      slotStepMinutes: 30,
      minNoticeHours: 12,
      horizonDays: 30,
      workingHours: [
        { dayOfWeek: 1, start: '09:00:00', end: '12:00:00' },
        { dayOfWeek: 3, start: '09:00:00', end: '17:00:00' },
      ],
      timeOff: [{ startDate: '2030-12-24', endDate: '2030-12-26', note: null }],
      version: 1,
    });
    expect(await screen.findByText('Зачувано.')).toBeInTheDocument();

    // A second save uses the new version.
    await user.click(screen.getByRole('button', { name: 'Зачувај' }));
    await expect.poll(() => api.to(`${BASE}/settings`, 'PUT').length).toBe(2);
    expect(api.to(`${BASE}/settings`, 'PUT')[1]!.body).toMatchObject({ version: 2 });
  });
});
