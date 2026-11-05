import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, teacher, WORKSPACE_ID } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const API = `/api/w/${WORKSPACE_ID}/dashboard`;

const dashboard = (days: number) => ({
  days,
  from: '2026-10-07',
  to: '2026-11-05',
  leads: {
    new: 3,
    inPeriod: 8,
    byStatus: [
      { key: 'New', count: 3 },
      { key: 'Contacted', count: 2 },
      { key: 'Converted', count: 1 },
      { key: 'Closed', count: 4 },
    ],
    byTypeInPeriod: [{ key: 'Message', count: 8 }],
  },
  tests: { published: 2, attemptsInPeriod: 12, sharedResultsInPeriod: 5 },
  site: {
    exists: true,
    published: true,
    visitsInPeriod: 200,
    daily: Array.from({ length: days }, (_, i) => ({ day: `2026-11-${String((i % 28) + 1).padStart(2, '0')}`, count: i === days - 1 ? 40 : 5 })),
  },
  offers: { active: 4, public: 3 },
  onboarding: { completed: 4, total: 5 },
  funnel: { visits: 200, leads: 8, contacted: 4, converted: 1 },
});

function api() {
  return fakeApi({
    me: teacher(),
    extra: [
      (call) => {
        if (call.path !== API) return undefined;
        const days = Number(new URLSearchParams(call.search).get('days'));
        return new Response(JSON.stringify(dashboard(days)), { status: 200, headers: { 'content-type': 'application/json' } });
      },
    ],
  });
}

describe('teacher dashboard', () => {
  it('shows the own numbers for the last 30 days with links into the inbox', async () => {
    api();
    await renderRoute(`/app/w/${WORKSPACE_ID}`);

    const tiles = await screen.findByRole('list', { name: 'Вашите бројки' });
    expect(within(tiles).getByRole('link', { name: /Нови барања\s*3/ })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/leads?status=New`);
    expect(tiles).toHaveTextContent('Барања во периодот8');
    expect(tiles).toHaveTextContent('Посети на страницата200');
    expect(tiles).toHaveTextContent('Решавања на тестови12');
    expect(tiles).toHaveTextContent('5 споделени · 2 објавени тестови');
    expect(within(tiles).getByRole('link', { name: /Активни понуди\s*4/ })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '30 дена' })).toBeChecked();
  });

  it('shows the funnel with the share of each step and the requests by status', async () => {
    api();
    await renderRoute(`/app/w/${WORKSPACE_ID}`);

    const funnel = await screen.findByRole('list', { name: 'Од посета до ученик' });
    const steps = within(funnel).getAllByRole('listitem');
    expect(steps.map((s) => s.textContent)).toEqual([
      'Посети на страницата200',
      'Барања8 · 4% од претходниот чекор',
      'Контактирани4 · 50% од претходниот чекор',
      'Конвертирани1 · 25% од претходниот чекор',
    ]);
    expect(screen.getByRole('link', { name: '4' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/leads?status=Closed`);
  });

  it('switches the period and offers the daily visits as a table', async () => {
    const user = userEvent.setup();
    const fake = api();
    await renderRoute(`/app/w/${WORKSPACE_ID}`);
    await screen.findByRole('list', { name: 'Вашите бројки' });

    await user.click(screen.getByRole('radio', { name: '7 дена' }));
    await expect.poll(() => fake.calls.some((c) => c.path === API && c.search === '?days=7')).toBe(true);
    expect(await screen.findByText('70 посети за 7 дена')).toBeInTheDocument(); // 6 × 5 + 40

    await user.click(screen.getByText('Прикажи како табела'));
    const rows = within(screen.getByRole('table')).getAllByRole('row');
    expect(rows).toHaveLength(8); // header + 7 days
    expect(rows.at(-1)).toHaveTextContent('40');
  });
});
