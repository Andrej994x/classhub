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
  contentType: string | null;
}

type Handler = (call: FakeCall) => Response | Promise<Response> | undefined;

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

export const SUBJECT_MATH = '33333333-0000-4000-8000-000000000001';
export const SUBJECT_ENGLISH = '33333333-0000-4000-8000-000000000002';
export const LEVEL_SECONDARY = '44444444-0000-4000-8000-000000000001';

export const CATALOG = {
  categories: [
    { id: 'c1', code: 'math-science', nameMk: 'Математика и природни науки', nameEn: 'Mathematics & science' },
    { id: 'c2', code: 'languages', nameMk: 'Јазици', nameEn: 'Languages' },
  ],
  subjects: [
    { id: SUBJECT_MATH, code: 'math', categoryId: 'c1', nameMk: 'Математика', nameEn: 'Mathematics' },
    { id: SUBJECT_ENGLISH, code: 'english', categoryId: 'c2', nameMk: 'Англиски јазик', nameEn: 'English' },
  ],
  levels: [{ id: LEVEL_SECONDARY, code: 'secondary', nameMk: 'Средно', nameEn: 'Secondary' }],
  languages: [
    { code: 'mk', nameMk: 'Македонски', nameEn: 'Macedonian' },
    { code: 'en', nameMk: 'Англиски', nameEn: 'English' },
  ],
  countries: ['DE', 'MK', 'US'],
};

export const EMPTY_PROFILE = {
  firstName: null, lastName: null, bio: null, teachingMode: null, countryCode: null,
  subjectIds: [] as string[], levelIds: [] as string[], languages: [] as string[], hasPhoto: false, photoVersion: null as string | null, updatedAtUtc: null,
};

export const ONBOARDING = (completed: number) => ({
  steps: [
    { key: 'profile', completed: completed >= 1, available: true },
    { key: 'subjects', completed: completed >= 2, available: true },
    { key: 'first-offer', completed: false, available: false },
    { key: 'public-page', completed: false, available: false },
    { key: 'publish', completed: false, available: false },
  ],
  completed,
  total: 5,
});

const parseBody = (text: string): unknown => {
  if (!text) return undefined;
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return text; // multipart or other non-JSON bodies
  }
};

export const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json', ...headers } });

export const problem = (status: number, code: string, errors?: Record<string, string[]>) =>
  json(status, { status, code, title: 'server text that must never be shown', errors });

/**
 * Stubs global fetch with a tiny in-memory API: a session (or none), the teacher's workspace and the
 * antiforgery endpoint. `extra` handlers run first and may answer any request.
 */
export function fakeApi({ me = null as FakeMe | null, extra = [] as Handler[] } = {}) {
  const state = { me, profile: { ...EMPTY_PROFILE }, onboarding: ONBOARDING(0) };
  const calls: FakeCall[] = [];

  const fetchMock = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async (input, init) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), 'http://localhost'), init);
    const url = new URL(request.url, 'http://localhost');
    const text = request.method === 'GET' ? '' : await request.clone().text();
    const call: FakeCall = {
      method: request.method,
      path: url.pathname,
      search: url.search,
      body: parseBody(text),
      xsrf: request.headers.get('X-XSRF-TOKEN'),
      contentType: request.headers.get('content-type'),
    };
    calls.push(call);

    for (const handler of extra) {
      const answer = await handler(call);
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
    if (route === `GET /api/w/${WORKSPACE_ID}/onboarding`) return state.me ? json(200, state.onboarding) : problem(401, 'UNAUTHORIZED');
    if (route === `GET /api/w/${WORKSPACE_ID}/profile`) return state.me ? json(200, state.profile) : problem(401, 'UNAUTHORIZED');
    if (route === 'GET /api/catalog') return state.me ? json(200, CATALOG) : problem(401, 'UNAUTHORIZED');
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
