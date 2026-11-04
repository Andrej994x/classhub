import { useMutation } from '@tanstack/react-query';
import { LogOut } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { api } from '../../api/client.ts';
import { errorMessage } from '../../api/errors.ts';
import { useMe, useSignOut } from '../../auth/session.ts';
import { Alert } from '../../components/ui/alert.tsx';
import { Button } from '../../components/ui/button.tsx';

/** Signed-in account and sign-out button for the area header. */
export function AccountMenu() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const me = useMe();
  const signOut = useSignOut();
  if (!me.data) return null;

  return (
    <div className="flex items-center gap-2">
      <span className="hidden max-w-48 truncate text-sm text-muted-foreground lg:inline" data-testid="account-email">
        {me.data.email}
      </span>
      <Button
        variant="ghost"
        size="sm"
        disabled={signOut.isPending}
        onClick={() => signOut.mutate(undefined, { onSuccess: () => void navigate('/login', { replace: true }) })}
      >
        <LogOut className="h-4 w-4" aria-hidden />
        <span className="sr-only sm:not-sr-only">{t('auth.logout')}</span>
      </Button>
      {signOut.isError && (
        <span role="alert" className="max-w-56 text-xs text-destructive">
          {t('auth.logoutFailed', { reason: errorMessage(signOut.error, t) })}
        </span>
      )}
    </div>
  );
}

/** Reminds unconfirmed accounts to confirm their email; publishing features will require it (later phases). */
export function UnconfirmedEmailBanner() {
  const { t } = useTranslation();
  const me = useMe();
  const resend = useMutation({
    mutationFn: async (email: string) => {
      await api.client.POST('/api/auth/resend-confirmation', { body: { email } });
    },
  });
  if (!me.data || me.data.emailConfirmed) return null;
  const email = me.data.email;

  return (
    <Alert variant="warning" className="mb-6 flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
      <span>{resend.isSuccess ? t('auth.unconfirmed.resent') : t('auth.unconfirmed.message')}</span>
      {!resend.isSuccess && (
        <Button variant="outline" size="sm" disabled={resend.isPending} onClick={() => resend.mutate(email)}>
          {t('auth.unconfirmed.resend')}
        </Button>
      )}
      {resend.isError && <span className="text-destructive">{errorMessage(resend.error, t)}</span>}
    </Alert>
  );
}
