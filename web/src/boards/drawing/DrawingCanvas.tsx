import { Excalidraw, getSceneVersion } from '@excalidraw/excalidraw';
import { useRef } from 'react';
import '@excalidraw/excalidraw/index.css';
import type { ExcalidrawImperativeAPI } from '@excalidraw/excalidraw/types';
import { pasteAllowed, withoutDisallowed, type DrawingDocument } from './scene.ts';

/**
 * The Excalidraw canvas (lazy-loaded with its page). Images, embedded web pages, loading files and exporting are off
 * (D-42): the board saves into the Activity Library only.
 */
export default function DrawingCanvas({
  initial,
  onReady,
  onBlocked,
  onSceneChange,
}: {
  initial: DrawingDocument;
  onReady: (api: ExcalidrawImperativeAPI) => void;
  onBlocked: () => void;
  /** A fingerprint of what would be saved (elements and view settings), reported when it changes. */
  onSceneChange: (fingerprint: string) => void;
}) {
  const api = useRef<ExcalidrawImperativeAPI | null>(null);
  const lastFingerprint = useRef<string | null>(null);
  return (
    <Excalidraw
      initialData={{ elements: initial.elements as never, appState: initial.appState, scrollToContent: true }}
      excalidrawAPI={(ready) => {
        api.current = ready;
        onReady(ready);
      }}
      // A paste from another board may carry images or files: refused before it is inserted.
      onPaste={(data) => {
        if (pasteAllowed(data as never)) return true;
        onBlocked();
        return false;
      }}
      // Anything else that brings such elements in (library items, converted diagrams): removed at once.
      onChange={(elements, appState) => {
        const allowed = withoutDisallowed(elements as never);
        if (allowed && api.current) {
          api.current.updateScene({ elements: allowed as never });
          onBlocked();
          return;
        }
        // Scrolling, zooming and selecting change nothing that is saved; drawing, moving and deleting do.
        const fingerprint = `${getSceneVersion(elements)}|${appState.viewBackgroundColor}|${appState.gridModeEnabled}`;
        if (fingerprint !== lastFingerprint.current) {
          lastFingerprint.current = fingerprint;
          onSceneChange(fingerprint);
        }
      }}
      langCode="en"
      validateEmbeddable={() => false}
      UIOptions={{
        tools: { image: false },
        canvasActions: { loadScene: false, saveToActiveFile: false, export: false, saveAsImage: false },
      }}
    />
  );
}
