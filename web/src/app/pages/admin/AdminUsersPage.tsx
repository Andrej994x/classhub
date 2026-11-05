import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { errorMessage } from '../../../api/errors.ts';
import { USER_STATUSES, useAdminUsers } from '../../../admin/api.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardDescription, CardHeader } from '../../../components/ui/card.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { formatDate } from '../../../lib/format.ts';
import { num } from '../../../teacher/offers.ts';
import { StatusBadge } from './AdminUserPage.tsx';

const browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
const select = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';

/** Account search: email (or id), status and platform role; filters live in the URL. */
export function AdminUsersPage() {
  const { t, i18n } = useTranslation();
  const [params, setParams] = useSearchParams();
  const filters = {
    q: params.get('q') ?? '',
    status: params.get('status') ?? '',
    staff: params.get('staff') === 'true',
    page: Math.max(1, Number(params.get('page') ?? '1') || 1),
  };
  const [search, setSearch] = useState(filters.q);
  const users = useAdminUsers(filters);
  const language = i18n.resolvedLanguage ?? 'mk';

  const update = (changes: Partial<Record<'q' | 'status' | 'staff' | 'page', string | null>>) => {
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

  // Debounced search that always applies the newest parameters (see the lead inbox).
  const latestUpdate = useRef(update);
  useEffect(() => {
    latestUpdate.current = update;
  });
  useEffect(() => {
    if (search === filters.q) return undefined;
    const timer = setTimeout(() => latestUpdate.current({ q: search.trim() || null }), 350);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search]);

  if (users.isError) return <Alert variant="error">{errorMessage(users.error, t)}</Alert>;
  const data = users.data;
  const total = num(data?.total) ?? 0;
  const pages = Math.max(1, Math.ceil(total / (num(data?.pageSize) ?? 20)));

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('admin.users.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('admin.users.lead')}</p>
      </div>

      <form aria-label={t('admin.users.filters')} className="grid gap-3 sm:grid-cols-4" onSubmit={(e) => e.preventDefault()}>
        <div className="flex flex-col gap-1 sm:col-span-2">
          <Label htmlFor="admin-users-search">{t('admin.users.search')}</Label>
          <input id="admin-users-search" type="search" className={select} value={search} maxLength={256} onChange={(e) => setSearch(e.target.value)} />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="admin-users-status">{t('admin.users.status')}</Label>
          <select id="admin-users-status" className={select} value={filters.status} onChange={(e) => update({ status: e.target.value || null })}>
            <option value="">{t('admin.users.all')}</option>
            {USER_STATUSES.map((s) => (
              <option key={s} value={s}>
                {t(`admin.statuses.${s}`)}
              </option>
            ))}
          </select>
        </div>
        <label className="flex items-center gap-2 self-end text-sm sm:h-10">
          <input type="checkbox" className="h-4 w-4" checked={filters.staff} onChange={(e) => update({ staff: e.target.checked ? 'true' : null })} />
          {t('admin.users.staffOnly')}
        </label>
      </form>

      {!data ? (
        <output className="block">{t('common.loading')}</output>
      ) : data.items.length === 0 ? (
        <Card>
          <CardHeader>
            <CardDescription>{t('admin.users.none')}</CardDescription>
          </CardHeader>
        </Card>
      ) : (
        <>
          <p className="text-sm text-muted-foreground">{t('admin.users.count', { count: total })}</p>
          <ul aria-label={t('admin.users.title')} className="flex flex-col gap-3">
            {data.items.map((user) => (
              <li key={user.id}>
                <Link
                  to={`/admin/users/${user.id}`}
                  className="flex flex-col gap-1 rounded-lg border border-border bg-card p-4 hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                >
                  <span className="flex flex-wrap items-center gap-2">
                    <span className="break-all font-semibold">{user.email}</span>
                    <StatusBadge status={user.status} />
                    {user.platformRoles.map((role) => (
                      <span key={role} className="rounded-full border border-border px-2 py-0.5 text-xs">
                        {t(`admin.roles.${role}`)}
                      </span>
                    ))}
                  </span>
                  <span className="text-sm text-muted-foreground">
                    {t('admin.users.registered', { date: formatDate(user.createdAtUtc, language, browserTimeZone) })}
                    {user.workspace?.siteSlug ? ` · /${user.workspace.siteSlug}` : ''}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
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
