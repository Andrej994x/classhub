/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';

// The ASP.NET Core host (API + Razor public pages) listens on 5080 in development.
const apiTarget = 'http://localhost:5080';

// Production build: written into the API's wwwroot/dist and served by ASP.NET Core from the same origin
// (SPA fallback for /app, /student, /admin and the auth pages; assets under /dist/).
const outDir = fileURLToPath(new URL('../src/ClassHub.Api/wwwroot/dist', import.meta.url));

export default defineConfig(({ command }) => ({
  base: command === 'build' ? '/dist/' : '/',
  plugins: [react(), tailwindcss()],
  build: {
    outDir,
    emptyOutDir: true,
    manifest: true,
    sourcemap: false,
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: false },
      // Server-rendered pages of the API (site preview; public sites from R1-P4).
      '/preview': { target: apiTarget, changeOrigin: false },
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
