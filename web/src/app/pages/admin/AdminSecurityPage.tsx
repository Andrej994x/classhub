import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '../../../api/errors.ts';
import { useEnableAuthenticator, useMfaStatus, useRegenerateRecoveryCodes, useRefreshMfaStatus, useSetupAuthenticator } from '../../../admin/api.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { num } from '../../../teacher/offers.ts';
import { KeyDisplay, RecoveryCodes } from './AdminGate.tsx';

/** The account's authenticator: recovery codes left, and new ones with a fresh code. */
export function AdminSecurityPage() {
  const { t } = useTranslation();
  const mfa = useMfaStatus();
  const regenerate = useRegenerateRecoveryCodes();
  const [code, setCode] = useState('');
  const [codes, setCodes] = useState<string[] | null>(null);
  const refreshStatus = useRefreshMfaStatus();

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    try {
      setCodes(await regenerate.mutateAsync(code.trim()));
    } catch {
      // shown below
    }
    setCode('');
  };

  if (codes) return <RecoveryCodes codes={codes} onDone={() => setCodes(null)} />;
  const refresh = () => void refreshStatus();
  const left = num(mfa.data?.recoveryCodesLeft) ?? 0;

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('admin.security.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('admin.security.lead')}</p>
      </div>
      <Card className="max-w-2xl">
        <CardHeader>
          <CardTitle className="text-base">{t('admin.security.recoveryTitle')}</CardTitle>
          <CardDescription>{t('admin.security.recoveryLeft', { count: left })}</CardDescription>
        </CardHeader>
        <CardContent>
          <form noValidate onSubmit={(e) => void submit(e)} className="flex flex-col gap-3">
            {left <= 2 && <Alert variant="warning">{t('admin.security.fewLeft')}</Alert>}
            <FormField
              label={t('admin.user.stepUpCode')}
              hint={t('admin.security.codeHint')}
              autoComplete="one-time-code"
              inputMode="numeric"
              maxLength={20}
              value={code}
              onChange={(e) => setCode(e.target.value)}
            />
            {regenerate.error && <Alert variant="error">{errorMessage(regenerate.error, t)}</Alert>}
            <Button type="submit" className="self-start" disabled={regenerate.isPending || !code.trim()}>
              {regenerate.isPending ? t('common.loading') : t('admin.security.regenerate')}
            </Button>
          </form>
        </CardContent>
      </Card>
      <ReplaceAuthenticator
        onReplaced={(fresh) => {
          setCodes(fresh);
          refresh();
        }}
      />
    </section>
  );
}

/**
 * New phone or lost phone: a new key, proved with a current code from the old app or with a recovery code (used up).
 * The old key stops working at once; the new one is confirmed with its first code and comes with new recovery codes.
 */
function ReplaceAuthenticator({ onReplaced }: { onReplaced: (codes: string[]) => void }) {
  const { t } = useTranslation();
  const setup = useSetupAuthenticator();
  const enable = useEnableAuthenticator();
  const [proof, setProof] = useState('');
  const [code, setCode] = useState('');

  const start = (event: FormEvent) => {
    event.preventDefault();
    const value = proof.trim();
    // Six digits are an authenticator code; anything else is treated as a recovery code.
    setup.mutate(/^\d{3}\s?\d{3}$/.test(value) ? { code: value } : { recoveryCode: value }, { onSettled: () => setProof('') });
  };

  const confirm = async (event: FormEvent) => {
    event.preventDefault();
    try {
      onReplaced(await enable.mutateAsync(code.trim()));
    } catch {
      setCode('');
    }
  };

  return (
    <Card className="max-w-2xl">
      <CardHeader>
        <CardTitle className="text-base">{t('admin.security.replaceTitle')}</CardTitle>
        <CardDescription>{t('admin.security.replaceLead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {!setup.data ? (
          <form noValidate onSubmit={start} className="flex flex-col gap-3">
            <FormField
              label={t('admin.security.proof')}
              hint={t('admin.security.proofHint')}
              autoComplete="one-time-code"
              maxLength={40}
              value={proof}
              onChange={(e) => setProof(e.target.value)}
            />
            {setup.error && <Alert variant="error">{errorMessage(setup.error, t)}</Alert>}
            <Button type="submit" variant="outline" className="self-start" disabled={setup.isPending || !proof.trim()}>
              {setup.isPending ? t('common.loading') : t('admin.security.replace')}
            </Button>
          </form>
        ) : (
          <>
            <KeyDisplay setup={setup.data} />
            <form noValidate onSubmit={(e) => void confirm(e)} className="flex flex-col gap-3">
              <FormField
                label={t('admin.mfa.code')}
                hint={t('admin.mfa.codeHint')}
                autoComplete="one-time-code"
                inputMode="numeric"
                maxLength={20}
                value={code}
                onChange={(e) => setCode(e.target.value)}
              />
              {enable.error && <Alert variant="error">{errorMessage(enable.error, t)}</Alert>}
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
