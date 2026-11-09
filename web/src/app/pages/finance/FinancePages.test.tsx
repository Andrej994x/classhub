import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, problem, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const FINANCE = `/api/w/${WORKSPACE_ID}/finance`;
const STUDENT = `/api/w/${WORKSPACE_ID}/students/s1`;

type Entry = {
  id: string;
  kind: string;
  occurredAtUtc: string;
  amountMinor: number | null;
  currency: string | null;
  lessons: number | null;
  packageId: string | null;
  packageTitle: string | null;
  lessonId: string | null;
  lessonTitle: string | null;
  paidOn: string | null;
  method: string | null;
  note: string | null;
  reversible: boolean;
};

const entry = (overrides: Partial<Entry>): Entry => ({
  id: 'e1',
  kind: 'Payment',
  occurredAtUtc: '2026-11-05T09:00:00Z',
  amountMinor: null,
  currency: null,
  lessons: null,
  packageId: null,
  packageTitle: null,
  lessonId: null,
  lessonTitle: null,
  paidOn: null,
  method: null,
  note: null,
  reversible: false,
  ...overrides,
});

const pkg = (overrides: Record<string, unknown> = {}) => ({
  id: 'p1',
  title: 'Пакет 4 часа',
  offerId: null as string | null,
  lessonCount: 4,
  usedLessons: 1,
  remainingLessons: 3,
  priceMinor: 480_000,
  currency: 'MKD',
  closed: false,
  createdAtUtc: '2026-11-01T09:00:00Z',
  version: 0,
  ...overrides,
});

const studentDetail = {
  id: 's1',
  firstName: 'Ана',
  lastName: 'Илиева',
  email: null,
  phone: null,
  isMinor: false,
  subjectIds: [],
  archived: false,
  guardianName: null,
  guardianEmail: null,
  guardianPhone: null,
  sourceLeadId: null,
  accountLinked: false,
  invitation: null,
  createdAtUtc: '2026-11-01T09:00:00Z',
  updatedAtUtc: '2026-11-01T09:00:00Z',
  version: 0,
};

