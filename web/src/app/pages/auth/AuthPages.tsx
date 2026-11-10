import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router';
import { api } from '../../../api/client.ts';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import {
  emailSchema,
  loginSchema,
  PASSWORD_MIN,
  readFragment,
  registerSchema,
  resetSchema,
  type EmailForm,
  type LoginForm,
  type RegisterForm,
  type ResetForm,
} from '../../../auth/schemas.ts';
import { meQueryKey, safeReturnTo, useMe, useSignIn, useSignInTwoFactor } from '../../../auth/session.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button, buttonVariants } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { isLanguage } from '../../../i18n/index.ts';
import { applyServerFieldErrors } from './serverErrors.ts';

const linkClass = 'text-primary underline-offset-4 hover:underline';

function AuthCard({ title, description, children }: { title: string; description?: string; children: ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        {description && <CardDescription>{description}</CardDescription>}
      </CardHeader>
      <CardContent className="flex flex-col gap-4">{children}</CardContent>
    </Card>
  );
}

/** Removes tokens from the address bar and history once they have been read. */
function clearFragment() {
  if (typeof window !== 'undefined' && window.location.hash) {
    window.history.replaceState(window.history.state, '', window.location.pathname + window.location.search);
  }
}

/** Link data from the URL fragment, read once (tokens travel in the fragment, never to servers or logs). */
function useFragmentOnce<K extends string>(keys: readonly K[]): Record<K, string | null> {
  const [values] = useState(() => {
    const fragment = readFragment(typeof window === 'undefined' ? '' : window.location.hash);
    return Object.fromEntries(keys.map((key) => [key, fragment.get(key)])) as Record<K, string | null>;
  });
  useEffect(clearFragment, []);
  return values;
}

export function LoginPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const returnTo = safeReturnTo(params.get('returnTo'));
  const me = useMe();
  const signIn = useSignIn();
  const [twoFactor, setTwoFactor] = useState<{ rememberMe: boolean } | null>(null);
  const form = useForm<LoginForm>({ resolver: zodResolver(loginSchema), defaultValues: { email: '', password: '', rememberMe: false } });
  const { errors, isSubmitting } = form.formState;

  if (me.data && !signIn.isSuccess) return <Navigate to={returnTo} replace />;

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const result = await signIn.mutateAsync(values);
      if (result.requiresTwoFactor) {
        setTwoFactor({ rememberMe: values.rememberMe });
        return;
      }
      void navigate(returnTo, { replace: true });
    } catch {
      form.resetField('password');
    }
  });

  if (twoFactor) {
    return (
      <TwoFactorStep
        rememberMe={twoFactor.rememberMe}
        onSignedIn={() => void navigate(returnTo, { replace: true })}
        onBack={() => {
          setTwoFactor(null);
          signIn.reset();
          form.resetField('password');
        }}
      />
    );
  }

  return (
    <AuthCard title={t('auth.login.title')} description={t('auth.login.lead')}>
      {signIn.error && <Alert variant="error">{errorMessage(signIn.error, t)}</Alert>}
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        <FormField
          label={t('auth.fields.email')}
          type="email"
          autoComplete="username"
          inputMode="email"
          error={errors.email?.message && t(errors.email.message)}
          {...form.register('email')}
        />
        <FormField
          label={t('auth.fields.password')}
          type="password"
          autoComplete="current-password"
          error={errors.password?.message && t(errors.password.message)}
          {...form.register('password')}
        />
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" className="h-4 w-4" {...form.register('rememberMe')} />
          {t('auth.fields.rememberMe')}
        </label>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? t('common.loading') : t('auth.login.submit')}
        </Button>
      </form>
      <div className="flex flex-col gap-2 text-sm">
        <Link className={linkClass} to="/forgot-password">
          {t('auth.login.forgot')}
        </Link>
        <span>
          {t('auth.login.noAccount')}{' '}
          <Link className={linkClass} to="/register">
            {t('nav.register')}
          </Link>
        </span>
      </div>
    </AuthCard>
  );
}

