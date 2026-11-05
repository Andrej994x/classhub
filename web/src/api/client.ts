import createClient, { type Middleware } from 'openapi-fetch';
import { ApiError, toApiError } from './errors.ts';
import type { paths } from './schema';

/**
 * Antiforgery for cookie-authenticated requests: the API returns the request token in the body and the
 * X-XSRF-TOKEN header of GET /api/auth/antiforgery, and re-issues it on sign-in/sign-out. The token is kept
 * in memory only (never in a readable cookie or storage) and sent as X-XSRF-TOKEN on unsafe requests.
 */
export const XSRF_HEADER = 'X-XSRF-TOKEN';
const UNSAFE = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export interface ApiClientOptions {
  baseUrl?: string;
  fetch?: typeof globalThis.fetch;
}

// The default looks up globalThis.fetch on every call, so tests can stub it after this module loaded.
export function createApiClient({ baseUrl = '', fetch: fetchImpl = (input, init) => globalThis.fetch(input, init) }: ApiClientOptions = {}) {
  let token: string | null = null;
  let pending: Promise<string> | null = null;
  // Requests still waiting for a response, and who waits for none to be left (see whenIdle).
  let inFlight = 0;
  let idleWaiters: (() => void)[] = [];

  const settle = () => {
    inFlight--;
    if (inFlight === 0) {
      const waiters = idleWaiters;
      idleWaiters = [];
      for (const resolve of waiters) resolve();
    }
  };

  const capture = (response: Response) => {
    const issued = response.headers.get(XSRF_HEADER);
    if (issued) token = issued;
  };

  async function refreshToken(): Promise<string> {
    pending ??= (async () => {
      let response: Response;
      try {
        response = await fetchImpl(`${baseUrl}/api/auth/antiforgery`, { credentials: 'include', cache: 'no-store' });
      } catch {
        throw ApiError.network();
      }
      if (!response.ok) throw await toApiError(response);
      const body = (await response.json()) as { requestToken: string };
      token = body.requestToken;
      return token;
    })().finally(() => {
      pending = null;
    });
    return pending;
  }

  /** fetch with cookies, antiforgery header, one retry after a stale-token rejection. */
  async function apiFetch(input: Request): Promise<Response> {
    const unsafe = UNSAFE.has(input.method.toUpperCase());
    const send = async (retry: boolean): Promise<Response> => {
      const request = input.clone();
      if (unsafe) request.headers.set(XSRF_HEADER, token ?? (await refreshToken()));
      let response: Response;
      inFlight++;
      try {
        response = await fetchImpl(request);
      } catch {
        throw ApiError.network();
      } finally {
        settle();
      }
      capture(response);
      if (unsafe && retry && response.status === 400 && (await toApiError(response)).code === 'ANTIFORGERY_INVALID') {
        // The token belonged to a previous identity (sign-in/sign-out in another tab): fetch a new one once.
        token = null;
        await refreshToken();
        return send(false);
      }
      return response;
    };
    return send(true);
  }

  const client = createClient<paths>({
    baseUrl,
    credentials: 'include',
    fetch: (input: Request) => apiFetch(input),
  });

  const errors: Middleware = {
    async onResponse({ response }) {
      if (!response.ok) throw await toApiError(response);
      return response;
    },
  };
  client.use(errors);

  return {
    client,
    refreshToken,
    /**
     * Resolves when no API request is waiting for its response (or after `timeoutMs`). Signing out waits for this:
     * a response that arrives after the logout response can carry a renewed session cookie and sign the browser in
     * again.
     */
    whenIdle: (timeoutMs = 5000): Promise<void> =>
      inFlight === 0
        ? Promise.resolve()
        : new Promise<void>((resolve) => {
            const timer = setTimeout(resolve, timeoutMs);
            idleWaiters.push(() => {
              clearTimeout(timer);
              resolve();
            });
          }),
    /** Drops the cached token (for example after a 401 when the session ended). */
    resetToken: () => {
      token = null;
    },
    get token() {
      return token;
    },
  };
}

// Absolute same-origin base: Request() needs an absolute URL outside browsers (tests); same result in the app.
export const api = createApiClient({ baseUrl: typeof window === 'undefined' ? '' : window.location.origin });
