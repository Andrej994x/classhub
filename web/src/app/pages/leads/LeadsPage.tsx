import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams, useSearchParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardDescription, CardHeader } from '../../../components/ui/card.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { formatDateTime } from '../../../lib/format.ts';
import { LEAD_STATUSES, LEAD_TYPES, useLeads } from '../../../teacher/leads.ts';
import { num } from '../../../teacher/offers.ts';
import { NotFoundContent } from '../Pages.tsx';

const browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
const select = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';

/** The lead inbox: newest first, filters in the URL (shareable, survive reloads). */
export function LeadsPage() {
  const { t, i18n } = useTranslation();
  const { workspaceId = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const filters = {
    status: params.get('status') ?? '',
    type: params.get('type') ?? '',
    q: params.get('q') ?? '',
    archived: params.get('archived') === 'true',
    page: Math.max(1, Number(params.get('page') ?? '1') || 1),
  };
  const [search, setSearch] = useState(filters.q);
  const leads = useLeads(workspaceId, filters);
  const language = i18n.resolvedLanguage ?? 'mk';

  const update = (changes: Partial<Record<'status' | 'type' | 'q' | 'archived' | 'page', string | null>>) => {
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value);
      else next.delete(key);
    }
    if (!('page' in changes)) next.delete('page');
    setParams(next, { replace: true });
  };

  // Search while typing, debounced.
  useEffect(() => {
    if (search === filters.q) return undefined;
    const timer = setTimeout(() => update({ q: search.trim() || null }), 350);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search]);

  if (leads.isError) return leads.error instanceof ApiError && leads.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(leads.error, t)}</Alert>;

  const data = leads.data;
  const total = num(data?.total) ?? 0;
  const pageSize = num(data?.pageSize) ?? 20;
  const pages = Math.max(1, Math.ceil(total / pageSize));

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('leads.title')}</h1>
        <p className="text-sm text-muted-foreground">
          {t('leads.lead')} {data && num(data.newCount)! > 0 && <strong>{t('leads.newCount', { count: num(data.newCount)! })}</strong>}
        </p>
      </div>

      <form aria-label={t('leads.filters')} className="grid gap-3 sm:grid-cols-4" onSubmit={(e) => e.preventDefault()}>
        <div className="flex flex-col gap-1 sm:col-span-2">
          <Label htmlFor="leads-search">{t('leads.search')}</Label>
          <input id="leads-search" type="search" className={select} value={search} maxLength={100} onChange={(e) => setSearch(e.target.value)} />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="leads-status">{t('leads.status')}</Label>
          <select id="leads-status" className={select} value={filters.status} onChange={(e) => update({ status: e.target.value || null })}>
            <option value="">{t('leads.all')}</option>
            {LEAD_STATUSES.map((s) => (
              <option key={s} value={s}>
                {t(`leads.statuses.${s}`)}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="leads-type">{t('leads.type')}</Label>
          <select id="leads-type" className={select} value={filters.type} onChange={(e) => update({ type: e.target.value || null })}>
            <option value="">{t('leads.all')}</option>
            {LEAD_TYPES.map((s) => (
              <option key={s} value={s}>
                {t(`leads.types.${s}`)}
              </option>
            ))}
          </select>
        </div>
        <label className="flex items-center gap-2 text-sm sm:col-span-4">
          <input type="checkbox" className="h-4 w-4" checked={filters.archived} onChange={(e) => update({ archived: e.target.checked ? 'true' : null })} />
          {t('leads.showArchived')}
        </label>
      </form>

      {!data ? (
        <output className="block">{t('common.loading')}</output>
      ) : data.items.length === 0 ? (
        <Card>
          <CardHeader>
            <CardDescription>{filters.status || filters.type || filters.q || filters.archived ? t('leads.noMatches') : t('leads.empty')}</CardDescription>
          </CardHeader>
        </Card>
      ) : (
        <ul aria-label={t('leads.title')} className="flex flex-col gap-3">
          {data.items.map((lead) => (
            <li key={lead.id}>
              <Link
                to={`/app/w/${workspaceId}/leads/${lead.id}`}
                className="flex flex-col gap-1 rounded-lg border border-border bg-card p-4 hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                <span className="flex flex-wrap items-center gap-2">
                  <span className="font-semibold">{lead.name}</span>
                  <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${lead.status === 'New' ? 'bg-primary text-primary-foreground' : 'bg-secondary text-secondary-foreground'}`}>
                    {t(`leads.statuses.${lead.status}`)}
                  </span>
                  <span className="text-xs text-muted-foreground">{t(`leads.types.${lead.type}`)}</span>
                  {lead.archived && <span className="text-xs text-muted-foreground">· {t('leads.archived')}</span>}
                </span>
                <span className="text-sm text-muted-foreground">
                  {lead.email} · {formatDateTime(lead.createdAtUtc, language, browserTimeZone)}
                  {lead.offerTitle ? ` · ${lead.offerTitle}` : ''}
                </span>
                {lead.preview && <span className="line-clamp-2 text-sm">{lead.preview}</span>}
              </Link>
            </li>
          ))}
        </ul>
      )}

      {pages > 1 && (
        <nav aria-label={t('leads.pagination')} className="flex items-center gap-3">
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
