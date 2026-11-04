import { vi } from 'vitest';

export interface FakeMe {
  id: string;
  email: string;
  emailConfirmed: boolean;
  locale: string;
  timeZone: string;
  platformRoles: string[];
}

export interface FakeCall {
  method: string;
  path: string;
  search: string;
  body: unknown;
  xsrf: string | null;
}

type Handler = (call: FakeCall) => Response | undefined;

export const WORKSPACE_ID = '11111111-1111-4111-8111-111111111111';

export const teacher = (overrides: Partial<FakeMe> = {}): FakeMe => ({
  id: '22222222-2222-4222-8222-222222222222',
  email: 'ana@example.com',
  emailConfirmed: true,
  locale: 'mk',
  timeZone: 'Europe/Skopje',
  platformRoles: [],
  ...overrides,
});

export const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json', ...headers } });

export const problem = (status: number, code: string, errors?: Record<string, string[]>) =>
  json(status, { status, code, title: 'server text that must never be shown', errors });

/**
 * Stubs global fetch with a tiny in-memory API: a session (or none), the teacher's workspace and the
 * antiforgery endpoint. `extra` handlers run first and may answer any request.
 */
export function fakeApi({ me = null as FakeMe | null, extra = [] as Handler[] } = {}) {
  const state = { me };
  const calls: FakeCall[] = [];

  const fetchMock = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async (input, init) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), 'http://localhost'), init);
    const url = new URL(request.url, 'http://localhost');
    const text = request.method === 'GET' ? '' : await request.clone().text();
    const call: FakeCall = {
      method: request.method,
      path: url.pathname,
      search: url.search,
      body: text ? (JSON.parse(text) as unknown) : undefined,
      xsrf: request.headers.get('X-XSRF-TOKEN'),
    };
    calls.push(call);

    for (const handler of extra) {
      const answer = handler(call);
      if (answer) return answer;
    }

    const route = `${call.method} ${call.path}`;
    if (route === 'GET /api/auth/antiforgery') return json(200, { requestToken: 'xsrf' }, { 'X-XSRF-TOKEN': 'xsrf' });
    if (route === 'GET /api/auth/me') return state.me ? json(200, state.me) : problem(401, 'UNAUTHORIZED');
    if (route === 'POST /api/auth/logout') {
      state.me = null;
      return new Response(null, { status: 204 });
    }
    if (route === 'GET /api/me/workspaces') return state.me ? json(200, [{ id: WORKSPACE_ID, name: 'Мој работен простор', role: 'Owner' }]) : problem(401, 'UNAUTHORIZED');
    if (route === `GET /api/w/${WORKSPACE_ID}`) {
      return state.me
        ? json(200, { id: WORKSPACE_ID, name: 'Математика со Ана', timeZone: 'Europe/Skopje', currency: 'MKD', locale: 'mk', role: 'Owner', createdAtUtc: '2026-11-04T10:00:00Z' })
        : problem(401, 'UNAUTHORIZED');
    }
    if (call.method === 'GET' && call.path.startsWith('/api/w/')) return problem(404, 'NOT_FOUND');
    return problem(404, 'NOT_FOUND');
  });

  vi.stubGlobal('fetch', fetchMock);
  return {
    calls,
    state,
    /** Calls to a path (any method unless given). */
    to: (path: string, method?: string) => calls.filter((c) => c.path === path && (!method || c.method === method)),
  };
}
