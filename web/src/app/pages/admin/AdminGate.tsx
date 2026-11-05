import { useState, type FormEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { encode } from 'uqr';
import { errorMessage } from '../../../api/errors.ts';
import { isPlatformStaff, useEnableAuthenticator, useMfaStatus, useRefreshMfaStatus, useSetupAuthenticator, type AuthenticatorSetup } from '../../../admin/api.ts';
import { useMe, useSignOut } from '../../../auth/session.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';

/**
 * The admin area opens only for platform staff with a session signed in with the authenticator (UX only: the API
 * enforces the same with the platform policies). Staff without the authenticator enroll here first.
 */
export function AdminGate({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const me = useMe();
  const staff = isPlatformStaff(me.data?.platformRoles);
  const mfa = useMfaStatus(staff);
  // Recovery codes are shown once, right after enrollment, until the user confirms they saved them.
  const [freshCodes, setFreshCodes] = useState<string[] | null>(null);
  const refresh = useRefreshMfaStatus();

  if (!staff) {
    return (
      <Card>
        <CardHeader>
          <h1 className="text-2xl font-semibold tracking-tight">{t('admin.title')}</h1>
          <CardDescription>{t('admin.noAccess')}</CardDescription>
        </CardHeader>
      </Card>
    );
  }
  if (freshCodes) {
    return (
      <RecoveryCodes
        codes={freshCodes}
        onDone={() => {
          setFreshCodes(null);
          void refresh();
        }}
      />
    );
  }
  if (mfa.isError) return <Alert variant="error">{errorMessage(mfa.error, t)}</Alert>;
  if (!mfa.data) return <output className="block">{t('common.loading')}</output>;
  if (!mfa.data.enabled) return <Enrollment onEnabled={setFreshCodes} />;
  if (!mfa.data.sessionVerified) return <SignInAgain />;
  return <>{children}</>;
}

/** Set up the authenticator: a new key as QR code and text, confirmed with the first code from the app. */
function Enrollment({ onEnabled }: { onEnabled: (codes: string[]) => void }) {
  const { t } = useTranslation();
  const setup = useSetupAuthenticator();
  const enable = useEnableAuthenticator();
  const [code, setCode] = useState('');

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    try {
      onEnabled(await enable.mutateAsync(code.trim()));
    } catch {
      setCode('');
    }
  };

  return (
    <Card className="max-w-2xl">
      <CardHeader>
        <h1 className="text-2xl font-semibold tracking-tight">{t('admin.mfa.title')}</h1>
        <CardDescription>{t('admin.mfa.lead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {setup.error && <Alert variant="error">{errorMessage(setup.error, t)}</Alert>}
        {!setup.data ? (
          <Button type="button" className="self-start" disabled={setup.isPending} onClick={() => setup.mutate(undefined)}>
            {setup.isPending ? t('common.loading') : t('admin.mfa.start')}
          </Button>
        ) : (
          <>
            <KeyDisplay setup={setup.data} />
            <form noValidate onSubmit={(e) => void submit(e)} className="flex flex-col gap-3">
              {enable.error && <Alert variant="error">{errorMessage(enable.error, t)}</Alert>}
              <FormField
                label={t('admin.mfa.code')}
                hint={t('admin.mfa.codeHint')}
                autoComplete="one-time-code"
                inputMode="numeric"
                maxLength={20}
                value={code}
                onChange={(e) => setCode(e.target.value)}
              />
              <Button type="submit" className="self-start" disabled={enable.isPending || !code.trim()}>
                {enable.isPending ? t('common.loading') : t('admin.mfa.confirm')}
              </Button>
            </form>
          </>
        )}
      </CardContent>
    </Card>
  );
}

export function KeyDisplay({ setup }: { setup: AuthenticatorSetup }) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-4 sm:flex-row sm:items-start">
      <QrCode value={setup.authenticatorUri} label={t('admin.mfa.qr')} />
      <div className="flex min-w-0 flex-col gap-2 text-sm">
        <p>{t('admin.mfa.scan')}</p>
        <p>{t('admin.mfa.orType')}</p>
        <code aria-label={t('admin.mfa.key')} className="break-all rounded-md bg-muted px-3 py-2 font-mono text-base tracking-wider">
          {setup.sharedKey}
        </code>
        <a className="text-primary underline-offset-4 hover:underline sm:hidden" href={setup.authenticatorUri}>
          {t('admin.mfa.openApp')}
        </a>
      </div>
    </div>
  );
}

/**
 * QR code of the otpauth:// link, always dark on light (scanners need the contrast in both themes). Hidden from
 * assistive technology: the same key is shown as text next to it.
 */
function QrCode({ value, label }: { value: string; label: string }) {
  const { data, size } = encode(value, { ecc: 'M', border: 2 });
  let path = '';
  data.forEach((row, y) =>
    row.forEach((dark, x) => {
      if (dark) path += `M${x} ${y}h1v1h-1z`;
    }),
  );
  return (
    <figure className="shrink-0">
      <figcaption className="sr-only">{label}</figcaption>
      <svg aria-hidden="true" viewBox={`0 0 ${size} ${size}`} shapeRendering="crispEdges" className="h-48 w-48 shrink-0 rounded-md border border-border">
        <rect width={size} height={size} fill="#ffffff" />
        <path d={path} fill="#000000" />
      </svg>
    </figure>
  );
}

/** The new recovery codes, shown once. */
export function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  const { t } = useTranslation();
  return (
    <Card className="max-w-2xl">
      <CardHeader>
        <CardTitle className="text-xl">{t('admin.mfa.codesTitle')}</CardTitle>
        <CardDescription>{t('admin.mfa.codesLead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <ul aria-label={t('admin.mfa.codesTitle')} className="grid grid-cols-2 gap-2 font-mono text-sm">
          {codes.map((code) => (
            <li key={code} className="rounded-md bg-muted px-3 py-2">
              {code}
            </li>
          ))}
        </ul>
        <Button type="button" className="self-start" onClick={onDone}>
          {t('admin.mfa.codesDone')}
        </Button>
      </CardContent>
    </Card>
  );
}

/** Enrolled, but this session started with the password only: sign in again with the code. */
function SignInAgain() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const signOut = useSignOut();
  return (
    <Card className="max-w-2xl">
      <CardHeader>
        <h1 className="text-2xl font-semibold tracking-tight">{t('admin.mfa.againTitle')}</h1>
        <CardDescription>{t('admin.mfa.againLead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {signOut.error && <Alert variant="error">{errorMessage(signOut.error, t)}</Alert>}
        <Button
          type="button"
          className="self-start"
          disabled={signOut.isPending}
          onClick={() => signOut.mutate(undefined, { onSuccess: () => void navigate('/login?returnTo=%2Fadmin', { replace: true }) })}
        >
          {t('admin.mfa.again')}
        </Button>
      </CardContent>
    </Card>
  );
}
