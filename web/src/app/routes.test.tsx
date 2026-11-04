import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { meQueryKey } from '../auth/session.ts';
import { i18n } from '../i18n/index.ts';
import { fakeApi, teacher, WORKSPACE_ID } from '../test/fakeApi.ts';
import { renderRoute } from '../test/render.tsx';

describe('routes and layouts', () => {
  it('renders the home page in Macedonian by default', async () => {
    fakeApi();
    await renderRoute('/');
    expect(screen.getByRole('heading', { level: 1, name: 'Добредојдовте во ClassHub' })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'Регистрација' })).toHaveAttribute('href', '/register');
  });

  it('renders the home page in English', async () => {
    fakeApi();
    await renderRoute('/', 'en');
    expect(screen.getByRole('heading', { level: 1, name: 'Welcome to ClassHub' })).toBeInTheDocument();
  });

  it('offers the workspace instead of sign-up when signed in', async () => {
    fakeApi({ me: teacher() });
    await renderRoute('/');
    expect(await screen.findByRole('link', { name: 'Отвори го мојот простор' })).toHaveAttribute('href', '/app');
  });

  it.each([
    ['/student', 'Мои часови', 'Ученички простор'],
    ['/admin', 'Администрација', 'Администрација на платформата'],
  ])('renders the %s area shell with its navigation', async (path, title, area) => {
    fakeApi({ me: teacher() });
    await renderRoute(path);
    expect(await screen.findByRole('heading', { level: 1, name: title })).toBeInTheDocument();
    expect(screen.getAllByText(new RegExp(area)).length).toBeGreaterThan(0);
    expect(screen.getByRole('navigation', { name: 'Главна навигација' })).toBeInTheDocument();
  });

  it('opens the teacher workspace from /app and shows its name', async () => {
    fakeApi({ me: teacher() });
    const { router } = await renderRoute('/app');

    expect(await screen.findByRole('heading', { level: 1, name: 'Математика со Ана' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/app/w/${WORKSPACE_ID}`);
    const nav = screen.getByRole('navigation', { name: 'Главна навигација' });
    expect(within(nav).getByRole('link', { name: 'Почетна' })).toHaveClass('text-primary');
  });

  it("shows 'not found' for a workspace the teacher is not a member of", async () => {
    fakeApi({ me: teacher() });
    await renderRoute('/app/w/99999999-9999-4999-8999-999999999999');

    expect(await screen.findByRole('heading', { level: 1, name: 'Страницата не е пронајдена' })).toBeInTheDocument();
  });

  it('sends the user to sign in when the session ends while the app is open', async () => {
    const api = fakeApi({
      me: teacher(),
      // The session is revoked on the server just before the workspace is loaded.
      extra: [(call) => (call.path === `/api/w/${WORKSPACE_ID}` ? ((api.state.me = null), new Response(JSON.stringify({ status: 401, code: 'UNAUTHORIZED' }), { status: 401, headers: { 'content-type': 'application/json' } })) : undefined)],
    });
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}`);

    expect(await screen.findByRole('heading', { level: 1, name: 'Најава' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/login');
    expect(new URLSearchParams(router.state.location.search).get('returnTo')).toBe(`/app/w/${WORKSPACE_ID}`);
  });

  it('sends anonymous visitors to sign in and remembers where they wanted to go', async () => {
    fakeApi();
    const { router } = await renderRoute('/app/w/abc?tab=1');

    expect(await screen.findByRole('heading', { level: 1, name: 'Најава' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/login');
    expect(new URLSearchParams(router.state.location.search).get('returnTo')).toBe('/app/w/abc?tab=1');
  });

  it('opens the mobile navigation drawer and closes it after navigating', async () => {
    const user = userEvent.setup();
    fakeApi({ me: teacher() });
    await renderRoute('/app');
    await screen.findByRole('heading', { level: 1, name: 'Математика со Ана' });

    await user.click(screen.getByRole('button', { name: 'Отвори мени' }));
    const drawer = await screen.findByRole('dialog', { name: 'Наставнички простор' });
    await user.click(within(drawer).getByRole('link', { name: 'Поставки' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('switches the UI language and remembers it', async () => {
    const user = userEvent.setup();
    const api = fakeApi();
    await renderRoute('/');

    await user.selectOptions(screen.getByRole('combobox', { name: 'Јазик' }), 'en');

    expect(await screen.findByRole('heading', { level: 1, name: 'Welcome to ClassHub' })).toBeInTheDocument();
    expect(window.localStorage.getItem('classhub.language')).toBe('en');
    expect(document.documentElement.lang).toBe('en');
    expect(api.to('/api/auth/me', 'PATCH')).toHaveLength(0); // anonymous: nothing saved on the server
    await i18n.changeLanguage('mk');
  });

  it('saves the language on the account when signed in', async () => {
    const user = userEvent.setup();
    const api = fakeApi({
      me: teacher(),
      extra: [(call) => (call.method === 'PATCH' && call.path === '/api/auth/me' ? new Response(JSON.stringify(teacher({ locale: 'en' })), { status: 200, headers: { 'content-type': 'application/json' } }) : undefined)],
    });
    await renderRoute('/app');
    await screen.findByRole('heading', { level: 1, name: 'Математика со Ана' });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Јазик' }), 'en');

    await screen.findByRole('heading', { level: 1, name: 'Математика со Ана' });
    await expect.poll(() => api.to('/api/auth/me', 'PATCH').length).toBe(1);
    expect(api.to('/api/auth/me', 'PATCH')[0]).toMatchObject({ body: { locale: 'en' }, xsrf: 'xsrf' });
    await i18n.changeLanguage('mk');
  });

  it('keeps the account when a 401 belongs to an earlier identity (the API still knows the session)', async () => {
    fakeApi({
      me: teacher(),
      extra: [(call) => (call.path === `/api/w/${WORKSPACE_ID}` ? new Response(JSON.stringify({ status: 401, code: 'UNAUTHORIZED' }), { status: 401, headers: { 'content-type': 'application/json' } }) : undefined)],
    });
    const { router } = await renderRoute(`/app/w/${WORKSPACE_ID}`);

    await expect.poll(() => screen.queryByRole('alert')?.textContent ?? '').toContain('Најавете се за да продолжите');
    expect(router.state.location.pathname).toBe(`/app/w/${WORKSPACE_ID}`);
    expect(screen.getByRole('navigation', { name: 'Главна навигација' })).toBeInTheDocument();
  });

  it("forgets the previous account's workspace data when another account signed in elsewhere", async () => {
    const api = fakeApi({ me: teacher() });
    const { queryClient } = await renderRoute(`/app/w/${WORKSPACE_ID}`);
    await screen.findByRole('heading', { level: 1, name: 'Математика со Ана' });
    expect(queryClient.getQueryData(['w', WORKSPACE_ID])).toBeDefined();

    // Another tab signed out and signed in as a different teacher; this tab re-checks the session.
    api.state.me = teacher({ id: '33333333-3333-4333-8333-333333333333', email: 'bojan@example.com' });
    await act(() => queryClient.invalidateQueries({ queryKey: meQueryKey }));

    await expect.poll(() => queryClient.getQueryData<{ email: string }>(meQueryKey)?.email).toBe('bojan@example.com');
    expect(queryClient.getQueryData(['w', WORKSPACE_ID])).toBeUndefined();
    expect(queryClient.getQueryData(['me', 'workspaces'])).toBeUndefined();
  });

  it('ignores a late language answer that arrives after signing out', async () => {
    const user = userEvent.setup();
    let answer!: (response: Response) => void;
    const late = new Promise<Response>((resolve) => {
      answer = resolve;
    });
    const api = fakeApi({ me: teacher(), extra: [(call) => (call.method === 'PATCH' && call.path === '/api/auth/me' ? late : undefined)] });
    const { router } = await renderRoute('/student');
    await screen.findByRole('heading', { level: 1, name: 'Мои часови' });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Јазик' }), 'en');
    await user.click(await screen.findByRole('button', { name: 'Sign out' }));
    await screen.findByRole('heading', { level: 1, name: 'Sign in' });

    answer(new Response(JSON.stringify(teacher({ locale: 'en' })), { status: 200, headers: { 'content-type': 'application/json' } }));
    await expect.poll(() => api.to('/api/auth/me', 'PATCH').length).toBe(1);
    await new Promise((done) => setTimeout(done, 50));

    expect(router.state.location.pathname).toBe('/login');
    expect(screen.getByRole('heading', { level: 1, name: 'Sign in' })).toBeInTheDocument();
    // The ended session is never brought back (no bounce into the teacher area).
    expect(api.to('/api/me/workspaces')).toHaveLength(0);
    await i18n.changeLanguage('mk');
  });

  it('adopts the account language after loading the session', async () => {
    fakeApi({ me: teacher({ locale: 'en' }) });
    await renderRoute('/app');

    expect(await screen.findByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument();
    await i18n.changeLanguage('mk');
  });

  it('renders the auth shell at /login and switches its language', async () => {
    const user = userEvent.setup();
    await renderRoute('/login');

    expect(screen.getByRole('heading', { level: 1, name: 'Најава' })).toBeInTheDocument();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Јазик' }), 'en');

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in' })).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('en');
    await i18n.changeLanguage('mk');
  });

  it.each(['/register', '/verify-email', '/forgot-password', '/reset-password'])('renders %s inside the auth shell', async (path) => {
    await renderRoute(path);
    expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Јазик' })).toBeInTheDocument();
    expect(screen.queryByText(/Страницата не е пронајдена/)).not.toBeInTheDocument();
  });

  it('renders a localized not-found page for unknown paths', async () => {
    fakeApi();
    await renderRoute('/does-not-exist');
    expect(screen.getByRole('heading', { name: /Страницата не е пронајдена/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Назад кон почетната страница' })).toHaveAttribute('href', '/');
  });
});
