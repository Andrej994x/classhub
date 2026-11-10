import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { fakeApi, json, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

// Excalidraw itself does not run in jsdom: a stand-in board with the same contract (initial scene in, API out).
const scene = { elements: [] as unknown[] };
function MockCanvas({ initial, onReady }: { initial: { elements: unknown[] }; onReady: (api: unknown) => void }) {
  // The scene starts from the initial data once, like Excalidraw.
  useEffect(() => {
    scene.elements = initial.elements;
  }, [initial]);
  useEffect(() => {
    onReady({ getSceneElements: () => scene.elements, getAppState: () => ({ viewBackgroundColor: '#ffffff', gridModeEnabled: false }) });
  });
  return <p>{`board: ${initial.elements.length} elements`}</p>;
}
vi.mock('../../../boards/drawing/DrawingCanvas.tsx', () => ({ default: MockCanvas }));

const BASE = `/api/w/${WORKSPACE_ID}/activities`;
const rectangle = { id: 'r1', type: 'rectangle', x: 1, y: 2 };

describe('drawing board', () => {
  it('saves a new drawing into the library without images, then keeps changing the same activity', async () => {
    const user = userEvent.setup();
    let version = 1;
    const api = fakeApi({
      me: teacher(),
      extra: [
        (call: FakeCall) => {
          if (call.path === BASE && call.method === 'POST') return json(201, { id: 'a1', ...(call.body as object), version: 1, sizeBytes: 50, createdAtUtc: '', updatedAtUtc: '' });
          if (call.path === `${BASE}/a1` && call.method === 'GET') return json(200, { id: 'a1', title: 'Агли', subjectId: null, toolType: 'drawing-board', schemaVersion: 1, document: { elements: [rectangle] }, version, sizeBytes: 50, createdAtUtc: '', updatedAtUtc: '' });
          if (call.path === `${BASE}/a1` && call.method === 'PUT') return json(200, { id: 'a1', ...(call.body as object), version: ++version, sizeBytes: 50, createdAtUtc: '', updatedAtUtc: '' });
          return undefined;
        },
      ],
    });
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/toolbox/drawing-board`);

    expect(await screen.findByText('board: 0 elements')).toBeInTheDocument();
    scene.elements = [rectangle, { id: 'i1', type: 'image', fileId: 'f1' }];
    const form = screen.getByRole('form', { name: 'Зачувај ја активноста' });
    await user.type(within(form).getByLabelText('Наслов'), 'Агли');
    await user.click(within(form).getByRole('button', { name: 'Зачувај во библиотеката' }));

    await expect.poll(() => api.to(BASE, 'POST').length).toBe(1);
    expect(api.to(BASE, 'POST')[0]!.body).toEqual({
      title: 'Агли',
      subjectId: null,
      toolType: 'drawing-board',
      schemaVersion: 1,
      document: { elements: [rectangle], appState: { viewBackgroundColor: '#ffffff', gridModeEnabled: false } },
      version: null,
    });
    // The page is now that activity, on the same board (no reload): the saved message stays and later saves change it.
    await expect.poll(() => router.state.location.search).toBe('?activity=a1');
    expect(await screen.findByText('Зачувано во Библиотеката на активности.')).toBeInTheDocument();
    expect(screen.getByText('board: 0 elements')).toBeInTheDocument();
    expect(api.to(`${BASE}/a1`, 'GET')).toHaveLength(0);
    await user.click(screen.getByRole('button', { name: 'Зачувај во библиотеката' }));
    await expect.poll(() => api.to(`${BASE}/a1`, 'PUT').length).toBe(1);
    expect(api.to(`${BASE}/a1`, 'PUT')[0]!.body).toMatchObject({ title: 'Агли', version: 1, document: { elements: [rectangle] } });
  });

  it('reopens a saved drawing from the library and saves it with the loaded version', async () => {
    const user = userEvent.setup();
    const api = fakeApi({
      me: teacher(),
      extra: [
        (call: FakeCall) => {
          if (call.path === `${BASE}/a1` && call.method === 'GET') return json(200, { id: 'a1', title: 'Агли', subjectId: null, toolType: 'drawing-board', schemaVersion: 1, document: { elements: [rectangle] }, version: 4, sizeBytes: 50, createdAtUtc: '', updatedAtUtc: '' });
          if (call.path === `${BASE}/a1` && call.method === 'PUT') return json(200, { id: 'a1', ...(call.body as object), version: 5, sizeBytes: 50, createdAtUtc: '', updatedAtUtc: '' });
          return undefined;
        },
      ],
    });
    await renderRoute(`/app/w/${WORKSPACE_ID}/toolbox/drawing-board?activity=a1`);

    expect(await screen.findByText('board: 1 elements')).toBeInTheDocument();
    expect(screen.getByLabelText('Наслов')).toHaveValue('Агли');
    await user.click(screen.getByRole('button', { name: 'Зачувај во библиотеката' }));
    await expect.poll(() => api.to(`${BASE}/a1`, 'PUT').length).toBe(1);
    expect(api.to(`${BASE}/a1`, 'PUT')[0]!.body).toMatchObject({ version: 4, document: { elements: [rectangle] } });
    expect(api.to(BASE, 'POST')).toHaveLength(0);
  });

  it('loads the saved activity after a refresh or Back, also when navigation state is left over', async () => {
    const api = fakeApi({
      me: teacher(),
      extra: [
        (call: FakeCall) =>
          call.path === `${BASE}/a1` && call.method === 'GET'
            ? json(200, { id: 'a1', title: 'Агли', subjectId: null, toolType: 'drawing-board', schemaVersion: 1, document: { elements: [rectangle] }, version: 2, sizeBytes: 50, createdAtUtc: '', updatedAtUtc: '' })
            : undefined,
      ],
    });
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/activities`);
    // A fresh page (as after a refresh) arriving with the state an earlier save left behind.
    await router.navigate(`/app/w/${WORKSPACE_ID}/toolbox/drawing-board?activity=a1`, { state: { createdId: 'a1' } });

    expect(await screen.findByText('board: 1 elements')).toBeInTheDocument();
    expect(screen.getByLabelText('Наслов')).toHaveValue('Агли');
    expect(api.to(`${BASE}/a1`, 'GET').length).toBeGreaterThan(0);
  });

  it('reopens with the latest version, not a cached older one', async () => {
    const user = userEvent.setup();
    let current = { title: 'Агли', version: 4 };
    const api = fakeApi({
      me: teacher(),
      extra: [
        (call: FakeCall) => {
          if (call.path === `${BASE}/a1` && call.method === 'GET') return json(200, { id: 'a1', ...current, subjectId: null, toolType: 'drawing-board', schemaVersion: 1, document: { elements: [rectangle] }, sizeBytes: 50, createdAtUtc: '', updatedAtUtc: '' });
          if (call.path === `${BASE}/a1` && call.method === 'PUT') return json(200, { id: 'a1', ...(call.body as object), version: current.version + 1, sizeBytes: 50, createdAtUtc: '', updatedAtUtc: '' });
          return undefined;
        },
      ],
    });
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}/toolbox/drawing-board?activity=a1`);
    expect(await screen.findByLabelText('Наслов')).toHaveValue('Агли');

    // Away to the library; meanwhile the activity is renamed elsewhere (version 5); then opened again.
    await router.navigate(`/app/w/${WORKSPACE_ID}/activities`);
    expect(await screen.findByRole('heading', { level: 1, name: 'Библиотека на активности' })).toBeInTheDocument();
    current = { title: 'Агли — повторување', version: 5 };
    await router.navigate(`/app/w/${WORKSPACE_ID}/toolbox/drawing-board?activity=a1`);

    expect(await screen.findByDisplayValue('Агли — повторување')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Зачувај во библиотеката' }));
    await expect.poll(() => api.to(`${BASE}/a1`, 'PUT').length).toBe(1);
    expect(api.to(`${BASE}/a1`, 'PUT')[0]!.body).toMatchObject({ title: 'Агли — повторување', version: 5 });
  });
});