/** The second sign-in step for accounts with the authenticator: a 6-digit code, or one of the recovery codes. */
function TwoFactorStep({ rememberMe, onSignedIn, onBack }: { rememberMe: boolean; onSignedIn: () => void; onBack: () => void }) {
  const { t } = useTranslation();
  const signIn = useSignInTwoFactor();
  const [recovery, setRecovery] = useState(false);
  const [code, setCode] = useState('');
  const [missing, setMissing] = useState(false);
  const fieldId = useId();
  const focusField = () => document.getElementById(fieldId)?.focus();
  // The code field takes the focus when the step opens (and after switching the kind of code, below).
  useEffect(() => {
    document.getElementById(fieldId)?.focus();
  }, [fieldId]);

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const value = code.trim();
    if (!value) {
      setMissing(true);
      focusField();
      return;
    }
    try {
      await signIn.mutateAsync(recovery ? { recoveryCode: value, rememberMe } : { code: value, rememberMe });
      onSignedIn();
    } catch {
      setCode('');
      focusField();
    }
  };

  // The two-factor cookie lasts five minutes: after that, only the password step can start over.
  const expired = signIn.error instanceof ApiError && signIn.error.code === 'SIGN_IN_EXPIRED';

  return (
    <AuthCard title={t('auth.twoFactor.title')} description={recovery ? t('auth.twoFactor.recoveryLead') : t('auth.twoFactor.lead')}>
      {signIn.error && <Alert variant="error">{errorMessage(signIn.error, t)}</Alert>}
      <form noValidate onSubmit={(e) => void onSubmit(e)} className="flex flex-col gap-4">
        <FormField
          id={fieldId}
          key={recovery ? 'recovery' : 'code'}
          label={recovery ? t('auth.twoFactor.recoveryCode') : t('auth.twoFactor.code')}
          autoComplete={recovery ? 'off' : 'one-time-code'}
          inputMode={recovery ? 'text' : 'numeric'}
          maxLength={recovery ? 40 : 20}
          value={code}
          error={missing && !code.trim() ? t('auth.twoFactor.required') : undefined}
          onChange={(e) => setCode(e.target.value)}
        />
        <Button type="submit" disabled={signIn.isPending || expired}>
          {signIn.isPending ? t('common.loading') : t('auth.twoFactor.submit')}
        </Button>
      </form>
      <div className="flex flex-col items-start gap-2 text-sm">
        {!expired && (
          <button
            type="button"
            className={linkClass}
            onClick={() => {
              setRecovery(!recovery);
              setCode('');
              setMissing(false);
              signIn.reset();
              requestAnimationFrame(focusField);
            }}
          >
            {recovery ? t('auth.twoFactor.useCode') : t('auth.twoFactor.useRecovery')}
          </button>
        )}
        <button type="button" className={linkClass} onClick={onBack}>
          {t('auth.twoFactor.back')}
        </button>
      </div>
    </AuthCard>
  );
}

export function RegisterPage() {
  const { t, i18n } = useTranslation();
  const me = useMe();
  const [sentTo, setSentTo] = useState<string | null>(null);
  const form = useForm<RegisterForm>({ resolver: zodResolver(registerSchema), defaultValues: { email: '', password: '', confirmPassword: '' } });
  const { errors, isSubmitting } = form.formState;
  const register = useMutation({
    mutationFn: async (values: RegisterForm) => {
      const locale = isLanguage(i18n.resolvedLanguage) ? i18n.resolvedLanguage : 'mk';
      await api.client.POST('/api/auth/register', { body: { email: values.email, password: values.password, locale } });
      return values.email;
    },
    onSuccess: setSentTo,
    onError: (error) =>
      applyServerFieldErrors(error, form.setError, {
        email: { field: 'email', message: 'auth.validation.emailInvalid' },
        password: { field: 'password', message: 'auth.validation.passwordRejected' },
      }),
  });

  if (me.data) return <Navigate to="/app/" replace />;

  if (sentTo) {
    return (
      <AuthCard title={t('auth.register.sentTitle')}>
        <Alert variant="success">{t('auth.register.sent', { email: sentTo })}</Alert>
        <Link className={buttonVariants({ variant: 'outline' })} to="/login">
          {t('auth.login.submit')}
        </Link>
      </AuthCard>
    );
  }

  const fieldsMarked = register.error instanceof ApiError && register.error.code === 'VALIDATION_FAILED';
  return (
    <AuthCard title={t('auth.register.title')} description={t('auth.register.lead')}>
      {register.error && !fieldsMarked && <Alert variant="error">{errorMessage(register.error, t)}</Alert>}
      <form noValidate onSubmit={form.handleSubmit((values) => register.mutateAsync(values).catch(() => undefined))} className="flex flex-col gap-4">
        <FormField
          label={t('auth.fields.email')}
          type="email"
          autoComplete="email"
          inputMode="email"
          error={errors.email?.message && t(errors.email.message)}
          {...form.register('email')}
        />
        <FormField
          label={t('auth.fields.password')}
          type="password"
          autoComplete="new-password"
          hint={t('auth.fields.passwordHint', { min: PASSWORD_MIN })}
          error={errors.password?.message && t(errors.password.message, { min: PASSWORD_MIN })}
          {...form.register('password')}
        />
        <FormField
          label={t('auth.fields.confirmPassword')}
          type="password"
          autoComplete="new-password"
          error={errors.confirmPassword?.message && t(errors.confirmPassword.message)}
          {...form.register('confirmPassword')}
        />
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? t('common.loading') : t('auth.register.submit')}
        </Button>
      </form>
      <p className="text-sm">
        {t('auth.register.haveAccount')}{' '}
        <Link className={linkClass} to="/login">
          {t('nav.signIn')}
        </Link>
      </p>
    </AuthCard>
  );
}

