import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, problem, teacher } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const PASSWORD = 'correct horse battery';

function twoFactorApi() {
  const api = fakeApi({
    extra: [
      (call) => (call.path === '/api/auth/login' ? json(202, { requiresTwoFactor: true }) : undefined),
      (call) => {
        if (call.path !== '/api/auth/login/two-factor') return undefined;
        const body = call.body as { code: string | null; recoveryCode: string | null };
        if (body.code === '123456' || body.recoveryCode === 'ABCDE-12345') {
          api.state.me = teacher({ platformRoles: ['PlatformOwner'] });
          return json(200, api.state.me, { 'X-XSRF-TOKEN': 'signed-in' });
        }
        return problem(401, 'MFA_CODE_INVALID');
      },
    ],
  });
  return api;
}

async function passwordStep() {
  const user = userEvent.setup();
  await user.type(await screen.findByLabelText('Е-пошта'), 'ana@example.com');
  await user.type(screen.getByLabelText('Лозинка'), PASSWORD);
  await user.click(screen.getByLabelText('Остани најавен на овој уред'));
  await user.click(screen.getByRole('button', { name: 'Најави се' }));
  return user;
}

describe('two-step sign-in', () => {
  it('asks for the authenticator code after the password and then signs in', async () => {
    const api = twoFactorApi();
    const { router } = await renderRoute('/login?returnTo=%2Fstudent');
    const user = await passwordStep();

    expect(await screen.findByRole('heading', { level: 1, name: 'Најава во два чекора' })).toBeInTheDocument();
    const code = screen.getByLabelText('Код од апликацијата');
    expect(code).toHaveAttribute('autocomplete', 'one-time-code');
    expect(code).toHaveAttribute('inputmode', 'numeric');
    expect(code).toHaveFocus();

    await user.type(code, '000000');
    await user.click(screen.getByRole('button', { name: 'Продолжи' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Кодот не е важечки');
    expect(screen.getByLabelText('Код од апликацијата')).toHaveValue('');

    await user.type(screen.getByLabelText('Код од апликацијата'), '123456');
    await user.click(screen.getByRole('button', { name: 'Продолжи' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Мои часови' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/student/');
    // "Remember me" from the password step is sent with the second step.
    expect(api.to('/api/auth/login/two-factor').at(-1)).toMatchObject({ body: { code: '123456', recoveryCode: null, rememberMe: true }, xsrf: 'xsrf' });
  });

  it('accepts a recovery code instead', async () => {
    const api = twoFactorApi();
    await renderRoute('/login?returnTo=%2Fstudent');
    const user = await passwordStep();

    await user.click(await screen.findByRole('button', { name: 'Користи код за враќање' }));
    await user.type(screen.getByLabelText('Код за враќање'), 'ABCDE-12345');
    await user.click(screen.getByRole('button', { name: 'Продолжи' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Мои часови' })).toBeInTheDocument();
    expect(api.to('/api/auth/login/two-factor').at(-1)).toMatchObject({ body: { code: null, recoveryCode: 'ABCDE-12345' } });
  });

  it('goes back to the password step when the sign-in expired', async () => {
    fakeApi({
      extra: [
        (call) => (call.path === '/api/auth/login' ? json(202, { requiresTwoFactor: true }) : undefined),
        (call) => (call.path === '/api/auth/login/two-factor' ? problem(401, 'SIGN_IN_EXPIRED') : undefined),
      ],
    });
    await renderRoute('/login');
    const user = await passwordStep();

    await user.type(await screen.findByLabelText('Код од апликацијата'), '123456');
    await user.click(screen.getByRole('button', { name: 'Продолжи' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Најавата истече');
    expect(screen.getByRole('button', { name: 'Продолжи' })).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Назад кон најавата' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Најава' })).toBeInTheDocument();
    expect(screen.getByLabelText('Лозинка')).toHaveValue('');
  });
});
