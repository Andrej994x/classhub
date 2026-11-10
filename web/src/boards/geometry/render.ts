import type { Construction, GeometryObject } from './construction.ts';
import { evaluate, parseTerm } from './expression.ts';

/** The part of a JSXGraph board the mapping uses (a fake one in tests). */
export type BoardLike = {
  create: (type: string, parents: unknown[], attributes?: Record<string, unknown>) => unknown;
  /** The visible area [left, top, right, bottom] (function graphs are drawn across it). */
  getBoundingBox?: () => [number, number, number, number];
};

/** What a construction object becomes on the board: the JSXGraph element type and its parents. */
export function boardElementType(object: GeometryObject): string {
  switch (object.type) {
    case 'triangle':
      return 'polygon';
    case 'function':
      return 'curve';
    default:
      return object.type; // point, segment, line, circle, angle (A, vertex B, C)
  }
}

/**
 * Draws the construction (objects in order: points before what is built on them). Returns the board element of
 * every object by its id. Free points can be dragged in the move tool; the selected points are highlighted.
 */
export function renderConstruction(board: BoardLike, construction: Construction, options: { draggable: boolean; selected: ReadonlySet<string> }): Map<string, unknown> {
  const elements = new Map<string, unknown>();
  const ref = (id: string) => elements.get(id);
  for (const object of construction.objects) {
    let element: unknown;
    switch (object.type) {
      case 'point':
        element = board.create('point', [object.x, object.y], {
          name: object.id,
          size: 3,
          fixed: !options.draggable,
          strokeColor: options.selected.has(object.id) ? '#d97706' : '#1d4ed8',
          fillColor: options.selected.has(object.id) ? '#fbbf24' : '#3b82f6',
        });
        break;
      case 'segment':
      case 'line':
        element = board.create(object.type, object.points.map(ref), { strokeColor: '#334155', strokeWidth: 2 });
        break;
      case 'circle':
        element = board.create('circle', [ref(object.center), ref(object.through)], { strokeColor: '#0f766e', strokeWidth: 2 });
        break;
      case 'triangle':
        // The inside can be grabbed too: dragging a triangle moves its three points.
        element = board.create('polygon', object.points.map(ref), { fillColor: '#93c5fd', fillOpacity: 0.25, hasInnerPoints: true, borders: { strokeColor: '#334155', strokeWidth: 2 } });
        break;
      case 'angle':
        element = board.create('angle', object.points.map(ref), { name: '', radius: 1, fillColor: '#fcd34d', fillOpacity: 0.4 });
        break;
      case 'function': {
        // A plain curve (t, f(t)) across the visible x-range, f read by the safe evaluator. Never JSXGraph's
        // functiongraph: it routes the x-coordinate through JSXGraph's own expression language.
        const parsed = parseTerm(object.term);
        if (!parsed.ok) continue;
        const tree = parsed.tree;
        const edge = (index: 0 | 2) => () => (board.getBoundingBox?.() ?? [-10, 10, 10, -10])[index];
        element = board.create('curve', [(t: number) => t, (t: number) => evaluate(tree, t), edge(0), edge(2)], {
          // The graph is its term: it cannot be dragged away from it (only points move).
          fixed: true,
          name: `${object.id}(x)`,
          withLabel: true,
          strokeColor: '#7c3aed',
          strokeWidth: 2,
        });
        break;
      }
    }
    elements.set(object.id, element);
  }
  return elements;
}