export function VerifyEmailPage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const link = useFragmentOnce(['userId', 'token'] as const);
  const started = useRef(false);
  const confirm = useMutation({
    mutationFn: async () => {
      if (!link.userId || !link.token) throw new ApiError(400, 'INVALID_TOKEN', 'missing link data');
      await api.client.POST('/api/auth/confirm-email', { body: { userId: link.userId, token: link.token } });
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: meQueryKey }),
  });
  const { mutate } = confirm;

  useEffect(() => {
    // Once per page load (React StrictMode runs effects twice in development).
    if (started.current) return;
    started.current = true;
    mutate();
  }, [mutate]);

  const alreadyConfirmed = confirm.error instanceof ApiError && confirm.error.code === 'EMAIL_ALREADY_CONFIRMED';
  const done = confirm.isSuccess || alreadyConfirmed;
  return (
    <AuthCard title={t('auth.verify.title')}>
      {(confirm.isPending || confirm.isIdle) && <output className="block">{t('common.loading')}</output>}
      {confirm.isSuccess && <Alert variant="success">{t('auth.verify.done')}</Alert>}
      {alreadyConfirmed && <Alert variant="info">{t('errors.codes.EMAIL_ALREADY_CONFIRMED')}</Alert>}
      {confirm.isError && !alreadyConfirmed && <Alert variant="error">{errorMessage(confirm.error, t)}</Alert>}
      {(done || confirm.isError) && (
        <Link className={buttonVariants()} to="/app/">
          {t('auth.verify.continue')}
        </Link>
      )}
    </AuthCard>
  );
}

export function ForgotPasswordPage() {
  const { t } = useTranslation();
  const [sent, setSent] = useState(false);
  const form = useForm<EmailForm>({ resolver: zodResolver(emailSchema), defaultValues: { email: '' } });
  const { errors, isSubmitting } = form.formState;
  const request = useMutation({
    mutationFn: async (values: EmailForm) => {
      await api.client.POST('/api/auth/forgot-password', { body: { email: values.email } });
    },
    onSuccess: () => setSent(true),
  });

  return (
    <AuthCard title={t('auth.forgot.title')} description={t('auth.forgot.lead')}>
      {sent ? (
        <Alert variant="success">{t('auth.forgot.sent')}</Alert>
      ) : (
        <>
          {request.error && <Alert variant="error">{errorMessage(request.error, t)}</Alert>}
          <form noValidate onSubmit={form.handleSubmit((values) => request.mutateAsync(values).catch(() => undefined))} className="flex flex-col gap-4">
            <FormField
              label={t('auth.fields.email')}
              type="email"
              autoComplete="email"
              inputMode="email"
              error={errors.email?.message && t(errors.email.message)}
              {...form.register('email')}
            />
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? t('common.loading') : t('auth.forgot.submit')}
            </Button>
          </form>
        </>
      )}
      <Link className={`text-sm ${linkClass}`} to="/login">
        {t('auth.backToLogin')}
      </Link>
    </AuthCard>
  );
}

export function ResetPasswordPage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const link = useFragmentOnce(['email', 'token'] as const);
  const form = useForm<ResetForm>({ resolver: zodResolver(resetSchema), defaultValues: { password: '', confirmPassword: '' } });
  const { errors, isSubmitting } = form.formState;
  const reset = useMutation({
    mutationFn: async (values: ResetForm) => {
      if (!link.email || !link.token) throw new ApiError(400, 'INVALID_TOKEN', 'missing link data');
      await api.client.POST('/api/auth/reset-password', { body: { email: link.email, token: link.token, newPassword: values.password } });
    },
    // The reset ended every session of the reset account (security stamp). This tab may be signed in as that
    // account or as another one, so nothing cached is trusted: the next screen asks the API who is signed in.
    onSuccess: () => {
      api.resetToken();
      queryClient.clear();
    },
    onError: (error) => applyServerFieldErrors(error, form.setError, { newPassword: { field: 'password', message: 'auth.validation.passwordRejected' } }),
  });

  if (reset.isSuccess) {
    return (
      <AuthCard title={t('auth.reset.title')}>
        <Alert variant="success">{t('auth.reset.done')}</Alert>
        <Link className={buttonVariants()} to="/login">
          {t('auth.login.submit')}
        </Link>
      </AuthCard>
    );
  }

  const fieldsMarked = reset.error instanceof ApiError && reset.error.code === 'VALIDATION_FAILED';
  return (
    <AuthCard title={t('auth.reset.title')} description={link.email ?? undefined}>
      {reset.error && !fieldsMarked && <Alert variant="error">{errorMessage(reset.error, t)}</Alert>}
      <form noValidate onSubmit={form.handleSubmit((values) => reset.mutateAsync(values).catch(() => undefined))} className="flex flex-col gap-4">
        {/* Lets password managers attach the new password to the right account. */}
        <input type="email" name="username" autoComplete="username" value={link.email ?? ''} readOnly hidden />
        <FormField
          label={t('auth.fields.newPassword')}
          type="password"
          autoComplete="new-password"
          hint={t('auth.fields.passwordHint', { min: PASSWORD_MIN })}
          error={errors.password?.message && t(errors.password.message, { min: PASSWORD_MIN })}
          {...form.register('password')}
        />
        <FormField
          label={t('auth.fields.confirmPassword')}
          type="password"
          autoComplete="new-password"
          error={errors.confirmPassword?.message && t(errors.confirmPassword.message)}
          {...form.register('confirmPassword')}
        />
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? t('common.loading') : t('auth.reset.submit')}
        </Button>
      </form>
    </AuthCard>
  );
}
