import { describe, expect, it, vi } from 'vitest';
import { createApiClient, XSRF_HEADER } from './client.ts';
import { ApiError } from './errors.ts';

type Call = { url: string; method: string; xsrf: string | null; credentials: RequestCredentials | undefined };

function fakeServer(handler: (call: Call, calls: Call[]) => Response) {
  const calls: Call[] = [];
  const fetchMock = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async (input, init) => {
    const request = input instanceof Request ? input : new Request(String(input), init);
    const call: Call = {
      url: new URL(request.url, 'http://localhost').pathname,
      method: request.method,
      xsrf: request.headers.get(XSRF_HEADER),
      credentials: input instanceof Request ? request.credentials : init?.credentials,
    };
    calls.push(call);
    return handler(call, calls);
  });
  return { calls, fetch: fetchMock as unknown as typeof fetch };
}

const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json', ...headers } });

const antiforgery = (token: string) => json(200, { requestToken: token }, { [XSRF_HEADER]: token });

describe('api client', () => {
  it('fetches an antiforgery token before the first unsafe request and sends it as a header', async () => {
    const server = fakeServer((call) => (call.url === '/api/auth/antiforgery' ? antiforgery('t1') : json(200, { ok: true })));
    const { client } = createApiClient({ baseUrl: 'http://localhost', fetch: server.fetch });

    await client.POST('/api/auth/logout' as never, {} as never);

    expect(server.calls.map((c) => c.url)).toEqual(['/api/auth/antiforgery', '/api/auth/logout']);
    expect(server.calls[1]?.xsrf).toBe('t1');
  });

  it('does not send the token on safe requests', async () => {
    const server = fakeServer(() => json(200, []));
    const { client } = createApiClient({ baseUrl: 'http://localhost', fetch: server.fetch });

    await client.GET('/api/auth/antiforgery' as never, {} as never);

    expect(server.calls).toHaveLength(1);
    expect(server.calls[0]?.xsrf).toBeNull();
  });

  it('adopts a token re-issued by the API (for example after sign-in)', async () => {
    const server = fakeServer((call) => {
      if (call.url === '/api/auth/antiforgery') return antiforgery('anon');
      if (call.url === '/api/auth/login') return json(200, {}, { [XSRF_HEADER]: 'signed-in' });
      return new Response(null, { status: 204 });
    });
    const api = createApiClient({ baseUrl: 'http://localhost', fetch: server.fetch });

    await api.client.POST('/api/auth/login' as never, {} as never);
    expect(api.token).toBe('signed-in');
    await api.client.POST('/api/auth/logout' as never, {} as never);

    expect(server.calls.at(-1)?.xsrf).toBe('signed-in');
  });

  it('refreshes a stale token once after ANTIFORGERY_INVALID and retries', async () => {
    let issued = 0;
    const server = fakeServer((call) => {
      if (call.url === '/api/auth/antiforgery') return antiforgery(`t${++issued}`);
      return call.xsrf === 't2' ? json(200, { ok: true }) : json(400, { code: 'ANTIFORGERY_INVALID', status: 400 });
    });
    const { client } = createApiClient({ baseUrl: 'http://localhost', fetch: server.fetch });

    const result = await client.POST('/api/x' as never, {} as never);

    expect((result as { data: unknown }).data).toEqual({ ok: true });
    expect(server.calls.filter((c) => c.url === '/api/x').map((c) => c.xsrf)).toEqual(['t1', 't2']);
  });

  it('turns ProblemDetails into ApiError with code and field errors', async () => {
    const server = fakeServer((call) =>
      call.url === '/api/auth/antiforgery'
        ? antiforgery('t')
        : json(400, { code: 'VALIDATION_FAILED', status: 400, title: 'x', errors: { email: ['bad'] }, traceId: 'abc' }),
    );
    const { client } = createApiClient({ baseUrl: 'http://localhost', fetch: server.fetch });

    const error = await client.POST('/api/auth/register' as never, {} as never).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 400, code: 'VALIDATION_FAILED', fieldErrors: { email: ['bad'] }, traceId: 'abc' });
  });

  it('reports network failures as NETWORK_ERROR', async () => {
    const failing = vi.fn<() => Promise<Response>>(async () => {
      throw new TypeError('Failed to fetch');
    }) as unknown as typeof fetch;
    const { client } = createApiClient({ baseUrl: 'http://localhost', fetch: failing });

    const error = await client.GET('/api/auth/me' as never, {} as never).catch((e: unknown) => e);

    expect(error).toMatchObject({ code: 'NETWORK_ERROR', status: 0 });
  });

  it('reports a network failure while fetching the antiforgery token as NETWORK_ERROR', async () => {
    const offline = vi.fn<() => Promise<Response>>(async () => {
      throw new TypeError('Failed to fetch');
    }) as unknown as typeof fetch;
    const { client } = createApiClient({ baseUrl: 'http://localhost', fetch: offline });

    const error = await client.POST('/api/auth/logout' as never, {} as never).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ code: 'NETWORK_ERROR', status: 0 });
  });

  it('reports a network failure during the stale-token refresh as NETWORK_ERROR', async () => {
    let online = true;
    const server = fakeServer((call) => {
      if (call.url === '/api/auth/antiforgery') {
        if (!online) throw new TypeError('Failed to fetch');
        return antiforgery('t1');
      }
      online = false; // the connection drops after the first rejection
      return json(400, { code: 'ANTIFORGERY_INVALID', status: 400 });
    });
    const { client } = createApiClient({ baseUrl: 'http://localhost', fetch: server.fetch });

    const error = await client.POST('/api/x' as never, {} as never).catch((e: unknown) => e);

    expect(error).toMatchObject({ code: 'NETWORK_ERROR', status: 0 });
  });

  it('always sends cookies', async () => {
    const server = fakeServer(() => json(200, {}));
    const { client } = createApiClient({ baseUrl: 'http://localhost', fetch: server.fetch });

    await client.GET('/api/auth/me' as never, {} as never);

    expect(server.calls[0]?.credentials).toBe('include');
  });
});
