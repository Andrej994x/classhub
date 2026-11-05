import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { errorMessage } from '../../../api/errors.ts';
import { useAuditLog, type AdminAuditEntry } from '../../../admin/api.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardDescription, CardHeader } from '../../../components/ui/card.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { formatDateTime } from '../../../lib/format.ts';
import { num } from '../../../teacher/offers.ts';

const browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
const input = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';

/** Audit entries, newest first: who did what, when, to which record. */
export function AuditEntries({ entries }: { entries: AdminAuditEntry[] }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'mk';
  return (
    <ol aria-label={t('admin.audit.entries')} className="flex flex-col gap-2">
      {entries.map((entry) => (
        <li key={entry.id} className="flex flex-col gap-1 rounded-md border border-border p-3 text-sm">
          <span className="flex flex-wrap items-baseline gap-x-2">
            <code className="font-mono font-semibold">{entry.action}</code>
            <span className="text-muted-foreground">{formatDateTime(entry.occurredAtUtc, language, browserTimeZone)}</span>
          </span>
          <span className="break-all text-muted-foreground">
            {t('admin.audit.by')}{' '}
            {entry.actorUserId ? (
              <Link className="text-primary underline-offset-4 hover:underline" to={`/admin/users/${entry.actorUserId}`}>
                {entry.actorEmail ?? entry.actorUserId}
              </Link>
            ) : (
              t('admin.audit.system')
            )}{' '}
            · {entry.targetType} {entry.targetType === 'User' ? <Link className="text-primary underline-offset-4 hover:underline" to={`/admin/users/${entry.targetId}`}>{entry.targetId}</Link> : entry.targetId}
          </span>
          {entry.detailsJson && (
            <details>
              <summary className="cursor-pointer text-muted-foreground">{t('admin.audit.details')}</summary>
              <pre className="mt-1 overflow-x-auto whitespace-pre-wrap break-all rounded bg-muted p-2 text-xs">{pretty(entry.detailsJson)}</pre>
            </details>
          )}
        </li>
      ))}
    </ol>
  );
}

function pretty(json: string) {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

/** The audit viewer with filters in the URL (action prefix, user, UTC days). */
export function AdminAuditPage() {
  const { t } = useTranslation();
  const [params, setParams] = useSearchParams();
  const filters = {
    action: params.get('action') ?? '',
    userId: params.get('userId') ?? '',
    from: params.get('from') ?? '',
    to: params.get('to') ?? '',
    page: Math.max(1, Number(params.get('page') ?? '1') || 1),
  };
  const [action, setAction] = useState(filters.action);
  const audit = useAuditLog(filters);

  const update = (changes: Partial<Record<'action' | 'userId' | 'from' | 'to' | 'page', string | null>>) => {
    setParams(
      (current) => {
        const next = new URLSearchParams(current);
        for (const [key, value] of Object.entries(changes)) {
          if (value) next.set(key, value);
          else next.delete(key);
        }
        if (!('page' in changes)) next.delete('page');
        return next;
      },
      { replace: true },
    );
  };

  const latestUpdate = useRef(update);
  useEffect(() => {
    latestUpdate.current = update;
  });
  useEffect(() => {
    if (action === filters.action) return undefined;
    const timer = setTimeout(() => latestUpdate.current({ action: action.trim() || null }), 350);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [action]);

  const data = audit.data;
  const total = num(data?.total) ?? 0;
  const pages = Math.max(1, Math.ceil(total / (num(data?.pageSize) ?? 20)));

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('admin.audit.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('admin.audit.lead')}</p>
      </div>

      <form aria-label={t('admin.audit.filters')} className="grid gap-3 sm:grid-cols-4" onSubmit={(e) => e.preventDefault()}>
        <div className="flex flex-col gap-1 sm:col-span-2">
          <Label htmlFor="audit-action">{t('admin.audit.action')}</Label>
          <input id="audit-action" type="search" className={input} placeholder="user." value={action} maxLength={100} onChange={(e) => setAction(e.target.value)} />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="audit-from">{t('admin.audit.from')}</Label>
          <input id="audit-from" type="date" className={input} value={filters.from} onChange={(e) => update({ from: e.target.value || null })} />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="audit-to">{t('admin.audit.to')}</Label>
          <input id="audit-to" type="date" className={input} value={filters.to} onChange={(e) => update({ to: e.target.value || null })} />
        </div>
        {filters.userId && (
          <p className="flex flex-wrap items-center gap-2 text-sm sm:col-span-4">
            {t('admin.audit.forUser', { id: filters.userId })}
            <Button type="button" variant="outline" size="sm" onClick={() => update({ userId: null })}>
              {t('admin.audit.clearUser')}
            </Button>
          </p>
        )}
      </form>

      {audit.isError ? (
        <Alert variant="error">{errorMessage(audit.error, t)}</Alert>
      ) : !data ? (
        <output className="block">{t('common.loading')}</output>
      ) : data.items.length === 0 ? (
        <Card>
          <CardHeader>
            <CardDescription>{t('admin.audit.none')}</CardDescription>
          </CardHeader>
        </Card>
      ) : (
        <>
          <p className="text-sm text-muted-foreground">{t('admin.audit.count', { count: total })}</p>
          <AuditEntries entries={data.items} />
        </>
      )}

      {pages > 1 && (
        <nav aria-label={t('admin.pagination')} className="flex items-center gap-3">
          <Button type="button" variant="outline" disabled={filters.page <= 1} onClick={() => update({ page: String(filters.page - 1) })}>
            {t('leads.previous')}
          </Button>
          <span className="text-sm">{t('leads.pageOf', { page: filters.page, pages })}</span>
          <Button type="button" variant="outline" disabled={filters.page >= pages} onClick={() => update({ page: String(filters.page + 1) })}>
            {t('leads.next')}
          </Button>
        </nav>
      )}
    </section>
  );
}