function financeApi() {
  const state = {
    finance: {
      studentId: 's1',
      balances: [{ currency: 'MKD', chargedMinor: 480_000, paidMinor: 200_000, outstandingMinor: 280_000 }],
      packages: [pkg()],
      unpaidLessons: 0,
      entries: [
        entry({ id: 'pay1', kind: 'Payment', amountMinor: 200_000, currency: 'MKD', paidOn: '2026-11-02', method: 'Cash', reversible: true }),
        entry({ id: 'l1', kind: 'LessonConsumed', lessons: 1, packageId: 'p1', packageTitle: 'Пакет 4 часа', lessonTitle: 'Равенки', reversible: true }),
      ],
      historyTotal: 2,
      historyPage: 1,
      historyPageSize: 50,
    },
  };
  const handler = (call: FakeCall) => {
    if (call.path === STUDENT) return json(200, studentDetail);
    if (call.path === `${STUDENT}/notes`) return json(200, []);
    if (call.path === `/api/w/${WORKSPACE_ID}/offers`) {
      return json(200, [
        { id: 'o1', kind: 'Package', title: 'Пакет математика', priceMinor: 480_000, currency: 'MKD', lessonCount: 4, isActive: true, archived: false },
        { id: 'o2', kind: 'SingleLesson', title: 'Неактивна', priceMinor: 1000, currency: 'MKD', lessonCount: null, isActive: false, archived: false },
      ]);
    }
    if (call.path === `${FINANCE}/students/s1`) {
      const page = Number(new URLSearchParams(call.search).get('historyPage') ?? '1');
      return json(200, page === 1 ? state.finance : { ...state.finance, historyPage: page, entries: [entry({ id: 'old', kind: 'Payment', amountMinor: 100, currency: 'MKD', paidOn: '2025-01-10', method: 'Cash', reversible: true })] });
    }
    if (call.path === `${FINANCE}/students/s1/packages`) {
      state.finance = { ...state.finance, packages: [pkg({ id: 'p2', title: 'Пакет математика', usedLessons: 0, remainingLessons: 4 }), ...state.finance.packages] };
      return json(200, state.finance);
    }
    if (call.path === `${FINANCE}/students/s1/payments`) {
      const body = call.body as { amountMinor: number };
      state.finance = {
        ...state.finance,
        balances: [{ currency: 'MKD', chargedMinor: 480_000, paidMinor: 200_000 + body.amountMinor, outstandingMinor: 280_000 - body.amountMinor }],
      };
      return json(200, state.finance);
    }
    if (call.path === `${FINANCE}/students/s1/adjustments`) return json(200, state.finance);
    if (call.path === `${FINANCE}/payments/pay1/reverse`) return problem(409, 'CONCURRENCY_CONFLICT');
    if (call.path === `${FINANCE}/ledger/l1/reverse`) {
      state.finance = { ...state.finance, packages: [pkg({ usedLessons: 0, remainingLessons: 4 })] };
      return json(200, state.finance);
    }
    if (call.path === `${FINANCE}/packages/p1/lesson-adjustments`) return problem(422, 'PACKAGE_LESSONS_OUT_OF_RANGE');
    if (call.path === `${FINANCE}/overview`) {
      return json(200, {
        students: [
          { studentId: 's1', name: 'Ана Илиева', archived: false, balances: [{ currency: 'MKD', chargedMinor: 480_000, paidMinor: 200_000, outstandingMinor: 280_000 }], remainingLessons: 3, unpaidLessons: 0 },
          { studentId: 's2', name: 'Борис Трајков', archived: false, balances: [{ currency: 'EUR', chargedMinor: 10_000, paidMinor: 15_000, outstandingMinor: -5_000 }], remainingLessons: 0, unpaidLessons: 2 },
        ],
      });
    }
    if (call.path === `${FINANCE}/report`) {
      return json(200, {
        from: '2026-11-01',
        to: '2026-11-30',
        currencies: [
          { currency: 'EUR', packagesSold: 1, chargedMinor: 10_000, receivedMinor: 15_000 },
          { currency: 'MKD', packagesSold: 2, chargedMinor: 960_000, receivedMinor: 200_000 },
        ],
        lessonsConsumed: 5,
        unpaidLessons: 2,
      });
    }
    return undefined;
  };
  const api = fakeApi({ me: teacher(), extra: [handler] });
  return { api, state };
}

