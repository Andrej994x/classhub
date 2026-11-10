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
