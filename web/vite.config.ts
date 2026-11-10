/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { cpSync, createReadStream, existsSync, readdirSync, statSync } from 'node:fs';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig, type Plugin } from 'vite';

// The ASP.NET Core host (API + Razor public pages) listens on 5080 in development.
const apiTarget = 'http://localhost:5080';

// Production build: written into the API's wwwroot/dist and served by ASP.NET Core from the same origin
// (SPA fallback for /app, /student, /admin and the auth pages; assets under /dist/).
const outDir = fileURLToPath(new URL('../src/ClassHub.Api/wwwroot/dist', import.meta.url));

const file = (path: string) => fileURLToPath(new URL(path, import.meta.url));

/**
 * Build entries: the SPA (index.html), the stylesheet of the Razor public pages and one entry per React island
 * (src/islands/*.tsx). The manifest maps them to hashed files for the Razor tag helpers <vite-asset>/<vite-island>.
 */
const islands = Object.fromEntries(
  readdirSync(file('./src/islands'))
    .filter((name) => name.endsWith('.tsx') && name !== 'mount.tsx')
    .map((name) => [`island-${name.replace(/\.tsx$/, '')}`, file(`./src/islands/${name}`)]),
);

/**
 * Paths of the Razor public pages that the dev server forwards to ASP.NET Core: the landing page (/ and /en), sitemap,
 * robots, the built public assets and every slug-shaped top-level path except the SPA areas (/{slug}, /{slug}/photo,
 * /{slug}/tests/{testSlug}).
 */
const publicPages = String.raw`^/(?:$|\?|en(?:$|\?)|sitemap\.xml|robots\.txt|dist/|(?!(?:app|student|admin|login|register|verify-email|forgot-password|reset-password|dev)(?:[/?#]|$))[a-z0-9][a-z0-9-]{1,38}[a-z0-9](?:/photo|/tests/[a-z0-9][a-z0-9-]{1,58}[a-z0-9])?(?:[?#]|$))`;

/**
 * The drawing board's fonts (R3-P3), self-hosted: Excalidraw loads them at runtime from `EXCALIDRAW_ASSET_PATH`
 * (`<base>excalidraw-assets/fonts/...`) and would otherwise fall back to a CDN, which the CSP blocks anyway. The dev server
 * serves them from the package; the build copies them next to the SPA.
 */
const excalidrawFonts = file('./node_modules/@excalidraw/excalidraw/dist/prod/fonts');
const FONT_TYPES: Record<string, string> = { '.woff2': 'font/woff2', '.woff': 'font/woff', '.ttf': 'font/ttf' };
function excalidrawAssets(): Plugin {
  return {
    name: 'classhub-excalidraw-assets',
    configureServer(server) {
      server.middlewares.use('/excalidraw-assets/fonts', (req, res, next) => {
        const path = normalize(join(excalidrawFonts, decodeURIComponent((req.url ?? '').split('?')[0]!)));
        if (!path.startsWith(excalidrawFonts) || !existsSync(path) || !statSync(path).isFile()) return next();
        res.setHeader('Content-Type', FONT_TYPES[extname(path)] ?? 'application/octet-stream');
        createReadStream(path).pipe(res);
      });
    },
    writeBundle(options) {
      cpSync(excalidrawFonts, join(options.dir ?? outDir, 'excalidraw-assets', 'fonts'), { recursive: true });
    },
  };
}

export default defineConfig(({ command }) => ({
  base: command === 'build' ? '/dist/' : '/',
  plugins: [react(), tailwindcss(), excalidrawAssets()],
  build: {
    outDir,
    emptyOutDir: true,
    manifest: true,
    sourcemap: false,
    rolldownOptions: {
      input: { index: file('./index.html'), public: file('./src/public/public.css'), ...islands },
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: false },
      // Server-rendered pages of the API: site preview and the public pages (they use the built public assets,
      // so run `npx vite build --watch` alongside when working on them).
      '/preview': { target: apiTarget, changeOrigin: false },
      [publicPages]: { target: apiTarget, changeOrigin: false },
    },
  },
  test: {
    environment: 'jsdom',
    // Playwright specs live in e2e/ and run with Playwright, not Vitest.
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    restoreMocks: true,
  },
}));
