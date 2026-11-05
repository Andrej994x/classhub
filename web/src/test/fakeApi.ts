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
  firstName: null as string | null, lastName: null as string | null, bio: null as string | null,
  teachingMode: null as string | null, countryCode: null as string | null,
  subjectIds: [] as string[], levelIds: [] as string[], languages: [] as string[], hasPhoto: false, photoVersion: null as string | null, updatedAtUtc: null, version: null as number | null,
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

export const SECTION_TYPES = ['hero', 'about', 'subjects', 'experience', 'offers', 'testimonials', 'faq', 'contact'];

export interface FakeSite {
  exists: boolean;
  slug: string;
  template: string;
  palette: string;
  language: string;
  draft: { sections: { type: string; visible: boolean; title: string | null; text: string | null; showPhoto: boolean; items: { title: string | null; subtitle: string | null; text: string | null }[] | null }[] };
  isPublished: boolean;
  updatedAtUtc: string | null;
  version: number | null;
  publishedAtUtc: string | null;
  hasUnpublishedChanges: boolean;
  viewsLast30Days: number;
}

export const NEW_SITE = (): FakeSite => ({
  exists: false,
  slug: 'ana-petrovska',
  template: 'classic',
  palette: 'ocean',
  language: 'mk',
  draft: {
    sections: SECTION_TYPES.map((type) => ({
      type,
      visible: !['experience', 'testimonials', 'faq'].includes(type),
      title: `Наслов ${type}`,
      text: null,
      showPhoto: type === 'hero',
      items: ['experience', 'testimonials', 'faq'].includes(type) ? [] : null,
    })),
  },
  isPublished: false,
  updatedAtUtc: null,
  version: null,
  publishedAtUtc: null,
  hasUnpublishedChanges: false,
  viewsLast30Days: 0,
});

export interface FakeOffer {
  id: string;
  kind: string;
  title: string;
  description: string | null;
  subjectId: string;
  durationMinutes: number;
  priceMinor: number;
  currency: string;
  mode: string;
  lessonCount: number | null;
  seats: number | null;
  isActive: boolean;
  showOnPublicSite: boolean;
  displayOrder: number;
  updatedAtUtc: string;
  version: number;
}

export const offer = (overrides: Partial<FakeOffer> = {}): FakeOffer => ({
  id: 'offer-1', kind: 'SingleLesson', title: 'Час по математика', description: null, subjectId: SUBJECT_MATH, durationMinutes: 60,
  priceMinor: 150_000, currency: 'MKD', mode: 'Online', lessonCount: null, seats: null, isActive: true, showOnPublicSite: true,
  displayOrder: 1, updatedAtUtc: '2026-11-05T10:00:00Z', version: 0, ...overrides,
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
  const state = { me, profile: { ...EMPTY_PROFILE }, onboarding: ONBOARDING(0), catalog: CATALOG, offers: [] as FakeOffer[], site: NEW_SITE(), slugTaken: [] as string[] };
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
    if (route === 'GET /api/catalog') return state.me ? json(200, state.catalog) : problem(401, 'UNAUTHORIZED');
    const sitePath = `/api/w/${WORKSPACE_ID}/site`;
    if (state.me && call.path === sitePath && call.method === 'GET') return json(200, state.site);
    if (state.me && call.path === sitePath && call.method === 'PUT') {
      const body = call.body as { version: number | null; slug: string };
      if ((body.version ?? 0) !== (state.site.version ?? 0)) return problem(409, 'CONCURRENCY_CONFLICT');
      if (state.slugTaken.includes(body.slug)) return problem(409, 'SLUG_TAKEN');
      state.site = { ...state.site, ...(body as object), exists: true, version: (state.site.version ?? 0) + 1, hasUnpublishedChanges: state.site.isPublished } as FakeSite;
      return json(200, state.site);
    }
    if (state.me && (call.path === `${sitePath}/publish` || call.path === `${sitePath}/unpublish`) && call.method === 'POST') {
      if (!state.site.exists) return problem(422, 'SITE_NOT_CREATED');
      if ((call.body as { version: number | null }).version !== state.site.version) return problem(409, 'CONCURRENCY_CONFLICT');
      const publish = call.path.endsWith('/publish');
      state.site = {
        ...state.site,
        isPublished: publish,
        publishedAtUtc: publish ? '2026-11-05T10:30:00Z' : null,
        hasUnpublishedChanges: false,
        version: (state.site.version ?? 0) + 1,
      };
      return json(200, state.site);
    }
    if (state.me && call.path === `${sitePath}/slug-availability`) {
      const slug = new URLSearchParams(call.search).get('slug') ?? '';
      return json(200, { slug, available: !state.slugTaken.includes(slug), reason: state.slugTaken.includes(slug) ? 'taken' : null });
    }
    if (state.me && call.path === `${sitePath}/preview` && call.method === 'POST') {
      return state.site.exists ? json(200, { url: '/preview/token-123', expiresAtUtc: '2026-11-05T10:15:00Z' }) : problem(422, 'SITE_NOT_CREATED');
    }
    const offersPath = `/api/w/${WORKSPACE_ID}/offers`;
    if (state.me && call.path === offersPath && call.method === 'GET') return json(200, state.offers);
    if (state.me && call.path === offersPath && call.method === 'POST') {
      const created = { ...(call.body as object), id: `offer-${state.offers.length + 1}`, displayOrder: state.offers.length + 1, version: 0, updatedAtUtc: '2026-11-05T10:00:00Z' } as FakeOffer;
      state.offers.push(created);
      return json(201, created);
    }
    if (state.me && call.path === `${offersPath}/order` && call.method === 'PUT') {
      const ids = (call.body as { offerIds: string[] }).offerIds;
      state.offers = ids.map((id, i) => ({ ...state.offers.find((o) => o.id === id)!, displayOrder: i + 1 }));
      return json(200, state.offers);
    }
    const offerMatch = state.me ? /^\/api\/w\/[^/]+\/offers\/([^/]+)$/.exec(call.path) : null;
    if (offerMatch && call.path.startsWith(offersPath)) {
      const index = state.offers.findIndex((o) => o.id === offerMatch[1]);
      if (index < 0) return problem(404, 'NOT_FOUND');
      if (call.method === 'GET') return json(200, state.offers[index]);
      if (call.method === 'DELETE') {
        state.offers.splice(index, 1);
        return new Response(null, { status: 204 });
      }
      if (call.method === 'PUT') {
        const body = call.body as { version: number };
        if (body.version !== state.offers[index]!.version) return problem(409, 'CONCURRENCY_CONFLICT');
        state.offers[index] = { ...state.offers[index]!, ...body, version: body.version + 1 };
        return json(200, state.offers[index]);
      }
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
