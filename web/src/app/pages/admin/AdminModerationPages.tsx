import { useState, type FormEvent } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { errorMessage } from '../../../api/errors.ts';
import { REPORT_STATUSES, useDecideReport, useHiddenContent, useReports, useRestoreContent, type AdminReport, type HiddenContent } from '../../../admin/moderation.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader } from '../../../components/ui/card.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { formatDateTime } from '../../../lib/format.ts';
import { num } from '../../../teacher/offers.ts';

const browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
const select = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';
const textarea = 'w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm';

function targetLabel(t: TFunction, item: { target: string; siteSlug?: string | null; sectionType?: string | null; testTitle?: string | null }) {
  const where = item.siteSlug ? `/${item.siteSlug}` : '';
  if (item.target === 'section') return `${t('admin.reports.targets.section')}: ${t(`site.sectionTypes.${item.sectionType}`, { defaultValue: item.sectionType })} · ${where}`;
  if (item.target === 'test') return `${t('admin.reports.targets.test')}: ${item.testTitle ?? ''} · ${where}`;
  return `${t('admin.reports.targets.site')} · ${where}`;
}

/** The moderation queue (R1-P11): reports from visitors, newest first; hide the content with a reason, or dismiss. */
export function AdminReportsPage() {
  const { t } = useTranslation();
  const [params, setParams] = useSearchParams();
  const status = params.get('status') ?? 'Open';
  const page = Math.max(1, Number(params.get('page') ?? '1') || 1);
  const reports = useReports(status, page);
  const data = reports.data;
  const total = num(data?.total) ?? 0;
  const pages = Math.max(1, Math.ceil(total / (num(data?.pageSize) ?? 20)));

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('admin.reports.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('admin.reports.lead')}</p>
      </div>
      <div className="flex max-w-xs flex-col gap-1">
        <Label htmlFor="reports-status">{t('admin.reports.status')}</Label>
        <select id="reports-status" className={select} value={status} onChange={(e) => setParams({ status: e.target.value }, { replace: true })}>
          {REPORT_STATUSES.map((s) => (
            <option key={s} value={s}>
              {t(`admin.reports.statuses.${s}`)}
            </option>
          ))}
        </select>
      </div>

      {reports.isError ? (
        <Alert variant="error">{errorMessage(reports.error, t)}</Alert>
      ) : !data ? (
        <output className="block">{t('common.loading')}</output>
      ) : data.items.length === 0 ? (
        <Card>
          <CardHeader>
            <CardDescription>{t('admin.reports.none')}</CardDescription>
          </CardHeader>
        </Card>
      ) : (
        <>
          <p className="text-sm text-muted-foreground">{t('admin.reports.count', { count: total })}</p>
          <ul aria-label={t('admin.reports.title')} className="flex flex-col gap-3">
            {data.items.map((report) => (
              <li key={report.id}>
                <ReportCard report={report} />
              </li>
            ))}
          </ul>
        </>
      )}

      {pages > 1 && (
        <nav aria-label={t('admin.pagination')} className="flex items-center gap-3">
          <Button type="button" variant="outline" disabled={page <= 1} onClick={() => setParams({ status, page: String(page - 1) }, { replace: true })}>
            {t('leads.previous')}
          </Button>
          <span className="text-sm">{t('leads.pageOf', { page, pages })}</span>
          <Button type="button" variant="outline" disabled={page >= pages} onClick={() => setParams({ status, page: String(page + 1) }, { replace: true })}>
            {t('leads.next')}
          </Button>
        </nav>
      )}
    </section>
  );
}

