import { expect, request as playwrightRequest, test } from '@playwright/test';
import { NEW_PASSWORD, openEmailLink, register, signIn, teacherViaApi, text, uniqueEmail, useLanguage, type Language } from './support.ts';

const workspaceName: Record<Language, string> = { mk: 'Мој работен простор', en: 'My workspace' };

for (const language of ['mk', 'en'] as const) {
  test.describe(`${language}`, () => {
    test.beforeEach(async ({ page }) => useLanguage(page, language));

    test('a teacher signs up, confirms the email, signs in to the workspace and signs out', async ({ page }) => {
      const email = uniqueEmail(`signup-${language}`);
      await register(page, language, email);

      await openEmailLink(page, language, email, '/verify-email');
      await expect(page.getByText(text(language, 'auth.verify.done'))).toBeVisible();
      expect(page.url()).not.toContain('token='); // the token is removed from the address bar

      await page.getByRole('link', { name: text(language, 'auth.verify.continue') }).click();
      await expect(page).toHaveURL(/\/login\?returnTo=%2Fapp%2F$/);
      await signIn(page, language, email);

      await expect(page.getByRole('heading', { level: 1, name: workspaceName[language] })).toBeVisible();
      await expect(page).toHaveURL(/\/app\/w\/[0-9a-f-]{36}$/);
      await expect(page.getByText(text(language, 'auth.unconfirmed.message'))).toHaveCount(0); // confirmed

      await page.getByRole('button', { name: text(language, 'auth.logout') }).click();
      await expect(page.getByRole('heading', { level: 1, name: text(language, 'auth.login.title') })).toBeVisible();
      await page.goto('/app');
      await expect(page).toHaveURL(/\/login\?returnTo=/); // the session really ended
    });

    test('a teacher resets a forgotten password and signs in with the new one', async ({ page }) => {
      const email = uniqueEmail(`reset-${language}`);
      await register(page, language, email);

      await page.goto('/forgot-password');
      await page.getByLabel(text(language, 'auth.fields.email')).fill(email);
      await page.getByRole('button', { name: text(language, 'auth.forgot.submit') }).click();
      await expect(page.getByText(text(language, 'auth.forgot.sent'))).toBeVisible();

      await openEmailLink(page, language, email, '/reset-password');
      await page.getByLabel(text(language, 'auth.fields.newPassword')).fill(NEW_PASSWORD);
      await page.getByLabel(text(language, 'auth.fields.confirmPassword')).fill(NEW_PASSWORD);
      await page.getByRole('button', { name: text(language, 'auth.reset.submit') }).click();
      await expect(page.getByText(text(language, 'auth.reset.done'))).toBeVisible();

      await page.getByRole('link', { name: text(language, 'auth.login.submit') }).click();
      await signIn(page, language, email); // the old password no longer works
      await expect(page.getByRole('alert')).toHaveText(text(language, 'errors.codes.INVALID_CREDENTIALS'));
      await signIn(page, language, email, NEW_PASSWORD);
      await expect(page.getByRole('heading', { level: 1, name: workspaceName[language] })).toBeVisible();
      await expect(page.getByText(text(language, 'auth.unconfirmed.message'))).toBeVisible(); // never confirmed
    });

    test("a second teacher opening the first teacher's workspace URL sees 'not found'", async ({ page, baseURL }) => {
      const owner = await playwrightRequest.newContext({ baseURL });
      const { workspaceId } = await teacherViaApi(owner, uniqueEmail(`owner-${language}`));
      await owner.dispose();

      const intruder = uniqueEmail(`intruder-${language}`);
      await register(page, language, intruder);
      await page.goto(`/app/w/${workspaceId}`);
      await signIn(page, language, intruder);

      await expect(page).toHaveURL(new RegExp(`/app/w/${workspaceId}$`));
      await expect(page.getByRole('heading', { level: 1, name: text(language, 'notFound.title') })).toBeVisible();
      await expect(page.getByText(workspaceName.mk)).toHaveCount(0);
    });
  });
}
