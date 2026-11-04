import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { safeReturnTo } from '../../../auth/session.ts';
import { i18n } from '../../../i18n/index.ts';
import { fakeApi, json, problem, teacher } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const PASSWORD = 'correct horse battery';

describe('sign in', () => {
  it('validates before calling the API', async () => {
    const user = userEvent.setup();
    const api = fakeApi();
    await renderRoute('/login');

    await user.click(await screen.findByRole('button', { name: 'Најави се' }));

    expect(await screen.findByText('Внесете ја вашата е-пошта.')).toBeInTheDocument();
    expect(screen.getByText('Внесете ја лозинката.')).toBeInTheDocument();
    expect(screen.getByLabelText('Е-пошта')).toHaveAttribute('aria-invalid', 'true');
    expect(api.to('/api/auth/login')).toHaveLength(0);
  });

  it('uses the right autocomplete attributes', async () => {
    fakeApi();
    await renderRoute('/login');
    expect(await screen.findByLabelText('Е-пошта')).toHaveAttribute('autocomplete', 'username');
    expect(screen.getByLabelText('Лозинка')).toHaveAttribute('autocomplete', 'current-password');
  });

  it('signs in with the antiforgery token and returns to the requested page', async () => {
    const user = userEvent.setup();
    const api = fakeApi({
      extra: [(call) => (call.path === '/api/auth/login' ? (api.state.me = teacher(), json(200, teacher(), { 'X-XSRF-TOKEN': 'signed-in' })) : undefined)],
    });
    const { router } = await renderRoute('/login?returnTo=%2Fstudent');

    await user.type(await screen.findByLabelText('Е-пошта'), 'ana@example.com');
    await user.type(screen.getByLabelText('Лозинка'), PASSWORD);
    await user.click(screen.getByRole('button', { name: 'Најави се' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Мои часови' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/student');
    expect(api.to('/api/auth/login')[0]).toMatchObject({ body: { email: 'ana@example.com', password: PASSWORD, rememberMe: false }, xsrf: 'xsrf' });
  });

  it('shows the translated error, never the server text, and clears the password', async () => {
    const user = userEvent.setup();
    fakeApi({ extra: [(call) => (call.path === '/api/auth/login' ? problem(401, 'INVALID_CREDENTIALS') : undefined)] });
    await renderRoute('/login');

    await user.type(await screen.findByLabelText('Е-пошта'), 'ana@example.com');
    await user.type(screen.getByLabelText('Лозинка'), 'wrong password');
    await user.click(screen.getByRole('button', { name: 'Најави се' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Е-поштата или лозинката не се точни.');
    expect(screen.queryByText(/server text/)).not.toBeInTheDocument();
    expect(screen.getByLabelText('Лозинка')).toHaveValue('');
  });

  it.each([
    ['/app/w/1', '/app/w/1'],
    ['//evil.example', '/app'],
    ['https://evil.example', '/app'],
    ['/\\evil.example', '/app'],
    [null, '/app'],
  ])('only returns to same-site paths (%s → %s)', (input, expected) => {
    expect(safeReturnTo(input)).toBe(expected);
  });
});

describe('registration', () => {
  it('sends the chosen UI language as the account language and shows the generic confirmation', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ extra: [(call) => (call.path === '/api/auth/register' ? json(202, { message: 'x' }) : undefined)] });
    await renderRoute('/register', 'en');

    await user.type(await screen.findByLabelText('Email'), 'new@example.com');
    await user.type(screen.getByLabelText('Password'), PASSWORD);
    await user.type(screen.getByLabelText('Repeat the password'), PASSWORD);
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Check your email' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('new@example.com');
    expect(api.to('/api/auth/register')[0]?.body).toEqual({ email: 'new@example.com', password: PASSWORD, locale: 'en' });
    await i18n.changeLanguage('mk');
  });

  it('checks the password length and confirmation in the browser', async () => {
    const user = userEvent.setup();
    const api = fakeApi();
    await renderRoute('/register');

    await user.type(await screen.findByLabelText('Е-пошта'), 'new@example.com');
    await user.type(screen.getByLabelText('Лозинка'), 'short');
    await user.type(screen.getByLabelText('Повторете ја лозинката'), 'different');
    await user.click(screen.getByRole('button', { name: 'Креирај сметка' }));

    expect(await screen.findByText('Лозинката мора да има најмалку 10 знаци.')).toBeInTheDocument();
    expect(screen.getByText('Лозинките не се совпаѓаат.')).toBeInTheDocument();
    expect(screen.getByLabelText('Лозинка')).toHaveAttribute('autocomplete', 'new-password');
    expect(api.to('/api/auth/register')).toHaveLength(0);
  });

  it('marks the password field when the API rejects it (common password)', async () => {
    const user = userEvent.setup();
    fakeApi({ extra: [(call) => (call.path === '/api/auth/register' ? problem(400, 'VALIDATION_FAILED', { password: ['too common'] }) : undefined)] });
    await renderRoute('/register');

    await user.type(await screen.findByLabelText('Е-пошта'), 'new@example.com');
    await user.type(screen.getByLabelText('Лозинка'), 'password12345');
    await user.type(screen.getByLabelText('Повторете ја лозинката'), 'password12345');
    await user.click(screen.getByRole('button', { name: 'Креирај сметка' }));

    expect(await screen.findByText(/Изберете посилна лозинка/)).toBeInTheDocument();
    expect(screen.getByLabelText('Лозинка')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.queryByText('too common')).not.toBeInTheDocument();
  });
});

describe('email confirmation and password reset links', () => {
  it('confirms the email from the link fragment and removes the token from the address bar', async () => {
    const api = fakeApi({ extra: [(call) => (call.path === '/api/auth/confirm-email' ? new Response(null, { status: 204 }) : undefined)] });
    window.history.replaceState(null, '', '/verify-email#userId=22222222-2222-4222-8222-222222222222&token=abc%2B1');
    await renderRoute('/verify-email');

    expect(await screen.findByText('Вашата адреса за е-пошта е потврдена.')).toBeInTheDocument();
    expect(api.to('/api/auth/confirm-email')).toHaveLength(1);
    expect(api.to('/api/auth/confirm-email')[0]?.body).toEqual({ userId: '22222222-2222-4222-8222-222222222222', token: 'abc+1' });
    expect(window.location.hash).toBe('');
  });

  it('explains an invalid or expired confirmation link', async () => {
    fakeApi({ extra: [(call) => (call.path === '/api/auth/confirm-email' ? problem(400, 'INVALID_TOKEN') : undefined)] });
    window.history.replaceState(null, '', '/verify-email#userId=22222222-2222-4222-8222-222222222222&token=bad');
    await renderRoute('/verify-email');

    expect(await screen.findByRole('alert')).toHaveTextContent('Линкот е неважечки или истечен');
  });

  it('resets the password with the token from the fragment', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ extra: [(call) => (call.path === '/api/auth/reset-password' ? new Response(null, { status: 204 }) : undefined)] });
    window.history.replaceState(null, '', '/reset-password#email=ana%40example.com&token=t%2F1');
    await renderRoute('/reset-password');

    await user.type(await screen.findByLabelText('Нова лозинка'), PASSWORD);
    await user.type(screen.getByLabelText('Повторете ја лозинката'), PASSWORD);
    await user.click(screen.getByRole('button', { name: 'Зачувај ја новата лозинка' }));

    expect(await screen.findByText(/Лозинката е променета/)).toBeInTheDocument();
    expect(api.to('/api/auth/reset-password')[0]?.body).toEqual({ email: 'ana@example.com', token: 't/1', newPassword: PASSWORD });
    expect(window.location.hash).toBe('');
  });

  it('answers the forgot-password request the same way for every address', async () => {
    const user = userEvent.setup();
    fakeApi({ extra: [(call) => (call.path === '/api/auth/forgot-password' ? json(202, { message: 'x' }) : undefined)] });
    await renderRoute('/forgot-password');

    await user.type(await screen.findByLabelText('Е-пошта'), 'someone@example.com');
    await user.click(screen.getByRole('button', { name: 'Испрати линк' }));

    expect(await screen.findByText('Ако постои сметка со оваа адреса, линкот за нова лозинка е на пат.')).toBeInTheDocument();
  });
});

