import { MAX_TERM_LENGTH, parseTerm } from './expression.ts';

/**
 * The geometry board's construction (R3-P4, D-33): ClassHub's own versioned JSON format, independent of the drawing
 * library. Version 1 holds free points and the objects built on them (segments, lines, circles, triangles, angles) and
 * the view (visible area, axes, grid); version 2 (R3-P5) adds function graphs, each a term of x read by the safe
 * parser. Version 1 documents still open. The API checks the same rules.
 */
export const GEOMETRY_SCHEMA_VERSION = 2;
export const MAX_OBJECTS = 2000;
export const COORDINATE_LIMIT = 1_000_000;

export type PointObject = { id: string; type: 'point'; x: number; y: number };
export type TwoPointObject = { id: string; type: 'segment' | 'line'; points: [string, string] };
export type CircleObject = { id: string; type: 'circle'; center: string; through: string };
export type ThreePointObject = { id: string; type: 'triangle' | 'angle'; points: [string, string, string] };
export type FunctionObject = { id: string; type: 'function'; term: string };
export type GeometryObject = PointObject | TwoPointObject | CircleObject | ThreePointObject | FunctionObject;
export type GeometryTool = 'move' | 'point' | 'segment' | 'line' | 'circle' | 'triangle' | 'angle';

export type View = { boundingBox: [number, number, number, number]; axis: boolean; grid: boolean };
export type Construction = { format: 'classhub.geometry'; version: 2; view: View; objects: GeometryObject[] };

export const DEFAULT_VIEW: View = { boundingBox: [-8, 6, 8, -6], axis: true, grid: true };

export const emptyConstruction = (): Construction => ({ format: 'classhub.geometry', version: 2, view: { ...DEFAULT_VIEW }, objects: [] });

/** How many points each tool needs (after choosing them the object is built). */
export const POINTS_NEEDED: Record<Exclude<GeometryTool, 'move'>, number> = { point: 1, segment: 2, line: 2, circle: 2, triangle: 3, angle: 3 };

