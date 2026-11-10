import { JSXGraph } from 'jsxgraph';
import 'jsxgraph/distrib/jsxgraph.css';
import { useEffect, useRef } from 'react';
import type { Construction, GeometryTool, View } from './construction.ts';
import { renderConstruction, type BoardLike } from './render.ts';

export type BoardTarget = { pointId: string } | { x: number; y: number };

type Element = { elType?: string; X?: () => number; Y?: () => number; on?: (event: string, handler: () => void) => void };
type Board = BoardLike & {
  on: (event: string, handler: (e: unknown) => void) => void;
  getUsrCoordsOfMouse: (e: unknown) => [number, number];
  getAllObjectsUnderMouse: (e: unknown) => Element[];
  getBoundingBox: () => [number, number, number, number];
};

/**
 * The JSXGraph board (lazy-loaded with its page), drawn from the construction: the construction is the source of truth,
 * the board is rebuilt when it changes. A click reports the point under the mouse or the spot's coordinates; in the
 * move tool free points can be dragged; panning and zooming report the visible area without changing the construction.
 */
export default function GeometryCanvas({
  construction,
  tool,
  selected,
  initialView,
  label,
  onTarget,
  onPointMoved,
  onView,
}: {
  construction: Construction;
  tool: GeometryTool;
  selected: ReadonlySet<string>;
  initialView: View['boundingBox'];
  label: string;
  onTarget: (target: BoardTarget) => void;
  onPointMoved: (id: string, x: number, y: number) => void;
  onView: (box: View['boundingBox']) => void;
}) {
  const container = useRef<HTMLDivElement>(null);
  // The visible area survives rebuilds (panning is not a change of the construction).
  const view = useRef<View['boundingBox']>(initialView);
  const handlers = useRef({ onTarget, onPointMoved, onView });
  useEffect(() => {
    handlers.current = { onTarget, onPointMoved, onView };
  });

  useEffect(() => {
    if (!container.current) return undefined;
    const board = JSXGraph.initBoard(container.current, {
      boundingbox: view.current,
      axis: construction.view.axis,
      grid: construction.view.grid,
      keepAspectRatio: true,
      showCopyright: false,
      showNavigation: true,
      pan: { enabled: true, needShift: tool !== 'move' },
      zoom: { wheel: true, needShift: false },
    }) as unknown as Board;
    const elements = renderConstruction(board, construction, { draggable: tool === 'move', selected });
    const idOf = new Map<unknown, string>([...elements].map(([id, element]) => [element, id]));

    board.on('down', (event) => {
      if (tool === 'move') return;
      const point = board.getAllObjectsUnderMouse(event).find((element) => element.elType === 'point' && idOf.has(element));
      if (point) {
        handlers.current.onTarget({ pointId: idOf.get(point)! });
      } else {
        const [x, y] = board.getUsrCoordsOfMouse(event);
        handlers.current.onTarget({ x, y });
      }
    });
    for (const object of construction.objects) {
      const element = elements.get(object.id) as Element | undefined;
      if (object.type === 'point' && element?.on) {
        element.on('up', () => handlers.current.onPointMoved(object.id, element.X!(), element.Y!()));
      }
    }
    board.on('boundingbox', () => {
      view.current = board.getBoundingBox();
      handlers.current.onView(view.current);
    });
    return () => JSXGraph.freeBoard(board as never);
    // A changed construction (objects, axes, grid), tool or selection redraws the board.
  }, [construction, tool, selected]);

  // JSXGraph sets its own roles on the element it draws in: the board's name lives on the wrapper.
  return (
    <div role="application" aria-label={label} className="h-full w-full">
      <div ref={container} className="jxgbox h-full w-full" style={{ touchAction: 'none' }} />
    </div>
  );
}
