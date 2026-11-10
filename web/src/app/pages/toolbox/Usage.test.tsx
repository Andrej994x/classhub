import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { fakeApi, json, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

vi.mock('../../../boards/drawing/DrawingCanvas.tsx', () => ({ default: () => <p>board</p> }));

const ADMIN = teacher({ email: 'admin@example.com', platformRoles: ['PlatformAdmin'] });
const MFA = { enabled: true, sessionVerified: true, required: true, recoveryCodesLeft: 10 };
const geogebra = {
  id: 'g',
  code: 'geogebra',
  nameMk: 'GeoGebra',
  nameEn: 'GeoGebra',
  descriptionMk: '',
  descriptionEn: '',
  kind: 'External',
  category: 'Geometry',
  url: 'https://www.geogebra.org/classic',
  builtInKey: null,
  subjectIds: [],
  isFeatured: true,
  licenseNoteMk: null,
  licenseNoteEn: null,
};

describe('usage analytics (R3-P6)', () => {
  it('counts an opened tool and shows the workspace its own use', async () => {
    const user = userEvent.setup();
    const api = fakeApi({
      me: teacher(),
      extra: [
        (call: FakeCall) => {
          if (call.path === `/api/w/${WORKSPACE_ID}/toolbox`) return json(200, { tools: [geogebra], categories: [] });
          if (call.path === `/api/w/${WORKSPACE_ID}/usage`) {
            return json(200, { days: 30, from: '2026-10-12', to: '2026-11-10', tools: [{ code: 'geogebra', opens: 3 }], boards: [{ board: 'drawing-board', created: 2, reused: 5 }] });
          }
          if (call.path === `/api/w/${WORKSPACE_ID}/toolbox/geogebra/open`) return new Response(null, { status: 204 });
          return undefined;
        },
      ],
    });
    await renderRoute(`/app/w/${WORKSPACE_ID}/toolbox`);

    const usage = await screen.findByRole('region', { name: 'Вашата употреба во последните 30 дена' });
    expect(usage).toHaveTextContent('GeoGebraотворена 3 пати');
    expect(usage).toHaveTextContent('Табла за цртање2 нови, 5 повторно користени');

    // The link opens the official site in a new tab; the page counts the opening (jsdom does not navigate).
    const link = screen.getByRole('link', { name: /Отвори GeoGebra/ });
    link.addEventListener('click', (event) => event.preventDefault());
    await user.click(link);
    await expect.poll(() => api.to(`/api/w/${WORKSPACE_ID}/toolbox/geogebra/open`, 'POST').length).toBe(1);
  });

  it('counts a saved activity opened again on its board once, and a new drawing not at all', async () => {
    const api = fakeApi({
      me: teacher(),
      extra: [
        (call: FakeCall) => {
          if (call.path === `/api/w/${WORKSPACE_ID}/activities/a1` && call.method === 'GET') {
            return json(200, { id: 'a1', title: 'Агли', subjectId: null, toolType: 'drawing-board', schemaVersion: 1, document: { elements: [] }, version: 1, sizeBytes: 9, createdAtUtc: '', updatedAtUtc: '' });
          }
          if (call.path === `/api/w/${WORKSPACE_ID}/activities/a1/open`) return new Response(null, { status: 204 });
          return undefined;
        },
      ],
    });
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/toolbox/drawing-board`);
    expect(await screen.findByText('board')).toBeInTheDocument();
    expect(api.calls.filter((c) => c.path.endsWith('/open'))).toHaveLength(0);

    await router.navigate(`/app/w/${WORKSPACE_ID}/activities`);
    await router.navigate(`/app/w/${WORKSPACE_ID}/toolbox/drawing-board?activity=a1`);
    expect(await screen.findByDisplayValue('Агли')).toBeInTheDocument();
    await expect.poll(() => api.to(`/api/w/${WORKSPACE_ID}/activities/a1/open`, 'POST').length).toBe(1);
  });

  it('shows staff the platform totals per tool, board and day', async () => {
    const user = userEvent.setup();
    const api = fakeApi({
      me: ADMIN,
      extra: [
        (call: FakeCall) => {
          if (call.path === '/api/auth/mfa') return json(200, MFA);
          if (call.path === '/api/admin/usage') {
            const days = Number(new URLSearchParams(call.search).get('days'));
            return json(200, {
              days,
              from: '2026-11-04',
              to: '2026-11-10',
              workspaces: 4,
              tools: [{ code: 'geogebra', opens: 12 }],
              boards: [{ board: 'geometry-board', created: 3, reused: 7 }],
              daily: [
                { day: '2026-11-09', toolOpens: 0, activitiesCreated: 0, activitiesReused: 0 },
                { day: '2026-11-10', toolOpens: 12, activitiesCreated: 3, activitiesReused: 7 },
              ],
            });
          }
          return undefined;
        },
      ],
    });
    await renderRoute('/admin/usage');

    expect(await screen.findByText('4 работни простори ги користеле алатките')).toBeInTheDocument();
    expect(within(screen.getByRole('table', { name: 'Отворени алатки' })).getByRole('row', { name: /geogebra 12/ })).toBeInTheDocument();
    expect(within(screen.getByRole('table', { name: 'Активности' })).getByRole('row', { name: /Геометриска табла 3 7/ })).toBeInTheDocument();
    expect(within(screen.getByRole('table', { name: 'По ден' })).getAllByRole('row')).toHaveLength(2); // header + the one day with use
    await user.click(screen.getByText('7 дена'));
    await expect.poll(() => api.calls.some((c) => c.path === '/api/admin/usage' && c.search.includes('days=7'))).toBe(true);
  });
});