const NAME = /^[A-Za-z0-9_']{1,40}$/;
const finite = (n: unknown): n is number => typeof n === 'number' && Number.isFinite(n) && Math.abs(n) <= COORDINATE_LIMIT;

/** Whether this many more objects fit (the API keeps constructions to {@link MAX_OBJECTS}). */
export const hasRoom = (construction: Construction, adding: number) => construction.objects.length + adding <= MAX_OBJECTS;

/** The next free point name: A … Z, then A1 … Z1, A2 … */
export function nextPointName(construction: Construction): string {
  const used = new Set(construction.objects.map((o) => o.id));
  for (let round = 0; ; round++) {
    for (let letter = 0; letter < 26; letter++) {
      const name = String.fromCodePoint(65 + letter) + (round === 0 ? '' : String(round));
      if (!used.has(name)) return name;
    }
  }
}

/** A new id for a built object (segments s1, s2 …; lines l1 …; circles c1 …; triangles t1 …; angles a1 …). */
function nextObjectId(construction: Construction, type: Exclude<GeometryObject['type'], 'point' | 'function'>): string {
  const prefix = { segment: 's', line: 'l', circle: 'c', triangle: 't', angle: 'a' }[type];
  const used = new Set(construction.objects.map((o) => o.id));
  for (let n = 1; ; n++) if (!used.has(`${prefix}${n}`)) return `${prefix}${n}`;
}

const round = (n: number) => Math.round(n * 1000) / 1000;

/** Adds a free point (coordinates rounded to thousandths); returns the new construction and the point's id. */
export function addPoint(construction: Construction, x: number, y: number): [Construction, string] {
  const id = nextPointName(construction);
  return [{ ...construction, objects: [...construction.objects, { id, type: 'point', x: round(x), y: round(y) }] }, id];
}

/** Builds an object of the tool from chosen points (distinct, existing). Null when the choice does not make one. */
export function addObject(construction: Construction, tool: Exclude<GeometryTool, 'move' | 'point'>, points: string[]): Construction | null {
  const ids = new Set(construction.objects.filter((o) => o.type === 'point').map((o) => o.id));
  if (points.length !== POINTS_NEEDED[tool] || new Set(points).size !== points.length || !points.every((p) => ids.has(p))) return null;
  const id = nextObjectId(construction, tool);
  const object: GeometryObject =
    tool === 'circle'
      ? { id, type: 'circle', center: points[0]!, through: points[1]! }
      : tool === 'segment' || tool === 'line'
        ? { id, type: tool, points: [points[0]!, points[1]!] }
        : { id, type: tool, points: [points[0]!, points[1]!, points[2]!] };
  return { ...construction, objects: [...construction.objects, object] };
}

const FUNCTION_NAMES = ['f', 'g', 'h', 'p', 'q', 'r'];

/** The next free function name: f, g, h, p, q, r, then f1, f2 … */
export function nextFunctionName(construction: Construction): string {
  const used = new Set(construction.objects.map((o) => o.id));
  const free = FUNCTION_NAMES.find((name) => !used.has(name));
  if (free) return free;
  for (let n = 1; ; n++) if (!used.has(`f${n}`)) return `f${n}`;
}

/** Adds a function graph if the term is a valid term of x (and there is room); null otherwise. */
export function addFunction(construction: Construction, term: string): Construction | null {
  const text = term.trim();
  if (!parseTerm(text).ok || !hasRoom(construction, 1)) return null;
  return { ...construction, objects: [...construction.objects, { id: nextFunctionName(construction), type: 'function', term: text }] };
}

/** Removes a function graph (nothing is built on functions). */
export function removeFunction(construction: Construction, id: string): Construction {
  return { ...construction, objects: construction.objects.filter((o) => !(o.id === id && o.type === 'function')) };
}

/** Moves a free point (dragging on the board). */
export function movePoint(construction: Construction, id: string, x: number, y: number): Construction {
  return { ...construction, objects: construction.objects.map((o) => (o.id === id && o.type === 'point' ? { ...o, x: round(x), y: round(y) } : o)) };
}

/** Several points moved at once (a dragged segment, circle or triangle moves its points together). */
export function movePoints(construction: Construction, moves: readonly { id: string; x: number; y: number }[]): Construction {
  return moves.reduce((current, move) => movePoint(current, move.id, move.x, move.y), construction);
}

/** The points an object is built on. */
export function dependencies(object: GeometryObject): string[] {
  return object.type === 'point' || object.type === 'function' ? [] : object.type === 'circle' ? [object.center, object.through] : [...object.points];
}

/**
 * A saved document back into a construction, defensively: objects that are malformed or refer to missing points are
 * dropped (with what was built on them), never drawn half.
 */
export function fromDocument(document: unknown): Construction {
  const value = (typeof document === 'object' && document !== null ? document : {}) as { view?: unknown; objects?: unknown };
  const view = (typeof value.view === 'object' && value.view !== null ? value.view : {}) as Partial<View>;
  const box = Array.isArray(view.boundingBox) && view.boundingBox.length === 4 && view.boundingBox.every(finite) ? (view.boundingBox as View['boundingBox']) : DEFAULT_VIEW.boundingBox;
  const objects: GeometryObject[] = [];
  const known = new Set<string>();
  for (const raw of Array.isArray(value.objects) ? value.objects.slice(0, MAX_OBJECTS) : []) {
    const object = parseObject(raw);
    if (!object || known.has(object.id) || !dependencies(object).every((d) => known.has(d))) continue;
    objects.push(object);
    known.add(object.id);
  }
  return { format: 'classhub.geometry', version: 2, view: { boundingBox: [...box], axis: view.axis !== false, grid: view.grid !== false }, objects };
}

/** Distinct point references of exactly this count, or null. */
const refs = (value: unknown, count: number) =>
  Array.isArray(value) && value.length === count && value.every((v) => typeof v === 'string') && new Set(value).size === count ? (value as string[]) : null;

function parseObject(raw: unknown): GeometryObject | null {
  if (typeof raw !== 'object' || raw === null) return null;
  const o = raw as Record<string, unknown>;
  if (typeof o.id !== 'string' || !NAME.test(o.id)) return null;
  switch (o.type) {
    case 'point':
      return finite(o.x) && finite(o.y) ? { id: o.id, type: 'point', x: o.x, y: o.y } : null;
    case 'segment':
    case 'line': {
      const points = refs(o.points, 2);
      return points ? { id: o.id, type: o.type, points: [points[0]!, points[1]!] } : null;
    }
    case 'circle':
      return typeof o.center === 'string' && typeof o.through === 'string' && o.center !== o.through ? { id: o.id, type: 'circle', center: o.center, through: o.through } : null;
    case 'triangle':
    case 'angle': {
      const points = refs(o.points, 3);
      return points ? { id: o.id, type: o.type, points: [points[0]!, points[1]!, points[2]!] } : null;
    }
    case 'function':
      return typeof o.term === 'string' && o.term.length <= MAX_TERM_LENGTH && parseTerm(o.term).ok ? { id: o.id, type: 'function', term: o.term } : null;
    default:
      return null;
  }
}
