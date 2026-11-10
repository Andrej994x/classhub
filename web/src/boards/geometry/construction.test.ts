import { describe, expect, it } from 'vitest';
import { addFunction, addObject, addPoint, emptyConstruction, fromDocument, hasRoom, MAX_OBJECTS, movePoint, movePoints, nextPointName, removeFunction, type Construction } from './construction.ts';
import { boardElementType, renderConstruction } from './render.ts';

function triangle(): Construction {
  let c = emptyConstruction();
  [c] = addPoint(c, 0, 0);
  [c] = addPoint(c, 4.12345, 0);
  [c] = addPoint(c, 0, 3);
  return addObject(c, 'triangle', ['A', 'B', 'C'])!;
}

describe('geometry construction (format v1, D-33)', () => {
  it('names points A, B, C … and builds objects on them', () => {
    const c = triangle();
    expect(c.objects).toEqual([
      { id: 'A', type: 'point', x: 0, y: 0 },
      { id: 'B', type: 'point', x: 4.123, y: 0 },
      { id: 'C', type: 'point', x: 0, y: 3 },
      { id: 't1', type: 'triangle', points: ['A', 'B', 'C'] },
    ]);
    expect(nextPointName(c)).toBe('D');
    const more = addObject(addObject(addObject(c, 'segment', ['A', 'B'])!, 'circle', ['A', 'C'])!, 'angle', ['B', 'A', 'C'])!;
    expect(more.objects.slice(4)).toEqual([
      { id: 's1', type: 'segment', points: ['A', 'B'] },
      { id: 'c1', type: 'circle', center: 'A', through: 'C' },
      { id: 'a1', type: 'angle', points: ['B', 'A', 'C'] },
    ]);
  });

  it('knows how much still fits', () => {
    const full = { ...emptyConstruction(), objects: Array.from({ length: MAX_OBJECTS - 1 }, (_, i) => ({ id: `P${i}`, type: 'point' as const, x: 0, y: 0 })) };
    expect(hasRoom(full, 1)).toBe(true);
    expect(hasRoom(full, 2)).toBe(false);
  });

  it('builds nothing from repeated, missing or too few points', () => {
    const c = triangle();
    expect(addObject(c, 'segment', ['A', 'A'])).toBeNull();
    expect(addObject(c, 'line', ['A', 'Z'])).toBeNull();
    expect(addObject(c, 'triangle', ['A', 'B'])).toBeNull();
    expect(addObject(c, 'circle', ['A', 't1'])).toBeNull(); // a triangle is not a point
  });

  it('continues names after Z and moves free points', () => {
    let c = emptyConstruction();
    for (let i = 0; i < 27; i++) [c] = addPoint(c, i, 0);
    expect(c.objects.at(-1)!.id).toBe('A1');
    expect(movePoint(c, 'B', 2.5, -1).objects[1]).toEqual({ id: 'B', type: 'point', x: 2.5, y: -1 });
    const moved = movePoints(c, [{ id: 'A', x: 1, y: 1 }, { id: 'C', x: 3.33333, y: 1 }]);
    expect(moved.objects.slice(0, 3).map((o) => (o.type === 'point' ? [o.x, o.y] : null))).toEqual([[1, 1], [1, 0], [3.333, 1]]);
  });

  it('round-trips through JSON and opens damaged documents defensively', () => {
    const c = triangle();
    expect(fromDocument(JSON.parse(JSON.stringify(c)))).toEqual(c);
    const damaged = fromDocument({
      view: { boundingBox: [1, 2, 'x', 4], axis: false },
      objects: [
        { id: 'A', type: 'point', x: 1, y: 2 },
        { id: 'A', type: 'point', x: 5, y: 5 }, // duplicate name
        { id: 'P', type: 'point', x: 'far', y: 0 },
        { id: 's1', type: 'segment', points: ['A', 'Q'] }, // a missing point
        { id: 'f1', type: 'function', term: 'x^2' }, // a function graph (version 2)
        { id: 'g', type: 'function', term: 'alert(1)' }, // not a term of x
        { id: '<b>', type: 'point', x: 0, y: 0 },
        { id: 'B', type: 'point', x: 2, y: 2 },
        { id: 's2', type: 'segment', points: ['A', 'B'] },
      ],
    });
    expect(damaged.objects.map((o) => o.id)).toEqual(['A', 'f1', 'B', 's2']);
    expect(damaged.view).toEqual({ boundingBox: [-8, 6, 8, -6], axis: false, grid: true });
  });
});

