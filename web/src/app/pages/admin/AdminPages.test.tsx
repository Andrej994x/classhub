import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, problem, teacher, WORKSPACE_ID, type FakeCall } from '../../../test/fakeApi.ts';
import { renderRoute } from '../../../test/render.tsx';

const OWNER = teacher({ id: '22222222-2222-4222-8222-222222222222', email: 'owner@example.com', platformRoles: ['PlatformOwner'] });
const TARGET_ID = '55555555-5555-4555-8555-555555555555';

const summary = (overrides: Record<string, unknown> = {}) => ({
  id: TARGET_ID,
  email: 'marija@example.com',
  emailConfirmed: true,
  status: 'Active',
  platformRoles: [] as string[],
  mfaEnabled: false,
  createdAtUtc: '2026-11-01T09:00:00Z',
  lastLoginAtUtc: '2026-11-04T18:00:00Z',
  workspace: { id: WORKSPACE_ID, name: 'Часови со Марија', createdAtUtc: '2026-11-01T09:00:00Z', siteSlug: 'marija', sitePublished: true },
  ...overrides,
});

const auditEntry = (action: string, details: string | null = null) => ({
  id: `${action}-1`,
  occurredAtUtc: '2026-11-05T10:00:00Z',
  actorUserId: OWNER.id,
  actorEmail: OWNER.email,
  action,
  targetType: 'User',
  targetId: TARGET_ID,
  workspaceId: null,
  detailsJson: details,
});

interface AdminState {
  mfa: { enabled: boolean; sessionVerified: boolean; required: boolean; recoveryCodesLeft: number };
  user: ReturnType<typeof summary>;
  history: ReturnType<typeof auditEntry>[];
}

function adminApi(me = OWNER, mfa: AdminState['mfa'] = { enabled: true, sessionVerified: true, required: true, recoveryCodesLeft: 10 }) {
  const state: AdminState = { mfa, user: summary(), history: [] };
  const detail = () => ({ user: state.user, lockedOutUntilUtc: null, history: state.history });
  const handler = (call: FakeCall) => {
    if (call.path === '/api/auth/mfa') return json(200, state.mfa);
    if (call.path === '/api/auth/mfa/authenticator') {
      const proof = call.body as { code: string | null; recoveryCode: string | null } | null;
      if (state.mfa.enabled && proof?.code !== '222333' && proof?.recoveryCode !== 'CCCCC-33333') return problem(400, 'MFA_CODE_INVALID');
      return json(200, { sharedKey: 'abcd efgh ijkl mnop', authenticatorUri: 'otpauth://totp/ClassHub%3Aowner%40example.com?secret=ABCDEFGHIJKLMNOP&issuer=ClassHub&digits=6' });
    }
    if (call.path === '/api/auth/mfa/authenticator/enable') {
      if ((call.body as { code: string }).code !== '123456') return problem(400, 'MFA_CODE_INVALID');
      state.mfa = { ...state.mfa, enabled: true, sessionVerified: true, recoveryCodesLeft: 10 };
      return json(200, { recoveryCodes: ['AAAAA-11111', 'BBBBB-22222'] });
    }
    if (call.path === '/api/admin/overview') {
      return json(200, { users: 42, suspendedUsers: 2, teachers: 40, activeTeachers: 25, newUsersLast7Days: 5, newUsersLast30Days: 12, publishedSites: 18, platformStaff: 2 });
    }
    if (call.path === '/api/admin/users') return json(200, { items: [state.user], total: 1, page: 1, pageSize: 20 });
    if (call.path === `/api/admin/users/${TARGET_ID}` && call.method === 'GET') return json(200, detail());
    if (call.path === `/api/admin/users/${TARGET_ID}/suspend`) {
      state.user = { ...state.user, status: 'Suspended' };
      state.history = [auditEntry('user.suspended', JSON.stringify({ after: { reason: (call.body as { reason: string }).reason } }))];
      return json(200, detail());
    }
    if (call.path === `/api/admin/users/${TARGET_ID}/admin-role/grant`) {
      if ((call.body as { code: string }).code !== '654321') return problem(400, 'MFA_CODE_INVALID');
      state.user = { ...state.user, platformRoles: ['PlatformAdmin'] };
      return json(200, detail());
    }
    if (call.path === '/api/admin/audit') {
      return json(200, { items: [auditEntry('user.suspended', '{"after":{"reason":"Spam"}}'), { ...auditEntry('workspace.settings.updated'), targetType: 'Workspace', targetId: WORKSPACE_ID, workspaceId: WORKSPACE_ID }], total: 2, page: 1, pageSize: 20 });
    }
    return undefined;
  };
  const api = fakeApi({ me, extra: [handler] });
  return { api, state };
}

