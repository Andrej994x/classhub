import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, teacher, WORKSPACE_ID } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const API = `/api/w/${WORKSPACE_ID}/dashboard/teaching`;
const months = ['2026-06', '2026-07', '2026-08', '2026-09', '2026-10', '2026-11'];

const teaching = {
  lessons: {
    next7Days: 3,
    next: [
      { id: 'l1', title: 'Математика', startUtc: '2026-11-10T16:00:00Z', endUtc: '2026-11-10T17:00:00Z', timeZone: 'Europe/Skopje', kind: 'Individual', students: ['Ана Петрова'] },
      { id: 'l2', title: 'Група А', startUtc: '2026-11-11T15:00:00Z', endUtc: '2026-11-11T16:30:00Z', timeZone: 'Europe/Skopje', kind: 'Group', students: ['Борис', 'Цвета'] },
    ],
  },
  activeStudents: 12,
  finance: {
    outstanding: [
      { currency: 'EUR', amountMinor: 5000 },
      { currency: 'MKD', amountMinor: 180_000 },
    ],
    studentsOwing: 2,
    remainingLessons: 9,
    unpaidLessons: 1,
    runningLow: [{ studentId: 's2', name: 'Борис', remainingLessons: 1 }],
    income: months.map((month) => ({ month, received: month === '2026-11' ? [{ currency: 'MKD', amountMinor: 200_000 }] : month === '2026-10' ? [{ currency: 'EUR', amountMinor: 4000 }] : [] })),
  },
};

describe('teacher dashboard: teaching', () => {
  it('shows lessons ahead, students and money per currency with links to the details', async () => {
    fakeApi({ me: teacher(), extra: [(call) => (call.path === API ? json(200, teaching) : undefined)] });
    await renderRoute(`/app/w/${WORKSPACE_ID}`);

    const tiles = await screen.findByRole('list', { name: 'Настава' });
    expect(within(tiles).getByRole('link', { name: /Часови во наредните 7 дена\s*3/ })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/calendar`);
    expect(within(tiles).getByRole('link', { name: /Активни ученици\s*12/ })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/students`);
    expect(tiles).toHaveTextContent('Преостанати часови од пакети91 ученик при крај на пакетот');
    expect(tiles).toHaveTextContent('Часови без пакет1');

    const next = screen.getByRole('list', { name: 'Следни часови' });
    expect(within(next).getByRole('link', { name: 'Математика' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/lessons/l1`);
    expect(next).toHaveTextContent('Борис, Цвета');

    // Each currency on its own, never summed together.
    const owed = screen.getByRole('list', { name: 'Ученици должат' });
    expect(within(owed).getAllByRole('listitem')).toHaveLength(2);
    expect(screen.getByText('2 ученици')).toBeInTheDocument();
    const low = screen.getByRole('list', { name: 'Пакети при крај' });
    expect(within(low).getByRole('link', { name: 'Борис' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/students/s2`);
    expect(low).toHaveTextContent('уште 1 час');

    const income = screen.getByRole('table', { name: 'Примени уплати' });
    expect(within(income).getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Месец', 'EUR', 'MKD']);
    expect(within(income).getAllByRole('row')).toHaveLength(7);
  });

  it('says so when there is nothing ahead and nothing owed', async () => {
    fakeApi({ me: teacher() });
    await renderRoute(`/app/w/${WORKSPACE_ID}`);

    expect(await screen.findByText('Нема часови во наредните 7 дена.')).toBeInTheDocument();
    expect(screen.getByText('Никој не ви должи.')).toBeInTheDocument();
    expect(screen.getByText('Нема евидентирани уплати во последните 6 месеци.')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Пакети при крај' })).not.toBeInTheDocument();
  });
});
