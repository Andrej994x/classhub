import { useEffect, useState } from 'react';

/**
 * Progressive web app (R3-P7). The service worker (public/sw.js) is registered only for the SPA areas, never for the
 * public pages, and only in the production build (the API serves it with Service-Worker-Allowed: /).
 */
export const PWA_SCOPES = ['/app/', '/student/'] as const;

type ServiceWorkers = Pick<ServiceWorkerContainer, 'register'>;

export function registerServiceWorker(production = import.meta.env.PROD, base = import.meta.env.BASE_URL, workers: ServiceWorkers | undefined = globalThis.navigator?.serviceWorker) {
  if (!production || !workers) return;
  for (const scope of PWA_SCOPES) {
    // Best effort: without a service worker the app works as before, only without the offline page.
    void workers.register(`${base}sw.js`, { scope }).catch(() => undefined);
  }
}

/** The manifest in the UI language (the app's name and description in mk or en). */
export function manifestFor(language: string, base = import.meta.env.BASE_URL) {
  return `${base}${language === 'en' ? 'manifest.en.webmanifest' : 'manifest.webmanifest'}`;
}

export function syncManifest(language: string) {
  if (typeof document === 'undefined') return;
  const link = document.querySelector<HTMLLinkElement>('link[rel="manifest"]');
  if (link) link.href = manifestFor(language);
}

/** The browser's install prompt, kept until the user decides (Chromium-based browsers offer it). */
type InstallPromptEvent = Event & { prompt: () => Promise<void>; userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }> };

const DISMISSED = 'classhub.install-dismissed';

function dismissedBefore(): boolean {
  try {
    return globalThis.localStorage?.getItem(DISMISSED) === '1';
  } catch {
    return false;
  }
}

/** Whether the app can be installed now, and the actions to install it or not to ask again. */
export function useInstallPrompt() {
  const [event, setEvent] = useState<InstallPromptEvent | null>(null);
  useEffect(() => {
    if (dismissedBefore()) return undefined;
    const onPrompt = (e: Event) => {
      e.preventDefault();
      setEvent(e as InstallPromptEvent);
    };
    const onInstalled = () => setEvent(null);
    window.addEventListener('beforeinstallprompt', onPrompt);
    window.addEventListener('appinstalled', onInstalled);
    return () => {
      window.removeEventListener('beforeinstallprompt', onPrompt);
      window.removeEventListener('appinstalled', onInstalled);
    };
  }, []);

  return {
    available: event !== null,
    install: async () => {
      if (!event) return;
      await event.prompt();
      await event.userChoice.catch(() => undefined);
      setEvent(null);
    },
    dismiss: () => {
      try {
        globalThis.localStorage?.setItem(DISMISSED, '1');
      } catch {
        // Not remembered (private mode): it is asked again next time.
      }
      setEvent(null);
    },
  };
}