describe('function graphs (format v2)', () => {
  it('adds valid terms as f, g, h … and removes them; invalid terms add nothing', () => {
    let c = emptyConstruction();
    c = addFunction(c, ' x^2 - 2 ')!;
    c = addFunction(c, '2sin(x)')!;
    expect(c.objects).toEqual([
      { id: 'f', type: 'function', term: 'x^2 - 2' },
      { id: 'g', type: 'function', term: '2sin(x)' },
    ]);
    expect(addFunction(c, 'alert(1)')).toBeNull();
    expect(addFunction(c, '')).toBeNull();
    expect(removeFunction(c, 'f').objects.map((o) => o.id)).toEqual(['g']);
    expect(addFunction(removeFunction(c, 'f'), 'x')!.objects.map((o) => o.id)).toEqual(['g', 'f']);
  });

  it('keeps a saved term up to 200 characters as written, spaces included, like the API', () => {
    const doc = (term: string) => ({ format: 'classhub.geometry', version: 2, objects: [{ id: 'f', type: 'function', term }] });
    expect(fromDocument(doc(' '.repeat(199) + 'x')).objects).toHaveLength(1);
    expect(fromDocument(doc(' '.repeat(200) + 'x')).objects).toHaveLength(0);
  });

  it('opens version 1 documents (points and shapes only) as version 2', () => {
    const v1 = { format: 'classhub.geometry', version: 1, objects: [{ id: 'A', type: 'point', x: 1, y: 1 }] };
    expect(fromDocument(v1)).toMatchObject({ version: 2, objects: [{ id: 'A', type: 'point', x: 1, y: 1 }] });
  });
});

describe('construction ↔ board mapping', () => {
  it('draws every object with its JSXGraph type and parents, points first', () => {
    const calls: { type: string; parents: unknown[] }[] = [];
    const board = { create: (type: string, parents: unknown[]) => (calls.push({ type, parents }), { type, parents }) };
    const c = addObject(addObject(triangle(), 'angle', ['B', 'A', 'C'])!, 'circle', ['A', 'B'])!;
    const elements = renderConstruction(board, c, { draggable: true, selected: new Set() });

    expect(calls.map((call) => call.type)).toEqual(['point', 'point', 'point', 'polygon', 'angle', 'circle']);
    expect(calls[0]!.parents).toEqual([0, 0]);
    expect(calls[3]!.parents).toEqual([elements.get('A'), elements.get('B'), elements.get('C')]);
    expect(calls[4]!.parents).toEqual([elements.get('B'), elements.get('A'), elements.get('C')]); // vertex in the middle
    expect(calls[5]!.parents).toEqual([elements.get('A'), elements.get('B')]);
    expect(boardElementType({ id: 'l1', type: 'line', points: ['A', 'B'] })).toBe('line');
  });

  it('plots functions through the safe evaluator, never a string for JSXGraph to evaluate', () => {
    const calls: { type: string; parents: unknown[]; attributes?: Record<string, unknown> }[] = [];
    const board = {
      create: (type: string, parents: unknown[], attributes?: Record<string, unknown>) => (calls.push({ type, parents, attributes }), {}),
      getBoundingBox: (): [number, number, number, number] => [-5, 4, 7, -4],
    };
    renderConstruction(board, addFunction(emptyConstruction(), 'x^2 - 1')!, { draggable: false, selected: new Set() });
    expect(calls).toHaveLength(1);
    // A parametric curve of plain functions: (t, f(t)) from the left to the right edge of the view.
    expect(calls[0]!.type).toBe('curve');
    const [x, y, from, to] = calls[0]!.parents as [(t: number) => number, (t: number) => number, () => number, () => number];
    expect(x(3)).toBe(3);
    expect(y(3)).toBe(8);
    expect([from(), to()]).toEqual([-5, 7]);
    expect(calls[0]!.parents.every((p) => typeof p === 'function')).toBe(true);
    expect(calls[0]!.attributes?.fixed).toBe(true); // a graph cannot be dragged away from its term
  });
});