describe('admin area', () => {
  it('is closed to accounts without a platform role', async () => {
    const { api } = adminApi(teacher());
    await renderRoute('/admin');

    expect(await screen.findByText('Овој дел е само за администраторите на платформата.')).toBeInTheDocument();
    expect(api.to('/api/admin/overview')).toHaveLength(0);
  });

  it('enrolls the authenticator, shows the recovery codes once and then opens the overview', async () => {
    const user = userEvent.setup();
    const { api } = adminApi(OWNER, { enabled: false, sessionVerified: false, required: true, recoveryCodesLeft: 0 });
    await renderRoute('/admin');

    await user.click(await screen.findByRole('button', { name: 'Создај клуч' }));
    expect(await screen.findByLabelText('Клуч за автентикација')).toHaveTextContent('abcd efgh ijkl mnop');
    expect(screen.getByText('QR код за апликацијата за автентикација')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Отвори во апликацијата за автентикација' })).toHaveAttribute('href', expect.stringMatching(/^otpauth:\/\/totp\//));

    await user.type(screen.getByLabelText('Код од апликацијата'), '000000');
    await user.click(screen.getByRole('button', { name: 'Потврди и вклучи' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Кодот не е важечки');

    await user.type(screen.getByLabelText('Код од апликацијата'), '123456');
    await user.click(screen.getByRole('button', { name: 'Потврди и вклучи' }));
    const codes = await screen.findByRole('list', { name: 'Вашите кодови за враќање' });
    expect(within(codes).getAllByRole('listitem').map((i) => i.textContent)).toEqual(['AAAAA-11111', 'BBBBB-22222']);
    expect(api.to('/api/admin/overview')).toHaveLength(0);

    await user.click(screen.getByRole('button', { name: 'Ги зачував кодовите' }));
    const tiles = await screen.findByRole('list', { name: 'Преглед на платформата' });
    expect(tiles).toHaveTextContent('Корисници42');
    expect(tiles).toHaveTextContent('2 суспендирани');
    expect(tiles).toHaveTextContent('Активни наставници25');
    expect(screen.queryByText('AAAAA-11111')).not.toBeInTheDocument();
  });

  it('asks a password-only session to sign in again with the code', async () => {
    const user = userEvent.setup();
    const { api } = adminApi(OWNER, { enabled: true, sessionVerified: false, required: true, recoveryCodesLeft: 10 });
    const { router } = await renderRoute('/admin');

    await user.click(await screen.findByRole('button', { name: 'Одјави се и најави се повторно' }));

    await expect.poll(() => router.state.location.pathname).toBe('/login');
    expect(new URLSearchParams(router.state.location.search).get('returnTo')).toBe('/admin');
    expect(api.to('/api/auth/logout')).toHaveLength(1);
  });

  it('suspends a user with a reason and shows the audit history', async () => {
    const user = userEvent.setup();
    const { api } = adminApi();
    await renderRoute('/admin/users');

    await user.click(await screen.findByRole('link', { name: /marija@example\.com/ }));
    expect(await screen.findByRole('heading', { level: 1, name: /marija@example\.com/ })).toBeInTheDocument();
    expect(screen.getByText('/marija · објавена')).toBeInTheDocument();

    const suspend = screen.getByRole('button', { name: 'Суспендирај' });
    expect(suspend).toBeDisabled();
    await user.type(screen.getByLabelText('Причина'), 'Spam on the public page');
    await user.click(suspend);

    expect(await screen.findByRole('button', { name: 'Реактивирај' })).toBeInTheDocument();
    expect(api.to(`/api/admin/users/${TARGET_ID}/suspend`)[0]).toMatchObject({ body: { reason: 'Spam on the public page' }, xsrf: 'xsrf' });
    const history = screen.getByRole('list', { name: 'Записи' });
    expect(history).toHaveTextContent('user.suspended');
    expect(history).toHaveTextContent('owner@example.com');
  });

  it('lets the owner grant Platform Admin with a fresh code', async () => {
    const user = userEvent.setup();
    const { api } = adminApi();
    await renderRoute(`/admin/users/${TARGET_ID}`);

    await user.type(await screen.findByLabelText('Вашиот код од апликацијата'), '111111');
    await user.click(screen.getByRole('button', { name: 'Додели' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Кодот не е важечки');

    await user.type(screen.getByLabelText('Вашиот код од апликацијата'), '654321');
    await user.click(screen.getByRole('button', { name: 'Додели' }));
    expect(await screen.findByRole('button', { name: 'Одземи' })).toBeInTheDocument();
    expect(api.to(`/api/admin/users/${TARGET_ID}/admin-role/grant`).at(-1)).toMatchObject({ body: { code: '654321' } });
  });

  it('does not offer role management or admin suspension to a Platform Admin', async () => {
    const admin = teacher({ id: '66666666-6666-4666-8666-666666666666', email: 'admin@example.com', platformRoles: ['PlatformAdmin'] });
    const { state } = adminApi(admin);
    state.user = summary({ platformRoles: ['PlatformAdmin'] });
    await renderRoute(`/admin/users/${TARGET_ID}`);

    expect(await screen.findByRole('heading', { level: 1, name: /marija@example\.com/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Додели' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Одземи' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Суспендирај' })).not.toBeInTheDocument();
  });

  it('replaces the authenticator only with proof of the current factor', async () => {
    const user = userEvent.setup();
    const { api } = adminApi();
    await renderRoute('/admin/security');

    const proof = await screen.findByLabelText('Тековен код или код за враќање');
    await user.type(proof, '999 999');
    await user.click(screen.getByRole('button', { name: 'Создај нов клуч' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Кодот не е важечки');
    expect(api.to('/api/auth/mfa/authenticator').at(-1)).toMatchObject({ body: { code: '999 999', recoveryCode: null } });

    await user.type(screen.getByLabelText('Тековен код или код за враќање'), 'ccccc-33333');
    await user.click(screen.getByRole('button', { name: 'Создај нов клуч' }));
    expect(api.to('/api/auth/mfa/authenticator').at(-1)).toMatchObject({ body: { code: null, recoveryCode: 'ccccc-33333' } });
  });

  it('filters the audit log through the URL and shows details only where the API sent them', async () => {
    const user = userEvent.setup();
    const { api } = adminApi();
    await renderRoute(`/admin/audit?userId=${TARGET_ID}`);

    const entries = await screen.findByRole('list', { name: 'Записи' });
    expect(within(entries).getAllByRole('listitem')).toHaveLength(2);
    expect(within(entries).getAllByText('Детали')).toHaveLength(1);
    expect(api.to('/api/admin/audit')[0]!.search).toContain(`userId=${TARGET_ID}`);

    await user.type(screen.getByLabelText('Акцијата почнува со'), 'user.');
    await expect.poll(() => api.to('/api/admin/audit').some((c) => c.search.includes('action=user.'))).toBe(true);
  });
});
