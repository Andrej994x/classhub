declare global {
  interface Window {
    EXCALIDRAW_ASSET_PATH?: string | string[];
  }
}

/** Excalidraw loads its fonts from ClassHub itself (never a CDN): `<base>excalidraw-assets/fonts/...` (see vite.config.ts). */
export function selfHostExcalidrawAssets() {
  window.EXCALIDRAW_ASSET_PATH = `${import.meta.env.BASE_URL}excalidraw-assets/`;
}
