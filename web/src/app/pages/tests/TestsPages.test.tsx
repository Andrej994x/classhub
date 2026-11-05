import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, SUBJECT_MATH, teacher, TEST, WORKSPACE_ID } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const LIST = `/app/w/${WORKSPACE_ID}/tests`;
const API = `/api/w/${WORKSPACE_ID}/tests`;

type SavedBody = {
  title: string;
  slug: string | null;
  subjectId: string;
  difficulty: string;
  version: number | null;
  questions: { id: string | null; text: string; points: number; options: { id: string | null; text: string; isCorrect: boolean }[] }[];
};

describe('diagnostic tests', () => {
  it('lists tests with status, subject, questions, points and attempts', async () => {
    const api = fakeApi({ me: teacher() });
    api.state.tests = [TEST({ status: 'Published', attemptCount: 3 })];
    await renderRoute(LIST);

    const item = within(await screen.findByRole('list', { name: 'Дијагностички тестови' })).getByRole('listitem');
    expect(item).toHaveTextContent('Математика: дропки');
    expect(item).toHaveTextContent('Објавен');
    expect(item).toHaveTextContent('Математика · Средно · 1 прашање · 2 бода · 3 решавања');
    expect(within(item).getByRole('link')).toHaveAttribute('href', `${LIST}/test-1`);
  });

  it('creates a test with questions, options, correct answers and points', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    const { router } = await renderRoute(`${LIST}/new`);

    await user.type(await screen.findByLabelText('Наслов'), 'Физика: сила');
    await user.selectOptions(screen.getByLabelText('Предмет'), SUBJECT_MATH);
    await user.selectOptions(screen.getByLabelText('Тежина'), 'Advanced');
    await user.type(screen.getByLabelText('Прашање 1'), 'Единица за сила?');
    await user.type(screen.getByRole('textbox', { name: 'Прашање 1, одговор 1' }), 'Њутн');
    await user.type(screen.getByRole('textbox', { name: 'Прашање 1, одговор 2' }), 'Џул');
    await user.click(screen.getByRole('checkbox', { name: 'Прашање 1, одговор 1 е точен' }));
    await user.clear(screen.getByLabelText('Бодови за прашање 1'));
    await user.type(screen.getByLabelText('Бодови за прашање 1'), '3');
    await user.click(screen.getByRole('button', { name: 'Додај одговор' }));
    await user.type(screen.getByRole('textbox', { name: 'Прашање 1, одговор 3' }), 'Ват');
    await user.click(screen.getByRole('button', { name: 'Креирај нацрт' }));

    await expect.poll(() => router.state.location.pathname).toBe(`${LIST}/test-1`);
    const body = api.to(API, 'POST')[0]?.body as SavedBody;
    expect(body).toMatchObject({ title: 'Физика: сила', slug: null, subjectId: SUBJECT_MATH, difficulty: 'Advanced', version: null });
    expect(body.questions).toEqual([
      { id: null, text: 'Единица за сила?', points: 3, options: [{ id: null, text: 'Њутн', isCorrect: true }, { id: null, text: 'Џул', isCorrect: false }, { id: null, text: 'Ват', isCorrect: false }] },
    ]);
  });

  it('validates in the browser before saving', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    await renderRoute(`${LIST}/new`);

    await user.clear(await screen.findByLabelText('Бодови за прашање 1'));
    await user.type(screen.getByLabelText('Бодови за прашање 1'), '101');
    await user.click(screen.getByRole('button', { name: 'Креирај нацрт' }));

    expect(await screen.findByText('Проверете ги означените полиња.')).toBeInTheDocument();
    expect(screen.getByText('Внесете цел број од 0 до 100.')).toBeInTheDocument();
    expect(screen.getByText('Изберете предмет.')).toBeInTheDocument();
    expect(api.to(API, 'POST')).toHaveLength(0);
  });

  it('keeps ids when editing, publishes with the saved version and shows what is missing', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.tests = [TEST({ version: 2, questions: [{ id: 'q-1', text: 'Без точен?', points: 1, options: [{ id: 'o-1', text: 'А', isCorrect: false }, { id: 'o-2', text: 'Б', isCorrect: false }] }] })];
    await renderRoute(`${LIST}/test-1`);

    const card = await screen.findByTestId('test-status');
    await user.click(within(card).getByRole('button', { name: 'Објави' }));
    expect(await within(card).findByText('Прашањето 1 треба да има најмалку два одговори и еден точен.')).toBeInTheDocument();

    await user.click(screen.getByRole('checkbox', { name: 'Прашање 1, одговор 2 е точен' }));
    expect(within(card).getByRole('button', { name: 'Објави' })).toBeDisabled(); // unsaved changes
    await user.click(screen.getByRole('button', { name: 'Зачувај тест' }));
    expect(await screen.findByText('Тестот е зачуван.')).toBeInTheDocument();
    const saved = api.to(`${API}/test-1`, 'PUT')[0]?.body as SavedBody;
    expect(saved.version).toBe(2);
    expect(saved.questions[0]).toEqual({ id: 'q-1', text: 'Без точен?', points: 1, options: [{ id: 'o-1', text: 'А', isCorrect: false }, { id: 'o-2', text: 'Б', isCorrect: true }] });

    await user.click(within(card).getByRole('button', { name: 'Објави' }));
    expect(await within(card).findByText('Статус: Објавен')).toBeInTheDocument();
    expect(api.to(`${API}/test-1/publish`, 'POST').at(-1)?.body).toEqual({ version: 3 });
  });

  it('sends the version the form was loaded from, even after a background refresh brought a newer one', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.tests = [TEST({ status: 'Published', version: 1 })];
    const { queryClient } = await renderRoute(`${LIST}/test-1`);

    await user.type(await screen.findByLabelText('Прашање 1'), ' (нова верзија)'); // unsaved edit
    // Another session saves meanwhile; a background refetch (e.g. after reconnecting) brings version 2.
    api.state.tests[0] = { ...api.state.tests[0]!, title: 'Променето на друго место', version: 2 };
    await queryClient.refetchQueries({ queryKey: ['w', WORKSPACE_ID, 'tests', 'test-1'] });
    expect(api.to(`${API}/test-1`, 'GET').length).toBeGreaterThan(1);

    await user.click(within(screen.getByTestId('test-status')).getByRole('button', { name: 'Деактивирај' }));
    expect(api.to(`${API}/test-1/deactivate`, 'POST')[0]?.body).toEqual({ version: 1 });
    expect(await screen.findByText(/Тестот е променет на друго место/)).toBeInTheDocument();
  });

  it('locks the structure once a test has attempts and offers a new version', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.tests = [TEST({ status: 'Published', attemptCount: 4 })];
    const { router } = await renderRoute(`${LIST}/test-1`);

    expect(await screen.findByText(/Овој тест има 4 решавања/)).toBeInTheDocument();
    expect(screen.getByLabelText('Бодови за прашање 1')).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: 'Прашање 1, одговор 1 е точен' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Додај прашање' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Отстрани го прашањето 1' })).not.toBeInTheDocument();
    expect(screen.getByLabelText('Прашање 1')).toBeEnabled(); // texts can still be corrected

    await user.click(screen.getByRole('button', { name: 'Дуплирај како нова верзија' }));
    await expect.poll(() => router.state.location.pathname).toBe(`${LIST}/test-2`);
    expect(await screen.findByRole('heading', { level: 1, name: 'Математика: дропки (2)' })).toBeInTheDocument();
    expect(screen.queryByText(/решавања:/)).not.toBeInTheDocument();
    expect(screen.getByLabelText('Бодови за прашање 1')).toBeEnabled();
  });

  it('archives a test after a confirmation', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    api.state.tests = [TEST()];
    const { router } = await renderRoute(`${LIST}/test-1`);

    await user.click(await screen.findByRole('button', { name: 'Архивирај' }));
    await user.click(screen.getByRole('button', { name: 'Да, архивирај' }));
    await expect.poll(() => router.state.location.pathname).toBe(LIST);
    expect(await screen.findByText(/Сè уште нема тестови/)).toBeInTheDocument();
    expect(api.to(`${API}/test-1`, 'DELETE')).toHaveLength(1);
  });
});