describe('finance on the student page', () => {
  it('shows the balance per currency, the packages and the history', async () => {
    financeApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/students/s1`);

    const balances = await screen.findByRole('list', { name: 'Состојба по валута' });
    expect(within(balances).getByText(/Должи 2\.800,00/)).toBeInTheDocument();
    const packages = screen.getByRole('list', { name: 'Пакети' });
    expect(within(packages).getByText('Преостануваат 3 од 4 часа')).toBeInTheDocument();
    const history = screen.getByRole('list', { name: 'Историја' });
    expect(within(history).getByText('Уплата')).toBeInTheDocument();
    expect(within(history).getByText('Одржан час')).toBeInTheDocument();
  });

  it('pages through a long history so older entries stay reachable', async () => {
    const user = userEvent.setup();
    const { api, state } = financeApi();
    state.finance = { ...state.finance, historyTotal: 60 };
    await renderRoute(`/app/w/${WORKSPACE_ID}/students/s1`);

    const pager = await screen.findByRole('navigation', { name: 'Страници од историјата' });
    expect(within(pager).getByText('Страница 1 од 2')).toBeInTheDocument();
    await user.click(within(pager).getByRole('button', { name: 'Следна' }));

    await expect.poll(() => api.to(`${FINANCE}/students/s1`).some((c) => new URLSearchParams(c.search).get('historyPage') === '2')).toBe(true);
    expect(await within(screen.getByRole('list', { name: 'Историја' })).findByText(/1,00/)).toBeInTheDocument();
    expect(screen.getByText('Страница 2 од 2')).toBeInTheDocument();
  });

  it('assigns a package from an active offer', async () => {
    const user = userEvent.setup();
    const { api } = financeApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/students/s1`);

    await user.click(await screen.findByRole('button', { name: 'Додели пакет' }));
    const offer = screen.getByLabelText('Од понуда');
    expect(within(offer).queryByRole('option', { name: /Неактивна/ })).toBeNull(); // inactive offers are not offered
    await user.selectOptions(offer, 'o1');
    expect(screen.getByText(/подоцнежните промени на понудата не го менуваат пакетот/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Додели' }));

    await expect.poll(() => api.to(`${FINANCE}/students/s1/packages`, 'POST').length).toBe(1);
    expect(api.to(`${FINANCE}/students/s1/packages`, 'POST')[0]!.body).toEqual({ offerId: 'o1', title: null, lessonCount: null, priceMinor: null, currency: null });
    expect(await screen.findByText('Пакет математика')).toBeInTheDocument();
  });

  it('assigns a custom package in another currency typed by the teacher', async () => {
    const user = userEvent.setup();
    const { api } = financeApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/students/s1`);

    await user.click(await screen.findByRole('button', { name: 'Додели пакет' }));
    await user.type(screen.getByLabelText('Назив'), 'Online course');
    await user.clear(screen.getByLabelText('Часови'));
    await user.type(screen.getByLabelText('Часови'), '2');
    await user.type(screen.getByLabelText('Цена на целиот пакет'), '100');
    await user.selectOptions(screen.getByLabelText('Валута'), '*');
    expect(screen.getByLabelText('Валута')).toHaveValue(''); // empty, ready for another currency
    await user.type(screen.getByLabelText('Валута'), 'eur');
    expect(screen.getByLabelText('Валута')).toHaveValue('EUR');
    await user.click(screen.getByRole('button', { name: 'Додели' }));

    await expect.poll(() => api.to(`${FINANCE}/students/s1/packages`, 'POST').length).toBe(1);
    expect(api.to(`${FINANCE}/students/s1/packages`, 'POST')[0]!.body).toEqual({ offerId: null, title: 'Online course', lessonCount: 2, priceMinor: 10_000, currency: 'EUR' });
  });

  it('records a partial payment in minor units and checks the amount first', async () => {
    const user = userEvent.setup();
    const { api } = financeApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/students/s1`);

    await user.click(await screen.findByRole('button', { name: 'Евидентирај уплата' }));
    await user.type(screen.getByLabelText('Износ'), '12,3x');
    await user.click(screen.getAllByRole('button', { name: 'Евидентирај уплата' }).at(-1)!);
    expect(await screen.findByText('Внесете износ, на пр. 1500 или 25,50.')).toBeInTheDocument();
    expect(api.to(`${FINANCE}/students/s1/payments`)).toHaveLength(0);

    await user.clear(screen.getByLabelText('Износ'));
    await user.type(screen.getByLabelText('Износ'), '1000,50');
    await user.selectOptions(screen.getByLabelText('Начин'), 'BankTransfer');
    await user.click(screen.getAllByRole('button', { name: 'Евидентирај уплата' }).at(-1)!);

    await expect.poll(() => api.to(`${FINANCE}/students/s1/payments`).length).toBe(1);
    expect(api.to(`${FINANCE}/students/s1/payments`)[0]!.body).toMatchObject({ amountMinor: 100_050, currency: 'MKD', method: 'BankTransfer', note: null, packageId: null });
    expect(await screen.findByText(/Должи 1\.799,50/)).toBeInTheDocument();
  });

  it('reverses with a reason and explains conflicts and limits', async () => {
    const user = userEvent.setup();
    const { api } = financeApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/students/s1`);

    const history = await screen.findByRole('list', { name: 'Историја' });
    await user.click(within(history).getAllByRole('button', { name: 'Поништи…' })[1]!);
    await user.type(screen.getByLabelText('Зошто се поништува?'), 'Беше отсутен');
    await user.click(screen.getByRole('button', { name: 'Поништи' }));
    await expect.poll(() => api.to(`${FINANCE}/ledger/l1/reverse`).length).toBe(1);
    expect(api.to(`${FINANCE}/ledger/l1/reverse`)[0]!.body).toEqual({ reason: 'Беше отсутен' });
    expect(await screen.findByText('Преостануваат 4 од 4 часа')).toBeInTheDocument();

    await user.click(within(screen.getByRole('list', { name: 'Историја' })).getAllByRole('button', { name: 'Поништи…' })[0]!);
    await user.type(screen.getByLabelText('Зошто се поништува?'), 'Двапати');
    await user.click(screen.getByRole('button', { name: 'Поништи' }));
    expect(await screen.findByText('Ова веќе е поништено. Освежете ја страницата.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Коригирај часови' }));
    await user.clear(screen.getByLabelText('Часови (+ искористени, − вратени)'));
    await user.type(screen.getByLabelText('Часови (+ искористени, − вратени)'), '9');
    await user.type(screen.getByLabelText('Причина'), 'Тест');
    await user.click(screen.getByRole('button', { name: 'Зачувај ја корекцијата' }));
    expect(await screen.findByText(/Корекцијата би оставила пакет/)).toBeInTheDocument();
  });

  it('records a balance correction as a negative amount when the student owes less', async () => {
    const user = userEvent.setup();
    const { api } = financeApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/students/s1`);

    await user.click(await screen.findByRole('button', { name: 'Коригирај ја состојбата' }));
    await user.click(screen.getByRole('button', { name: 'Зачувај ја корекцијата' }));
    expect(await screen.findByText('Наведете причина.')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Износ'), '400');
    await user.type(screen.getByLabelText('Причина'), 'Попуст');
    await user.click(screen.getByRole('button', { name: 'Зачувај ја корекцијата' }));

    await expect.poll(() => api.to(`${FINANCE}/students/s1/adjustments`).length).toBe(1);
    expect(api.to(`${FINANCE}/students/s1/adjustments`)[0]!.body).toEqual({ amountMinor: -40_000, currency: 'MKD', reason: 'Попуст' });
  });
});

