import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { InstallApp } from '../app/components/InstallApp.tsx';
import { i18n } from '../i18n/index.ts';
import { fakeApi, teacher } from '../test/fakeApi.ts';
import { renderRoute } from '../test/render.tsx';
import { captureInstallPrompt, manifestFor, PWA_SCOPES, registerServiceWorker, withAreaSlash } from './pwa.ts';

type Register = ServiceWorkerContainer['register'];

describe('service worker registration', () => {
  it('registers the worker only for the SPA areas, never for the public pages', () => {
    const register = vi.fn<Register>(() => Promise.resolve({} as ServiceWorkerRegistration));

    registerServiceWorker(true, '/dist/', { register });

    expect(register.mock.calls).toEqual([
      ['/dist/sw.js', { scope: '/app/' }],
      ['/dist/sw.js', { scope: '/student/' }],
    ]);
    expect(PWA_SCOPES).not.toContain('/');
  });

  it('does nothing in development or where service workers are unavailable', () => {
    const register = vi.fn<Register>(() => Promise.resolve({} as ServiceWorkerRegistration));

    registerServiceWorker(false, '/dist/', { register });
    registerServiceWorker(true, '/dist/', undefined);

    expect(register).not.toHaveBeenCalled();
  });

  it('keeps the app working when the browser refuses the worker', async () => {
    const register = vi.fn<Register>(() => Promise.reject(new Error('blocked')));

    expect(() => registerServiceWorker(true, '/dist/', { register })).not.toThrow();
    await Promise.resolve();
    expect(register).toHaveBeenCalledTimes(2);
  });
});

describe('area roots inside the worker scopes', () => {
  it('adds the slash only to the bare area roots', () => {
    expect(withAreaSlash('/app')).toBe('/app/');
    expect(withAreaSlash('/student')).toBe('/student/');
    for (const path of ['/app/', '/app/w/1', '/student/', '/student/settings', '/apple-teacher', '/students', '/admin', '/']) {
      expect(withAreaSlash(path)).toBeNull();
    }
  });

  it('replaces /student with /student/ (the same page), keeping the query and hash', async () => {
    fakeApi({ me: teacher() });
    const { router } = await renderRoute('/student?from=menu#top');

    expect(await screen.findByRole('heading', { level: 1, name: 'Мои часови' })).toBeInTheDocument();
    await waitFor(() => expect(router.state.location).toMatchObject({ pathname: '/student/', search: '?from=menu', hash: '#top' }));
    expect(router.state.historyAction).toBe('REPLACE');
  });

  it('replaces /app with /app/', async () => {
    fakeApi({ me: teacher() });
    const { router } = await renderRoute('/app');

    await waitFor(() => expect(router.state.location.pathname).not.toBe('/app'));
    expect(router.state.location.pathname.startsWith('/app/')).toBe(true);
  });
});

describe('manifest language', () => {
  afterEach(async () => {
    document.head.querySelector('link[rel="manifest"]')?.remove();
    await i18n.changeLanguage('mk');
  });

  it('points at the manifest in the UI language and follows a language change', async () => {
    expect(manifestFor('mk', '/dist/')).toBe('/dist/manifest.webmanifest');
    expect(manifestFor('en', '/dist/')).toBe('/dist/manifest.en.webmanifest');
    const link = document.createElement('link');
    link.rel = 'manifest';
    document.head.append(link);

    await i18n.changeLanguage('en');
    expect(new URL(link.href).pathname).toMatch(/manifest\.en\.webmanifest$/);

    await i18n.changeLanguage('mk');
    expect(new URL(link.href).pathname).toMatch(/\/manifest\.webmanifest$/);
  });
});

function installPrompt(outcome: 'accepted' | 'dismissed' = 'accepted') {
  const event = new Event('beforeinstallprompt', { cancelable: true }) as Event & { prompt: () => Promise<void>; userChoice: Promise<{ outcome: string }> };
  event.prompt = vi.fn<() => Promise<void>>(() => Promise.resolve());
  event.userChoice = Promise.resolve({ outcome });
  return event;
}

describe('install prompt', () => {
  // As main.tsx does at startup.
  beforeAll(() => captureInstallPrompt());

  beforeEach(async () => {
    localStorage.clear();
    await i18n.changeLanguage('en');
  });
  afterEach(async () => {
    window.dispatchEvent(new Event('appinstalled')); // forget any offer left over
    localStorage.clear();
    await i18n.changeLanguage('mk');
  });

  it('keeps an offer made before the app header exists (on the sign-in page)', async () => {
    const user = userEvent.setup();
    const event = installPrompt();
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);

    // Signed in: the header with the install button mounts later.
    render(<InstallApp />);
    await user.click(screen.getByRole('button', { name: 'Install the app' }));
    expect(event.prompt).toHaveBeenCalledOnce();
    expect(screen.queryByRole('button', { name: 'Install the app' })).toBeNull();
  });

  it('is hidden until the browser offers installing, then installs on request', async () => {
    const user = userEvent.setup();
    render(<InstallApp />);
    expect(screen.queryByRole('button', { name: 'Install the app' })).toBeNull();

    const event = installPrompt();
    act(() => {
      window.dispatchEvent(event);
    });
    expect(event.defaultPrevented).toBe(true);

    await user.click(screen.getByRole('button', { name: 'Install the app' }));
    expect(event.prompt).toHaveBeenCalledOnce();
    expect(screen.queryByRole('button', { name: 'Install the app' })).toBeNull();
  });

  it('disappears once installed', () => {
    render(<InstallApp />);
    act(() => {
      window.dispatchEvent(installPrompt());
    });
    expect(screen.getByRole('button', { name: 'Install the app' })).toBeDefined();

    act(() => {
      window.dispatchEvent(new Event('appinstalled'));
    });
    expect(screen.queryByRole('button', { name: 'Install the app' })).toBeNull();
  });

  it('is not offered again after the user declines it', async () => {
    const user = userEvent.setup();
    const first = render(<InstallApp />);
    act(() => {
      window.dispatchEvent(installPrompt());
    });

    await user.click(screen.getByRole('button', { name: 'Do not offer to install again' }));
    expect(screen.queryByRole('button', { name: 'Install the app' })).toBeNull();
    first.unmount();

    render(<InstallApp />);
    const later = installPrompt();
    act(() => {
      window.dispatchEvent(later);
    });
    expect(later.defaultPrevented).toBe(false);
    expect(screen.queryByRole('button', { name: 'Install the app' })).toBeNull();
  });

  it('still works when storage is unavailable', async () => {
    const user = userEvent.setup();
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied');
    });
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied');
    });
    try {
      render(<InstallApp />);
      act(() => {
        window.dispatchEvent(installPrompt());
      });
      await user.click(screen.getByRole('button', { name: 'Do not offer to install again' }));
      expect(screen.queryByRole('button', { name: 'Install the app' })).toBeNull();
    } finally {
      getItem.mockRestore();
      setItem.mockRestore();
    }
  });
});
