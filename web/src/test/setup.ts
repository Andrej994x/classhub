import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';
import { api } from '../api/client.ts';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  api.resetToken();
  window.localStorage.clear();
  window.sessionStorage.clear();
  window.history.replaceState(null, '', '/');
});
