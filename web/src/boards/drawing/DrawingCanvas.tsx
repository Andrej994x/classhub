import { Excalidraw } from '@excalidraw/excalidraw';
import '@excalidraw/excalidraw/index.css';
import type { ExcalidrawImperativeAPI } from '@excalidraw/excalidraw/types';
import type { DrawingDocument } from './scene.ts';

/**
 * The Excalidraw canvas (lazy-loaded with its page). Images, embedded web pages, loading files and exporting are off
 * (D-42): the board saves into the Activity Library only.
 */
export default function DrawingCanvas({ initial, onReady }: { initial: DrawingDocument; onReady: (api: ExcalidrawImperativeAPI) => void }) {
  return (
    <Excalidraw
      initialData={{ elements: initial.elements as never, appState: initial.appState, scrollToContent: true }}
      excalidrawAPI={onReady}
      langCode="en"
      validateEmbeddable={() => false}
      UIOptions={{
        tools: { image: false },
        canvasActions: { loadScene: false, saveToActiveFile: false, export: false, saveAsImage: false },
      }}
    />
  );
}
