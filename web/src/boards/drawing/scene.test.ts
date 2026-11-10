import { describe, expect, it } from 'vitest';
import { fromDocument, toDocument } from './scene.ts';

const rectangle = { id: 'r1', type: 'rectangle', x: 10, y: 20, width: 100, height: 50, isDeleted: false };
const text = { id: 't1', type: 'text', text: 'Агол α', x: 0, y: 0 };

describe('drawing scene (format v1)', () => {
  it('keeps live shapes and the view settings, and round-trips unchanged', () => {
    const document = toDocument([rectangle, text], { viewBackgroundColor: '#fffce8', gridModeEnabled: true, zoom: { value: 2 }, openMenu: 'canvas' });
    expect(document).toEqual({ elements: [rectangle, text], appState: { viewBackgroundColor: '#fffce8', gridModeEnabled: true } });
    // Saved → reopened → saved again: the same document.
    const reopened = fromDocument(JSON.parse(JSON.stringify(document)));
    expect(toDocument(reopened.elements, reopened.appState)).toEqual(document);
  });

  it('never saves images, embedded pages or deleted elements (D-42)', () => {
    const document = toDocument(
      [rectangle, { id: 'i1', type: 'image', fileId: 'f1' }, { id: 'e1', type: 'embeddable', link: 'https://example.com' }, { id: 'd1', type: 'ellipse', isDeleted: true }],
      {},
    );
    expect(document.elements.map((e) => e.id)).toEqual(['r1']);
    expect(document.appState).toEqual({ viewBackgroundColor: '#ffffff', gridModeEnabled: false });
  });

  it('opens unknown or damaged content as an empty or partial drawing instead of failing', () => {
    expect(fromDocument(null)).toEqual({ elements: [], appState: { viewBackgroundColor: '#ffffff', gridModeEnabled: false } });
    expect(fromDocument({ elements: 'x', appState: [] }).elements).toEqual([]);
    expect(fromDocument({ elements: [rectangle, { type: 'iframe' }, 7, null] }).elements).toEqual([rectangle]);
  });
});
