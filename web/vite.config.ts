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
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    restoreMocks: true,
  },
}));
