import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '../../../api/errors.ts';
import { useMfaStatus, useRegenerateRecoveryCodes } from '../../../admin/api.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { num } from '../../../teacher/offers.ts';
import { RecoveryCodes } from './AdminGate.tsx';

/** The account's authenticator: recovery codes left, and new ones with a fresh code. */
export function AdminSecurityPage() {
  const { t } = useTranslation();
  const mfa = useMfaStatus();
  const regenerate = useRegenerateRecoveryCodes();
  const [code, setCode] = useState('');
  const [codes, setCodes] = useState<string[] | null>(null);

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
    </section>
  );
}
