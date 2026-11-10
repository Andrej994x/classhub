import { useEffect, useSyncExternalStore } from 'react';
import { useLocation, useNavigate } from 'react-router';

/**
 * Progressive web app (R3-P7). The service worker (public/sw.js) is registered only for the SPA areas, never for the
 * public pages, and only in the production build (the API serves it with Service-Worker-Allowed: /).
 */
export const PWA_SCOPES = ['/app/', '/student/'] as const;

/**
 * The area roots without the trailing slash ("/app", "/student") lie outside the worker's scopes, so they are replaced
 * by "/app/" and "/student/" (same page) to keep the offline page working there too. Scopes without the slash would
 * be plain string prefixes and also catch public teacher sites such as "/apple-…".
 */
export function withAreaSlash(pathname: string): string | null {
  return pathname === '/app' || pathname === '/student' ? `${pathname}/` : null;
}

export function useAreaRootSlash() {
  const { pathname, search, hash } = useLocation();
  const navigate = useNavigate();
  const target = withAreaSlash(pathname);
  useEffect(() => {
    if (target) void navigate({ pathname: target, search, hash }, { replace: true });
  }, [target, search, hash, navigate]);
}

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

/**
 * The offer is kept for the whole visit: the browser fires `beforeinstallprompt` once, possibly on the sign-in page
 * before the app header (and its install button) exists, and never repeats it. So it is captured at startup
 * (`captureInstallPrompt`, from main.tsx) and the header reads it from here.
 */
let offer: InstallPromptEvent | null = null;
const listeners = new Set<() => void>();
let capturing = false;

function setOffer(next: InstallPromptEvent | null) {
  offer = next;
  for (const listener of listeners) listener();
}

export function captureInstallPrompt(target: Pick<Window, 'addEventListener'> = window) {
  if (capturing) return;
  capturing = true;
  target.addEventListener('beforeinstallprompt', (e) => {
    // After "do not offer again" the browser keeps its own install UI; ClassHub shows nothing.
    if (dismissedBefore()) return;
    e.preventDefault();
    setOffer(e as InstallPromptEvent);
  });
  target.addEventListener('appinstalled', () => setOffer(null));
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** Whether the app can be installed now, and the actions to install it or not to ask again. */
export function useInstallPrompt() {
  const event = useSyncExternalStore(subscribe, () => offer, () => null);

  return {
    available: event !== null,
    install: async () => {
      if (!event) return;
      setOffer(null); // a prompt can be shown only once
      await event.prompt();
      await event.userChoice.catch(() => undefined);
    },
    dismiss: () => {
      try {
        globalThis.localStorage?.setItem(DISMISSED, '1');
      } catch {
        // Not remembered (private mode): it is asked again next time.
      }
      setOffer(null);
    },
  };
}
