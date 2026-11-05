import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { moneyInput, parseMoney } from '../../../lib/format.ts';
import { fakeApi, offer, SUBJECT_MATH, teacher, WORKSPACE_ID } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const OFFERS = `/app/w/${WORKSPACE_ID}/offers`;
const API = `/api/w/${WORKSPACE_ID}/offers`;

describe('money input', () => {
  it.each([
    ['1500', 'MKD', 150_000],
    ['25,50', 'EUR', 2550],
    ['25.5', 'EUR', 2550],
    ['0', 'EUR', 0],
    ['1200', 'JPY', 1200],
  ])('parses %s %s into minor units', (input, currency, minor) => {
    expect(parseMoney(input, currency)).toBe(minor);
  });

  it.each(['-5', '1.234', 'abc', '', '1 000', '12.5', '1e3'])('rejects %s', (input) => {
    expect(parseMoney(input, input === '12.5' ? 'JPY' : 'EUR')).toBeNull();
  });

  it('formats minor units back for the input field', () => {
    expect(moneyInput(150_000, 'MKD')).toBe('1500.00');
    expect(moneyInput(1200, 'JPY')).toBe('1200');
  });
});

describe('offers page', () => {
  it('lists offers with kind, subject, duration, price and status', async () => {
    const api = fakeApi({ me: teacher() });
    api.state.offers = [
      offer(),
      offer({ id: 'offer-2', kind: 'Package', title: 'Пакет 10 часа', lessonCount: 10, priceMinor: 1_200_000, displayOrder: 2, isActive: false }),
    ];
    await renderRoute(OFFERS);

    const items = within(await screen.findByRole('list', { name: 'Понуди' })).getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('Индивидуален час · Математика · 60 минути');
    expect(items[1]).toHaveTextContent('Пакет часови · Математика · 60 минути · 10 часа');
    expect(items[1]).toHaveTextContent('Неактивна (без нови барања)');
    expect(within(items[0]!).getByTestId('offer-price')).toHaveTextContent(/1\.500,00/);
  });

  it('creates a package: price in minor units, lesson count, no seats', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    const { router } = await renderRoute(`${OFFERS}/new`);

    await user.click(await screen.findByRole('radio', { name: 'Пакет часови' }));
    await user.type(screen.getByLabelText('Наслов'), 'Пакет 10 часа');
    await user.selectOptions(screen.getByLabelText('Предмет'), SUBJECT_MATH);
    await user.clear(screen.getByLabelText('Времетраење на час (минути)'));
    await user.type(screen.getByLabelText('Времетраење на час (минути)'), '45');
    await user.type(screen.getByLabelText('Цена'), '12000,50');
    await user.type(screen.getByLabelText('Часови во пакетот'), '10');
    await user.click(screen.getByRole('button', { name: 'Зачувај понуда' }));

    await screen.findByRole('heading', { level: 1, name: 'Понуди' });
    expect(router.state.location.pathname).toBe(OFFERS);
    expect(api.to(API, 'POST')[0]).toMatchObject({
      xsrf: 'xsrf',
      body: { kind: 'Package', title: 'Пакет 10 часа', subjectId: SUBJECT_MATH, durationMinutes: 45, priceMinor: 1_200_050, currency: 'MKD', lessonCount: 10, seats: null, version: null },
    });
  });

  it('validates kind-specific fields and the price in the browser', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    await renderRoute(`${OFFERS}/new`);

    await user.click(await screen.findByRole('radio', { name: 'Групна програма' }));
    await user.type(screen.getByLabelText('Наслов'), 'Група');
    await user.type(screen.getByLabelText('Цена'), '-10');
    await user.type(screen.getByLabelText('Места'), '0');
    await user.click(screen.getByRole('button', { name: 'Зачувај понуда' }));

    expect(await screen.findByText('Внесете цена од 0 или повеќе, со најмногу две децимали.')).toBeInTheDocument();
    expect(screen.getByText('Групната програма има помеѓу 1 и 500 места.')).toBeInTheDocument();
    expect(screen.getByText('Изберете предмет.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Часови во пакетот')).not.toBeInTheDocument();
    expect(api.to(API, 'POST')).toHaveLength(0);
  });

  it('edits with the loaded version and explains a conflict', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.offers = [offer({ version: 4 })];
    await renderRoute(`${OFFERS}/offer-1`);

    const title = await screen.findByLabelText('Наслов');
    await user.clear(title);
    await user.type(title, 'Нов наслов');
    api.state.offers[0]!.version = 5; // saved meanwhile in another tab
    await user.click(screen.getByRole('button', { name: 'Зачувај понуда' }));

    expect(await screen.findByText(/Понудата е променета на друго место/)).toBeInTheDocument();
    expect(api.to(`${API}/offer-1`, 'PUT')[0]?.body).toMatchObject({ title: 'Нов наслов', version: 4, priceMinor: 150_000 });
  });

  it('submits the version the form was loaded with, even after a background refetch', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.offers = [offer({ version: 4 })];
    const { queryClient } = await renderRoute(`${OFFERS}/offer-1`);
    const title = await screen.findByLabelText('Наслов');
    await user.clear(title);
    await user.type(title, 'Мој наслов');

    // Another tab saved; this tab refetches the offer in the background while the form keeps its values.
    api.state.offers[0] = { ...api.state.offers[0]!, title: 'Од другиот таб', version: 5 };
    await queryClient.invalidateQueries({ queryKey: ['w', WORKSPACE_ID, 'offers', 'offer-1'] });
    await user.click(screen.getByRole('button', { name: 'Зачувај понуда' }));

    expect(await screen.findByText(/Понудата е променета на друго место/)).toBeInTheDocument();
    expect(api.to(`${API}/offer-1`, 'PUT')[0]?.body).toMatchObject({ title: 'Мој наслов', version: 4 });
  });

  it('reorders and archives (with confirmation)', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.offers = [offer({ id: 'a', title: 'A' }), offer({ id: 'b', title: 'B', displayOrder: 2 })];
    await renderRoute(OFFERS);

    await user.click(await screen.findByRole('button', { name: 'Помести „B“ нагоре' }));
    await expect.poll(() => api.to(`${API}/order`, 'PUT')[0]?.body).toEqual({ offerIds: ['b', 'a'] });

    const list = screen.getByRole('list', { name: 'Понуди' });
    await user.click(within(within(list).getAllByRole('listitem')[0]!).getByRole('button', { name: 'Архивирај' }));
    await user.click(screen.getByRole('button', { name: 'Да, архивирај' }));

    await expect.poll(() => api.to(`${API}/b`, 'DELETE').length).toBe(1);
    await expect.poll(() => within(screen.getByRole('list', { name: 'Понуди' })).getAllByRole('listitem').length).toBe(1);
  });

  it('shows "not found" for an offer of another workspace', async () => {
    fakeApi({ me: teacher() });
    await renderRoute(`${OFFERS}/missing`);
    expect(await screen.findByRole('heading', { level: 1, name: 'Страницата не е пронајдена' })).toBeInTheDocument();
  });
});
