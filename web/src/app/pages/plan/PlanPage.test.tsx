import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, teacher, WORKSPACE_ID } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const plan = (code: 'free' | 'pro', publishedUsed: number) => ({
  code,
  nameMk: code === 'free' ? 'Бесплатен' : 'Про',
  nameEn: code === 'free' ? 'Free' : 'Pro',
  paidUntilUtc: code === 'pro' ? '2026-12-05T10:00:00Z' : null,
  limits: [
    { key: 'active-students', limit: code === 'free' ? 10 : 150, used: null },
    { key: 'published-tests', limit: code === 'free' ? 3 : 50, used: publishedUsed },
    { key: 'activities', limit: 10, used: null },
    { key: 'storage-mb', limit: 500, used: null },
    { key: 'analytics-level', limit: code === 'free' ? 1 : 2, used: null },
  ],
});

function api(body: ReturnType<typeof plan>) {
  return fakeApi({ me: teacher(), extra: [(call) => (call.path === `/api/w/${WORKSPACE_ID}/plan` ? json(200, body) : undefined)] });
}

describe('plan page', () => {
  it('shows the Free limits, the usage and how to move to Pro', async () => {
    api(plan('free', 3));
    await renderRoute(`/app/w/${WORKSPACE_ID}/plan`);

    expect(await screen.findByText('План: Бесплатен')).toBeInTheDocument();
    const limits = screen.getByRole('list', { name: 'Лимити' });
    const items = within(limits).getAllByRole('listitem').map((i) => i.textContent);
    expect(items).toEqual(['Активни ученицидо 10', 'Објавени тестови3 од 3', 'Активностидо 10', 'Простор за материјалидо 500 MB', 'АналитикаОсновна']);
    expect(screen.getByText('Ви треба повеќе?')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'План' })).toHaveAttribute('href', `/app/w/${WORKSPACE_ID}/plan`);
  });

  it('shows the paid period on Pro without the upgrade note', async () => {
    api(plan('pro', 4));
    await renderRoute(`/app/w/${WORKSPACE_ID}/plan`);

    expect(await screen.findByText('План: Про')).toBeInTheDocument();
    expect(screen.getByText(/Платен до/)).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Лимити' })).toHaveTextContent('4 од 50');
    expect(screen.queryByText('Ви треба повеќе?')).not.toBeInTheDocument();
  });
});
