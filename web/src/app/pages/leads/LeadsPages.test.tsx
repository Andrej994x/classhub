import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, LEAD, teacher, WORKSPACE_ID } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const INBOX = `/app/w/${WORKSPACE_ID}/leads`;
const API = `/api/w/${WORKSPACE_ID}/leads`;

describe('lead inbox', () => {
  it('lists requests newest first with status, type and the new count', async () => {
    const api = fakeApi({ me: teacher() });
    api.state.leads = [LEAD(), LEAD({ id: 'lead-2', name: 'Бојан Колев', type: 'LessonRequest', status: 'Contacted', offerTitle: 'Пакет 10 часа', message: null })];
    await renderRoute(INBOX);

    const list = await screen.findByRole('list', { name: 'Барања' });
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('Марија Илиевска');
    expect(items[0]).toHaveTextContent('Ново');
    expect(items[1]).toHaveTextContent('Барање за час');
    expect(items[1]).toHaveTextContent('Пакет 10 часа');
    expect(screen.getByText('1 ново')).toBeInTheDocument();
    expect(within(items[0]!).getByRole('link')).toHaveAttribute('href', `${INBOX}/lead-1`);
  });

  it('filters by status and type, searches, and shows archived requests on request', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.leads = [LEAD(), LEAD({ id: 'lead-2', name: 'Бојан Колев', status: 'Closed', archived: true })];
    const { router } = await renderRoute(INBOX);
    await screen.findByRole('list', { name: 'Барања' });

    await user.selectOptions(screen.getByLabelText('Статус'), 'Contacted');
    expect(await screen.findByText('Нема барања според овие филтри.')).toBeInTheDocument();
    expect(router.state.location.search).toBe('?status=Contacted');
    expect(api.calls.some((c) => c.path === API && c.search.includes('status=Contacted'))).toBe(true);

    await user.selectOptions(screen.getByLabelText('Статус'), '');
    await user.click(screen.getByRole('checkbox', { name: 'Прикажи архивирани барања' }));
    expect(await screen.findByText('Бојан Колев')).toBeInTheDocument();
    expect(screen.queryByText('Марија Илиевска')).not.toBeInTheDocument();

    await user.click(screen.getByRole('checkbox', { name: 'Прикажи архивирани барања' }));
    await user.type(screen.getByLabelText('Пребарај (име, е-пошта, порака)'), 'математика');
    await expect.poll(() => api.calls.some((c) => c.path === API && c.search.includes('q='))).toBe(true);
    expect(await screen.findByText('Марија Илиевска')).toBeInTheDocument();
  });

  it('explains an empty inbox', async () => {
    fakeApi({ me: teacher() });
    await renderRoute(INBOX);
    expect(await screen.findByText(/Сè уште нема барања/)).toBeInTheDocument();
  });
});

describe('lead detail', () => {
  it('shows the request and moves it through the allowed statuses with the loaded version', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.leads = [LEAD({ version: 4 })];
    await renderRoute(`${INBOX}/lead-1`);

    expect(await screen.findByRole('heading', { level: 1, name: 'Марија Илиевска' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'marija@example.test' })).toHaveAttribute('href', 'mailto:marija@example.test');
    expect(screen.getByRole('link', { name: '+389 70 123 456' })).toHaveAttribute('href', 'tel:+38970123456');
    expect(screen.getByText('Здраво, ми треба помош по математика.')).toBeInTheDocument();
    expect(screen.getByText(/верзија на текстот 2026-11-05\.1/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Означи како контактирано' }));
    expect(await screen.findByText('Статус: Контактирано')).toBeInTheDocument();
    expect(api.to(`${API}/lead-1/status`, 'PUT')[0]?.body).toEqual({ status: 'Contacted', version: 4 });
    // Only the transitions allowed from "Contacted" are offered.
    expect(screen.getByRole('button', { name: 'Означи како ново' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Означи како контактирано' })).not.toBeInTheDocument();
  });

  it('adds private notes and archives and restores the request', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.leads = [LEAD()];
    await renderRoute(`${INBOX}/lead-1`);

    expect(await screen.findByText('Сè уште нема белешки. Белешките ги гледате само вие.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Зачувај белешка' })).toBeDisabled();
    await user.type(screen.getByLabelText('Додај белешка'), 'Јавив се во среда.');
    await user.click(screen.getByRole('button', { name: 'Зачувај белешка' }));
    expect(await within(await screen.findByRole('list', { name: 'Приватни белешки' })).findByText('Јавив се во среда.')).toBeInTheDocument();
    expect(screen.getByLabelText('Додај белешка')).toHaveValue('');

    await user.click(screen.getByRole('button', { name: 'Архивирај' }));
    expect(await screen.findByRole('button', { name: 'Врати од архива' })).toBeInTheDocument();
    expect(api.to(`${API}/lead-1/archive`, 'POST')[0]?.body).toEqual({ version: 0 });
  });

  it('explains a conflicting change and a missing request', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.leads = [LEAD()];
    await renderRoute(`${INBOX}/lead-1`);
    await screen.findByRole('heading', { level: 1, name: 'Марија Илиевска' });
    api.state.leads[0]!.version = 7; // changed in another tab

    await user.click(screen.getByRole('button', { name: 'Затвори' }));
    expect(await screen.findByText(/Ова барање е променето на друго место/)).toBeInTheDocument();

    await renderRoute(`${INBOX}/missing`);
    expect(await screen.findByRole('heading', { level: 1, name: 'Страницата не е пронајдена' })).toBeInTheDocument();
  });
});