function ReportCard({ report }: { report: AdminReport }) {
  const { t, i18n } = useTranslation();
  const decide = useDecideReport();
  const [reason, setReason] = useState('');
  const language = i18n.resolvedLanguage ?? 'mk';
  const open = report.status === 'Open';
  const fieldId = `report-${report.id}-reason`;

  const act = (hide: boolean) => (event?: FormEvent) => {
    event?.preventDefault();
    if (hide && reason.trim().length < 3) return;
    decide.mutate({ reportId: report.id, hide, reason: reason.trim() });
  };

  return (
    <article aria-label={targetLabel(t, report)} className="flex flex-col gap-2 rounded-lg border border-border bg-card p-4 text-sm">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <strong className="text-base">{t(`admin.reports.reasons.${report.reason}`)}</strong>
        <span className="text-muted-foreground">{formatDateTime(report.createdAtUtc, language, browserTimeZone)}</span>
        {report.targetHidden && <span className="rounded-full bg-destructive px-2 py-0.5 text-xs font-medium text-destructive-foreground">{t('admin.reports.alreadyHidden')}</span>}
      </div>
      <p>
        {targetLabel(t, report)}
        {report.siteSlug && (
          <>
            {' · '}
            <a className="text-primary underline-offset-4 hover:underline" href={`/${report.siteSlug}`} target="_blank" rel="noreferrer">
              {t('admin.reports.page')}
            </a>
          </>
        )}
      </p>
      {report.details && <p className="whitespace-pre-wrap rounded bg-muted p-2">{report.details}</p>}
      <p className="text-muted-foreground">
        {t('admin.reports.reporter')}: {report.reporterEmail ?? t('admin.reports.anonymous')}
        {open && num(report.openReportsForTarget)! > 1 && ` · ${t('admin.reports.sameTarget', { count: num(report.openReportsForTarget) })}`}
      </p>
      {open ? (
        <form noValidate onSubmit={act(true)} className="flex flex-col gap-2">
          <Label htmlFor={fieldId}>{t('admin.reports.reason')}</Label>
          <textarea id={fieldId} rows={2} maxLength={500} className={textarea} value={reason} onChange={(e) => setReason(e.target.value)} />
          <p className="text-xs text-muted-foreground">{t('admin.reports.note')}</p>
          {decide.error && <Alert variant="error">{errorMessage(decide.error, t)}</Alert>}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" variant="destructive" disabled={decide.isPending || reason.trim().length < 3}>
              {t('admin.reports.hide')}
            </Button>
            <Button type="button" variant="outline" disabled={decide.isPending} onClick={() => act(false)()}>
              {t('admin.reports.dismiss')}
            </Button>
          </div>
        </form>
      ) : (
        <p className="text-muted-foreground">
          {t(`admin.reports.statuses.${report.status}`)}
          {report.resolvedAtUtc && ` · ${t('admin.reports.resolution', { date: formatDateTime(report.resolvedAtUtc, language, browserTimeZone) })}`}
          {report.resolutionNote && ` · ${report.resolutionNote}`}
        </p>
      )}
    </article>
  );
}

/** Content hidden by moderation, with "show again" (reason required; the teacher is told). */
export function AdminHiddenPage() {
  const { t } = useTranslation();
  const hidden = useHiddenContent();

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('admin.hidden.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('admin.hidden.lead')}</p>
      </div>
      {hidden.isError ? (
        <Alert variant="error">{errorMessage(hidden.error, t)}</Alert>
      ) : !hidden.data ? (
        <output className="block">{t('common.loading')}</output>
      ) : hidden.data.length === 0 ? (
        <Card>
          <CardHeader>
            <CardDescription>{t('admin.hidden.none')}</CardDescription>
          </CardHeader>
        </Card>
      ) : (
        <ul aria-label={t('admin.hidden.title')} className="flex flex-col gap-3">
          {hidden.data.map((item) => (
            <li key={`${item.workspaceId}-${item.target}-${item.sectionType ?? ''}-${item.testId ?? ''}`}>
              <HiddenCard item={item} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function HiddenCard({ item }: { item: HiddenContent }) {
  const { t, i18n } = useTranslation();
  const restore = useRestoreContent();
  const [reason, setReason] = useState('');
  const id = `hidden-${item.workspaceId}-${item.target}-${item.sectionType ?? item.testId ?? 'site'}`;

  return (
    <Card>
      <CardContent className="flex flex-col gap-2 pt-6 text-sm">
        <strong>{targetLabel(t, item)}</strong>
        <p className="text-muted-foreground">
          {t('admin.hidden.hiddenAt', { date: formatDateTime(item.hiddenAtUtc, i18n.resolvedLanguage ?? 'mk', browserTimeZone) })} · {item.reason}
        </p>
        <form
          noValidate
          className="flex flex-col gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (reason.trim().length >= 3) restore.mutate({ item, reason: reason.trim() });
          }}
        >
          <Label htmlFor={id}>{t('admin.hidden.reason')}</Label>
          <input id={id} className={select} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
          {restore.error && <Alert variant="error">{errorMessage(restore.error, t)}</Alert>}
          <Button type="submit" variant="outline" className="self-start" disabled={restore.isPending || reason.trim().length < 3}>
            {t('admin.hidden.restore')}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
