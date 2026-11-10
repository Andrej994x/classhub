/*
 * ClassHub's service worker (R3-P7). Registered only for the SPA areas (/app/ and /student/), never for the public
 * pages. It caches static, content-hashed build assets and the offline page, nothing else:
 *  - pages (navigations): always from the network; offline, the offline page (which says that what was on screen may be
 *    out of date). A cached page is never shown as current.
 *  - API requests and everything else: straight to the network, never cached.
 *  - /dist/assets/* (immutable, hashed names): from the cache when present, otherwise fetched and kept (the newest 300).
 */
const CACHE = 'classhub-static-v1';
const BASE = new URL('./', self.location.href).pathname; // "/dist/"
const OFFLINE = `${BASE}offline.html`;
const ASSETS = `${BASE}assets/`;
const MAX_ASSETS = 300;

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(CACHE)
      .then((cache) => cache.add(new Request(OFFLINE, { cache: 'reload' })))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((key) => key.startsWith('classhub-') && key !== CACHE).map((key) => caches.delete(key))))
      .then(() => self.clients.claim()),
  );
});

async function trim(cache) {
  const keys = await cache.keys();
  const assets = keys.filter((request) => new URL(request.url).pathname.startsWith(ASSETS));
  await Promise.all(assets.slice(0, Math.max(0, assets.length - MAX_ASSETS)).map((request) => cache.delete(request)));
}

async function fromCacheOrNetwork(request) {
  const cache = await caches.open(CACHE);
  const cached = await cache.match(request);
  if (cached) return cached;
  const response = await fetch(request);
  if (response.ok && response.type === 'basic') {
    await cache.put(request, response.clone());
    await trim(cache);
  }
  return response;
}

async function pageOrOffline(request) {
  try {
    return await fetch(request);
  } catch {
    const offline = await caches.match(OFFLINE);
    return offline ?? Response.error();
  }
}

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET') return;
  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;
  if (request.mode === 'navigate') {
    event.respondWith(pageOrOffline(request));
  } else if (url.pathname.startsWith(ASSETS)) {
    event.respondWith(fromCacheOrNetwork(request));
  }
  // Everything else (the API above all) goes to the network as usual: never cached.
});