describe('finance page', () => {
  it('lists who owes what per currency and a period report without mixing currencies', async () => {
    const user = userEvent.setup();
    const { api } = financeApi();
    await renderRoute(`/app/w/${WORKSPACE_ID}/finance`);

    const students = await screen.findByRole('list', { name: 'Ученици' });
    expect(within(students).getByRole('link', { name: 'Ана Илиева' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/students/s1`);
    expect(within(students).getByText(/Должи 2\.800,00.*преостануваат 3 часа/)).toBeInTheDocument();
    expect(within(students).getByText(/Платено однапред.*2 неплатени часа/)).toBeInTheDocument();

    const table = await screen.findByRole('table', { name: 'Извештај' });
    const rows = within(table).getAllByRole('row');
    expect(rows).toHaveLength(3); // header + EUR + MKD, never a total across currencies
    expect(rows[2]).toHaveTextContent('MKD');
    expect(screen.getByText('Одржани часови: 5, од нив неплатени: 2')).toBeInTheDocument();

    await user.clear(screen.getByLabelText('Од'));
    await user.type(screen.getByLabelText('Од'), '2026-10-01');
    await user.click(screen.getByRole('button', { name: 'Прикажи' }));
    await expect.poll(() => api.to(`${FINANCE}/report`).some((c) => new URLSearchParams(c.search).get('from') === '2026-10-01')).toBe(true);
  });
});
