import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect } from 'react';
import { describe, expect, it, vi } from 'vitest';
import type { BoardTarget, PointMove } from '../../../boards/geometry/GeometryCanvas.tsx';
import { fakeApi, json, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

// JSXGraph does not run in jsdom: a stand-in board that reports clicks like the real one.
const board = { click: (_target: BoardTarget) => {}, drag: (_moves: PointMove[]) => {} };
function MockCanvas({ construction, onTarget, onPointsMoved }: { construction: { objects: unknown[] }; onTarget: (target: BoardTarget) => void; onPointsMoved: (moves: PointMove[]) => void }) {
  useEffect(() => {
    board.click = (target) => act(() => onTarget(target));
    board.drag = (moves) => act(() => onPointsMoved(moves));
  });
  return <p>{`board: ${construction.objects.length} objects`}</p>;
}
vi.mock('../../../boards/geometry/GeometryCanvas.tsx', () => ({ default: MockCanvas }));

const BASE = `/api/w/${WORKSPACE_ID}/activities`;

describe('geometry board', () => {
  it('builds a triangle from three clicks, undoes and saves the construction', async () => {
    const user = userEvent.setup();
    const api = fakeApi({
      me: teacher(),
      extra: [(call: FakeCall) => (call.path === BASE && call.method === 'POST' ? json(201, { id: 'g1', ...(call.body as object), version: 1, sizeBytes: 80, createdAtUtc: '', updatedAtUtc: '' }) : undefined)],
    });
    await renderRoute(`/app/w/${WORKSPACE_ID}/toolbox/geometry-board`);
    expect(await screen.findByText('board: 0 objects')).toBeInTheDocument();

    const tools = screen.getByRole('toolbar', { name: 'Алатки за конструкција' });
    await user.click(within(tools).getByRole('button', { name: 'Триаголник' }));
    expect(within(tools).getByRole('button', { name: 'Триаголник' })).toHaveAttribute('aria-pressed', 'true');
    board.click({ x: 0, y: 0 });
    board.click({ x: 4, y: 0 });
    expect(screen.getByText('Кликнете точки или празни места (2 од 3). За агол: прва точка, темето, па другата точка.')).toBeInTheDocument();
    board.click({ pointId: 'A' }); // the first point again: still two distinct points
    board.click({ x: 0, y: 3 });
    expect(await screen.findByText('board: 4 objects')).toBeInTheDocument();

    // One click is one step: undo removes the last click (point C and the triangle it completed); Ctrl+Z the point before.
    await user.click(within(tools).getByRole('button', { name: 'Врати' }));
    expect(await screen.findByText('board: 2 objects')).toBeInTheDocument();
    await user.keyboard('{Control>}z{/Control}');
    expect(await screen.findByText('board: 1 objects')).toBeInTheDocument();

    // Build it again (on the remaining point A) and save: points and the triangle in ClassHub's format.
    board.click({ x: 4, y: 0 });
    board.click({ x: 0, y: 3 });
    board.click({ pointId: 'A' });
    expect(await screen.findByText('board: 4 objects')).toBeInTheDocument();

    // Dragging the triangle moves its three points: all of them reach the construction, as one undo step.
    await user.click(within(tools).getByRole('button', { name: 'Помести' }));
    board.drag([{ id: 'A', x: 1, y: 1 }, { id: 'B', x: 5, y: 1 }, { id: 'C', x: 1, y: 4 }]);
    board.drag([{ id: 'A', x: 2, y: 2 }, { id: 'B', x: 6, y: 2 }, { id: 'C', x: 2, y: 5 }]);
    await user.click(within(tools).getByRole('button', { name: 'Врати' }));

    await user.type(screen.getByLabelText('Наслов'), 'Правоаголен триаголник');
    await user.click(screen.getByRole('button', { name: 'Зачувај во библиотеката' }));
    await expect.poll(() => api.to(BASE, 'POST').length).toBe(1);
    const body = api.to(BASE, 'POST')[0]!.body as { toolType: string; schemaVersion: number; document: { format: string; objects: unknown[]; view: unknown } };
    expect(body.toolType).toBe('geometry-board');
    expect(body.schemaVersion).toBe(1);
    expect(body.document.format).toBe('classhub.geometry');
    expect(body.document.objects).toEqual([
      { id: 'A', type: 'point', x: 1, y: 1 },
      { id: 'B', type: 'point', x: 5, y: 1 },
      { id: 'C', type: 'point', x: 1, y: 4 },
      { id: 't1', type: 'triangle', points: ['B', 'C', 'A'] },
    ]);
    expect(body.document.view).toEqual({ boundingBox: [-8, 6, 8, -6], axis: true, grid: true });
    expect(await screen.findByText('Зачувано во Библиотеката на активности.')).toBeInTheDocument();
  });
});
