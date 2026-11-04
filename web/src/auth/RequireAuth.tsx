import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate, useLocation } from 'react-router';
import { Alert } from '../components/ui/alert.tsx';
import { errorMessage } from '../api/errors.ts';
import { useMe } from './session.ts';

/**
 * UX-only guard: sends anonymous visitors to the sign-in page and back afterwards. The API enforces access
 * on every request; nothing here is a security boundary.
 */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const location = useLocation();
  const me = useMe();

  if (me.isPending) {
    return (
      <output className="block p-8 text-sm text-muted-foreground">
        {t('common.loading')}
      </output>
    );
  }
  if (me.isError) {
    return (
      <div className="p-8">
        <Alert variant="error">{errorMessage(me.error, t)}</Alert>
      </div>
    );
  }
  if (!me.data) {
    const returnTo = `${location.pathname}${location.search}`;
    return <Navigate to={`/login?returnTo=${encodeURIComponent(returnTo)}`} replace />;
  }
  return <>{children}</>;
}
