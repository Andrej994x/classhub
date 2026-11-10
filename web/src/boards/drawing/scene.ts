/**
 * The drawing board's document, format version 1 (R3-P3): the Excalidraw scene's elements and the few view settings
 * worth keeping. Never images, embedded web pages or files (D-42): they are removed before saving, and the API refuses
 * them too.
 */
export const DRAWING_SCHEMA_VERSION = 1;

/** The element types a drawing may contain (the API checks the same list). */
export const DRAWING_ELEMENT_TYPES = ['rectangle', 'diamond', 'ellipse', 'arrow', 'line', 'freedraw', 'text', 'frame'] as const;

type SceneElement = { type: string; isDeleted?: boolean } & Record<string, unknown>;
type SceneAppState = { viewBackgroundColor?: string; gridModeEnabled?: boolean } & Record<string, unknown>;

export type DrawingDocument = {
  elements: SceneElement[];
  appState: { viewBackgroundColor: string; gridModeEnabled: boolean };
};

const allowed = new Set<string>(DRAWING_ELEMENT_TYPES);

/** The scene as it is saved: live, allowed elements only, and the background and grid. */
export function toDocument(elements: readonly SceneElement[], appState: SceneAppState): DrawingDocument {
  return {
    elements: elements.filter((e) => !e.isDeleted && allowed.has(e.type)).map((e) => ({ ...e })),
    appState: { viewBackgroundColor: appState.viewBackgroundColor ?? '#ffffff', gridModeEnabled: appState.gridModeEnabled ?? false },
  };
}

/** A saved document back into the board's initial data (defensive: unknown content is dropped, not drawn). */
export function fromDocument(document: unknown): DrawingDocument {
  const value = (typeof document === 'object' && document !== null ? document : {}) as { elements?: unknown; appState?: unknown };
  const elements = Array.isArray(value.elements)
    ? value.elements.filter((e): e is SceneElement => typeof e === 'object' && e !== null && typeof (e as SceneElement).type === 'string' && allowed.has((e as SceneElement).type))
    : [];
  const appState = (typeof value.appState === 'object' && value.appState !== null ? value.appState : {}) as SceneAppState;
  return {
    elements,
    appState: {
      viewBackgroundColor: typeof appState.viewBackgroundColor === 'string' ? appState.viewBackgroundColor : '#ffffff',
      gridModeEnabled: appState.gridModeEnabled === true,
    },
  };
}

/**
 * The scene without what a drawing may not contain (images, embedded pages, iframes, anything unknown), or null when
 * it is fine. However such elements arrive (a paste from another board, a library item, a converted diagram), the
 * board removes them at once instead of letting them vanish silently on save.
 */
export function withoutDisallowed<T extends SceneElement>(elements: readonly T[]): T[] | null {
  return elements.some((e) => !e.isDeleted && !allowed.has(e.type)) ? elements.filter((e) => e.isDeleted || allowed.has(e.type)) : null;
}

/** Whether a paste may go in: structured content only with allowed elements and no files (images). */
export function pasteAllowed(data: { elements?: readonly SceneElement[] | null; files?: Record<string, unknown> | null }): boolean {
  if (data.files && Object.keys(data.files).length > 0) return false;
  return !data.elements || data.elements.every((e) => allowed.has(e.type));
}