describe('session controls', () => {
  it('asks unconfirmed accounts to confirm and can resend the link', async () => {
    const user = userEvent.setup();
    const api = fakeApi({
      me: teacher({ emailConfirmed: false }),
      extra: [(call) => (call.path === '/api/auth/resend-confirmation' ? json(202, { message: 'x' }) : undefined)],
    });
    await renderRoute('/student');

    await user.click(await screen.findByRole('button', { name: 'Испрати го линкот повторно' }));

    expect(await screen.findByText('Нов линк за потврда е на пат.')).toBeInTheDocument();
    expect(api.to('/api/auth/resend-confirmation')[0]?.body).toEqual({ email: 'ana@example.com' });
  });

  it('stays signed in and explains it when signing out fails', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher(), extra: [(call) => (call.path === '/api/auth/logout' ? problem(500, 'INTERNAL_ERROR') : undefined)] });
    const { router } = await renderRoute('/student');

    await user.click(await screen.findByRole('button', { name: 'Одјава' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Одјавата не успеа');
    expect(router.state.location.pathname).toBe('/student');
    expect(screen.getByRole('heading', { level: 1, name: 'Мои часови' })).toBeInTheDocument();
    expect(api.to('/api/auth/logout')).toHaveLength(1);
  });

  it('treats an already ended session (401) as signed out', async () => {
    const user = userEvent.setup();
    fakeApi({ me: teacher(), extra: [(call) => (call.path === '/api/auth/logout' ? problem(401, 'UNAUTHORIZED') : undefined)] });
    const { router } = await renderRoute('/student');

    await user.click(await screen.findByRole('button', { name: 'Одјава' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Најава' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/login');
  });

  it('signs out and goes to the sign-in page', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ me: teacher() });
    const { router } = await renderRoute('/student');

    await user.click(await screen.findByRole('button', { name: 'Одјава' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Најава' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/login');
    expect(api.to('/api/auth/logout')).toHaveLength(1);
  });
});
