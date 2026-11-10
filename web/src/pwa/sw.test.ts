import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it, vi } from 'vitest';

/**
 * The service worker (public/sw.js) run against stand-ins for the worker globals: which requests it answers, from
 * where, and that storage failures never cost a response.
 */
// Tests run from web/ (npm test, CI).
const SOURCE = readFileSync(resolve(process.cwd(), 'public/sw.js'), 'utf8');
const ORIGIN = 'https://classhub.test';

type FakeRequest = { method: string; url: string; mode: string };
type FetchEvent = { request: FakeRequest; respondWith: (response: Promise<Response>) => void };
type FakeCache = { match: (r: unknown) => Promise<Response | undefined>; put: (r: unknown, res: Response) => Promise<void>; keys: () => Promise<{ url: string }[]>; delete: (r: unknown) => Promise<boolean> };

const key = (r: unknown) => (typeof r === 'string' ? new URL(r, ORIGIN).href : (r as FakeRequest).url);

function memoryCache(): FakeCache & { stored: Map<string, Response> } {
  const stored = new Map<string, Response>();
  return {
    stored,
    match: (r) => Promise.resolve(stored.get(key(r))),
    put: (r, response) => {
      stored.set(key(r), response);
      return Promise.resolve();
    },
    keys: () => Promise.resolve([...stored.keys()].map((url) => ({ url }))),
    delete: (r) => Promise.resolve(stored.delete((r as { url: string }).url)),
  };
}

function worker(cache: FakeCache | Error, network: (request: FakeRequest) => Promise<Response>) {
  const handlers: Record<string, (event: FetchEvent) => void> = {};
  const self = {
    location: { href: `${ORIGIN}/dist/sw.js`, origin: ORIGIN },
    addEventListener: (type: string, handler: (event: FetchEvent) => void) => {
      handlers[type] = handler;
    },
    skipWaiting: () => Promise.resolve(),
    clients: { claim: () => Promise.resolve() },
  };
  const caches = {
    open: () => (cache instanceof Error ? Promise.reject(cache) : Promise.resolve(cache)),
    match: (r: unknown) => (cache instanceof Error ? Promise.reject(cache) : cache.match(r)),
  };
  const fetch = vi.fn<(request: FakeRequest) => Promise<Response>>(network);
  // eslint-disable-next-line typescript/no-implied-eval -- runs the worker's own source file with stand-in globals
  new Function('self', 'caches', 'fetch', SOURCE)(self, caches, fetch);

  /** The response the worker gives, or null when it leaves the request to the browser. */
  async function handle(path: string, { method = 'GET', mode = 'cors' } = {}): Promise<Response | null> {
    let answered = null as Promise<Response> | null;
    handlers.fetch!({ request: { method, url: new URL(path, ORIGIN).href, mode }, respondWith: (response) => (answered = response) });
    return answered ? await answered : null;
  }
  return { handle, fetch };
}

/** A same-origin network response (constructed responses report type "default", the browser's say "basic"). */
const ok = (body: string) => Promise.resolve(Object.defineProperty(new Response(body, { status: 200 }), 'type', { value: 'basic' }));
const quotaExceeded = Object.assign(new Error('The quota has been exceeded.'), { name: 'QuotaExceededError' });

describe('service worker', () => {
  it('serves hashed assets from the cache once fetched', async () => {
    const cache = memoryCache();
    const { handle, fetch } = worker(cache, () => ok('js'));

    expect(await (await handle('/dist/assets/index-abc.js'))!.text()).toBe('js');
    expect(await (await handle('/dist/assets/index-abc.js'))!.text()).toBe('js');

    expect(fetch).toHaveBeenCalledOnce();
    expect([...cache.stored.keys()]).toEqual([`${ORIGIN}/dist/assets/index-abc.js`]);
  });

  it('still returns a downloaded asset when storing it fails', async () => {
    const cache = memoryCache();
    cache.put = () => Promise.reject(quotaExceeded);
    const { handle } = worker(cache, () => ok('js'));

    const response = await handle('/dist/assets/index-abc.js');

    expect(response!.status).toBe(200);
    expect(await response!.text()).toBe('js');
  });

  it('still loads assets when the cache cannot be opened or read', async () => {
    expect(await (await worker(quotaExceeded, () => ok('css')).handle('/dist/assets/app.css'))!.text()).toBe('css');

    const unreadable = memoryCache();
    unreadable.match = () => Promise.reject(new Error('broken'));
    expect(await (await worker(unreadable, () => ok('css')).handle('/dist/assets/app.css'))!.text()).toBe('css');
  });

  it('never answers API requests, other methods or other origins', async () => {
    const cache = memoryCache();
    const { handle, fetch } = worker(cache, () => ok('data'));

    expect(await handle('/api/auth/me')).toBeNull();
    expect(await handle('/api/w/1/activities', { method: 'POST' })).toBeNull();
    expect(await handle('/dist/assets/index-abc.js', { method: 'POST' })).toBeNull();
    expect(await handle('https://cdn.example/dist/assets/x.js')).toBeNull();
    expect(fetch).not.toHaveBeenCalled();
    expect(cache.stored.size).toBe(0);
  });

  it('loads pages from the network and shows the offline page only without a connection', async () => {
    const cache = memoryCache();
    await cache.put(`${ORIGIN}/dist/offline.html`, new Response('offline page'));
    let online = true;
    const { handle } = worker(cache, () => (online ? ok('fresh page') : Promise.reject(new TypeError('Failed to fetch'))));

    expect(await (await handle('/app/w/1', { mode: 'navigate' }))!.text()).toBe('fresh page');
    online = false;
    expect(await (await handle('/app/w/1', { mode: 'navigate' }))!.text()).toBe('offline page');
    expect([...cache.stored.keys()]).toEqual([`${ORIGIN}/dist/offline.html`]);
  });

  it('gives the browser its own error when even the offline page is unavailable', async () => {
    const { handle } = worker(quotaExceeded, () => Promise.reject(new TypeError('Failed to fetch')));

    const response = await handle('/student/', { mode: 'navigate' });

    expect(response!.type).toBe('error');
  });
});
