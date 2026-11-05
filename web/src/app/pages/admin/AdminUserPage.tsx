import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { PLATFORM_ADMIN, PLATFORM_OWNER, useAdminUser, useChangeAdminRole, useChangeUserStatus, type AdminUserDetail } from '../../../admin/api.ts';
import { useMe } from '../../../auth/session.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { formatDateTime } from '../../../lib/format.ts';
import { NotFoundContent } from '../Pages.tsx';
import { AuditEntries } from './AdminAuditPage.tsx';
import { SubscriptionCard } from './SubscriptionCard.tsx';

const browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
export const REASON_MAX = 500;

export function StatusBadge({ status }: { status: string }) {
  const { t } = useTranslation();
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${status === 'Suspended' ? 'bg-destructive text-destructive-foreground' : 'bg-secondary text-secondary-foreground'}`}>
      {t(`admin.statuses.${status}`)}
    </span>
  );
}

/** One account: metadata, suspension with a reason, the Platform Admin role (owner only) and its audit history. */
export function AdminUserPage() {
  const { t, i18n } = useTranslation();
  const { userId = '' } = useParams();
  const user = useAdminUser(userId);
  const me = useMe();
  const language = i18n.resolvedLanguage ?? 'mk';

  if (user.isError) return user.error instanceof ApiError && user.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(user.error, t)}</Alert>;
  if (!user.data) return <output className="block">{t('common.loading')}</output>;

  const detail = user.data;
  const account = detail.user;
  const date = (value: string | null | undefined) => (value ? formatDateTime(value, language, browserTimeZone) : t('admin.user.never'));
  const self = me.data?.id === account.id;
  const isOwner = me.data?.platformRoles.includes(PLATFORM_OWNER) ?? false;
  const targetIsOwner = account.platformRoles.includes(PLATFORM_OWNER);
  // Mirrors the API rules (which stay authoritative): nobody changes their own account here, owners are managed with
  // the command-line tool, and only the owner manages admins.
  const mayChangeStatus = !self && !targetIsOwner && (isOwner || !account.platformRoles.includes(PLATFORM_ADMIN));

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-2">
        <Link to="/admin/users" className="text-sm text-primary underline-offset-4 hover:underline">
          {t('admin.user.back')}
        </Link>
        <h1 className="flex flex-wrap items-center gap-2 text-2xl font-semibold tracking-tight">
          <span className="break-all">{account.email}</span>
          <StatusBadge status={account.status} />
        </h1>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t('admin.user.account')}</CardTitle>
          <CardDescription>{t('admin.user.metadataOnly')}</CardDescription>
        </CardHeader>
        <CardContent>
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <Item label={t('admin.user.registered')} value={date(account.createdAtUtc)} />
            <Item label={t('admin.user.lastLogin')} value={date(account.lastLoginAtUtc)} />
            <Item label={t('admin.user.emailConfirmed')} value={account.emailConfirmed ? t('admin.yes') : t('admin.no')} />
            <Item label={t('admin.user.mfa')} value={account.mfaEnabled ? t('admin.yes') : t('admin.no')} />
            <Item label={t('admin.user.roles')} value={account.platformRoles.length ? account.platformRoles.map((r) => t(`admin.roles.${r}`)).join(', ') : t('admin.user.noRoles')} />
            {detail.lockedOutUntilUtc && <Item label={t('admin.user.lockedOut')} value={date(detail.lockedOutUntilUtc)} />}
            <Item label={t('admin.user.workspace')} value={account.workspace ? account.workspace.name : t('admin.user.noWorkspace')} />
            {account.workspace && (
              <Item
                label={t('admin.user.site')}
                value={account.workspace.siteSlug ? `/${account.workspace.siteSlug} · ${account.workspace.sitePublished ? t('admin.user.published') : t('admin.user.draft')}` : t('admin.user.noSite')}
              />
            )}
          </dl>
        </CardContent>
      </Card>

      {account.workspace && <SubscriptionCard workspaceId={account.workspace.id} />}
      {mayChangeStatus && <StatusCard key={account.status} detail={detail} />}
      {isOwner && !self && !targetIsOwner && <RoleCard key={account.platformRoles.join()} detail={detail} />}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t('admin.user.history')}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {detail.history.length === 0 ? <p className="text-sm text-muted-foreground">{t('admin.audit.none')}</p> : <AuditEntries entries={detail.history} />}
          <Link to={`/admin/audit?userId=${account.id}`} className="text-sm text-primary underline-offset-4 hover:underline">
            {t('admin.user.fullAudit')}
          </Link>
        </CardContent>
      </Card>
    </section>
  );
}

function Item({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="break-words font-medium">{value}</dd>
    </div>
  );
}

function StatusCard({ detail }: { detail: AdminUserDetail }) {
  const { t } = useTranslation();
  const change = useChangeUserStatus(detail.user.id);
  const suspend = detail.user.status === 'Active';
  const [reason, setReason] = useState('');
  const valid = reason.trim().length >= 3;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (valid) change.mutate({ suspend, reason: reason.trim() });
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{suspend ? t('admin.user.suspendTitle') : t('admin.user.reactivateTitle')}</CardTitle>
        <CardDescription>{suspend ? t('admin.user.suspendLead') : t('admin.user.reactivateLead')}</CardDescription>
      </CardHeader>
      <CardContent>
        <form noValidate onSubmit={submit} className="flex flex-col gap-2">
          <Label htmlFor="admin-status-reason">{t('admin.user.reason')}</Label>
          <textarea
            id="admin-status-reason"
            rows={3}
            maxLength={REASON_MAX}
            required
            className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
          <p className="text-xs text-muted-foreground">{t('admin.user.reasonHint')}</p>
          {change.error && <Alert variant="error">{errorMessage(change.error, t)}</Alert>}
          <Button type="submit" variant={suspend ? 'destructive' : 'default'} className="self-start" disabled={change.isPending || !valid}>
            {change.isPending ? t('common.loading') : suspend ? t('admin.user.suspend') : t('admin.user.reactivate')}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}

/** Platform Owner only: grant or revoke Platform Admin, confirmed with a fresh authenticator code (step-up). */
function RoleCard({ detail }: { detail: AdminUserDetail }) {
  const { t } = useTranslation();
  const change = useChangeAdminRole(detail.user.id);
  const grant = !detail.user.platformRoles.includes(PLATFORM_ADMIN);
  const [code, setCode] = useState('');

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (code.trim()) change.mutate({ grant, code: code.trim() }, { onError: () => setCode('') });
  };

  if (grant && detail.user.status !== 'Active') return null;
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{grant ? t('admin.user.grantTitle') : t('admin.user.revokeTitle')}</CardTitle>
        <CardDescription>{grant ? t('admin.user.grantLead') : t('admin.user.revokeLead')}</CardDescription>
      </CardHeader>
      <CardContent>
        <form noValidate onSubmit={submit} className="flex flex-col gap-3">
          <FormField
            label={t('admin.user.stepUpCode')}
            hint={t('admin.user.stepUpHint')}
            autoComplete="one-time-code"
            inputMode="numeric"
            maxLength={20}
            value={code}
            onChange={(e) => setCode(e.target.value)}
          />
          {change.error && <Alert variant="error">{errorMessage(change.error, t)}</Alert>}
          <Button type="submit" variant={grant ? 'default' : 'destructive'} className="self-start" disabled={change.isPending || !code.trim()}>
            {change.isPending ? t('common.loading') : grant ? t('admin.user.grant') : t('admin.user.revoke')}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
