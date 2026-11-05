import { expect, test } from '@playwright/test';
import { grantPlatformOwner, PASSWORD, signIn, teacherViaApi, text, totp, uniqueEmail, useLanguage, xsrf } from './support.ts';

/**
 * R1-P9: the Platform Owner (bootstrapped with the CLI) enrolls the authenticator, signs in again with a TOTP code,
 * suspends a teacher with a reason and finds the entry in the audit log; the teacher's session ends and sign-in fails.
 */
test('the platform owner signs in with the authenticator and suspends a teacher', async ({ page, request, playwright, baseURL }) => {
  test.setTimeout(120_000);
  const teacherEmail = uniqueEmail('target');
  const ownerEmail = uniqueEmail('owner');
  await teacherViaApi(request, teacherEmail);

  const ownerSetup = await playwright.request.newContext({ baseURL });
  await teacherViaApi(ownerSetup, ownerEmail);
  await ownerSetup.dispose();
  await grantPlatformOwner(ownerEmail);

  await useLanguage(page, 'mk');
  await page.goto('/admin');
  await expect(page.getByRole('heading', { level: 1, name: text('mk', 'auth.login.title') })).toBeVisible();
  await signIn(page, 'mk', ownerEmail);

  // First visit: enroll the authenticator (the test reads the key like a person typing it into the app).
  await expect(page.getByRole('heading', { level: 1, name: text('mk', 'admin.mfa.title') })).toBeVisible();
  await page.getByRole('button', { name: text('mk', 'admin.mfa.start') }).click();
  const key = (await page.getByLabel(text('mk', 'admin.mfa.key')).textContent())!.trim();
  await page.getByLabel(text('mk', 'admin.mfa.code')).fill(totp(key));
  await page.getByRole('button', { name: text('mk', 'admin.mfa.confirm') }).click();
  await expect(page.getByRole('list', { name: text('mk', 'admin.mfa.codesTitle') }).getByRole('listitem')).toHaveCount(10);
  await page.getByRole('button', { name: text('mk', 'admin.mfa.codesDone') }).click();
  await expect(page.getByRole('heading', { level: 1, name: text('mk', 'admin.overview.title') })).toBeVisible();

  // Sign out and back in: password, then the code from the app.
  await page.getByRole('button', { name: text('mk', 'auth.logout') }).click();
  await expect(page.getByRole('heading', { level: 1, name: text('mk', 'auth.login.title') })).toBeVisible();
  await page.goto('/admin');
  await signIn(page, 'mk', ownerEmail);
  await expect(page.getByRole('heading', { level: 1, name: text('mk', 'auth.twoFactor.title') })).toBeVisible();
  await page.getByLabel(text('mk', 'auth.twoFactor.code')).fill(totp(key));
  await page.getByRole('button', { name: text('mk', 'auth.twoFactor.submit') }).click();
  await expect(page.getByRole('heading', { level: 1, name: text('mk', 'admin.overview.title') })).toBeVisible();

  // Find the teacher and suspend the account with a reason.
  await page.goto('/admin/users');
  await page.getByLabel(text('mk', 'admin.users.search')).fill(teacherEmail);
  await page.getByRole('link', { name: new RegExp(teacherEmail.replace(/[.]/g, '\\.')) }).click();
  await expect(page.getByRole('heading', { level: 1, name: new RegExp(teacherEmail.replace(/[.]/g, '\\.')) })).toBeVisible();
  await page.getByLabel(text('mk', 'admin.user.reason')).fill('Spam on the public page');
  await page.getByRole('button', { name: text('mk', 'admin.user.suspend') }).click();
  await expect(page.getByRole('button', { name: text('mk', 'admin.user.reactivate') })).toBeVisible();
  await expect(page.getByRole('heading', { level: 1 })).toContainText(text('mk', 'admin.statuses.Suspended'));

  // The teacher's session ended and signing in is refused.
  expect((await request.get('/api/me/workspaces')).status()).toBe(401);
  const login = await request.post('/api/auth/login', { data: { email: teacherEmail, password: PASSWORD, rememberMe: false }, headers: { 'X-XSRF-TOKEN': await xsrf(request) } });
  expect(login.status()).toBe(403);
  expect(((await login.json()) as { code: string }).code).toBe('ACCOUNT_SUSPENDED');

  // The audit log shows who suspended whom, with the reason.
  await page.goto('/admin/audit?action=user.suspended');
  const entries = page.getByRole('list', { name: text('mk', 'admin.audit.entries') });
  const entry = entries.getByRole('listitem').filter({ hasText: ownerEmail }).first();
  await expect(entry).toContainText('user.suspended');
  await entry.getByText(text('mk', 'admin.audit.details')).click();
  await expect(entry).toContainText('Spam on the public page');
});
