import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { DASHBOARD_PERIODS, useDashboard, type Dashboard, type DashboardPeriod } from '../../../teacher/dashboard.ts';
import { num } from '../../../teacher/offers.ts';

const n = (value: number | string | null | undefined) => num(value) ?? 0;

/** The teacher's numbers (R1-P8): stat tiles, the conversion funnel, leads by status and daily page visits. */
export function DashboardOverview({ workspaceId }: { workspaceId: string }) {
  const { t } = useTranslation();
  const [days, setDays] = useState<DashboardPeriod>(30);
  const dashboard = useDashboard(workspaceId, days);

  if (dashboard.isError) {
    return dashboard.error instanceof ApiError && dashboard.error.status === 404 ? null : <Alert variant="error">{errorMessage(dashboard.error, t)}</Alert>;
  }

  return (
    <section aria-labelledby="dashboard-numbers" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="dashboard-numbers" className="text-lg font-semibold">
          {t('dashboard.numbers')}
        </h2>
        <fieldset className="inline-flex rounded-md border border-border p-0.5">
          <legend className="sr-only">{t('dashboard.period')}</legend>
          {DASHBOARD_PERIODS.map((period) => (
            <label key={period} className="cursor-pointer">
              <input type="radio" name="dashboard-period" className="peer sr-only" checked={days === period} onChange={() => setDays(period)} />
              <span className="block rounded px-3 py-1.5 text-sm font-medium text-muted-foreground hover:bg-muted peer-checked:bg-primary peer-checked:text-primary-foreground peer-focus-visible:ring-2 peer-focus-visible:ring-ring">
                {t('dashboard.days', { count: period })}
              </span>
            </label>
          ))}
        </fieldset>
      </div>
      {!dashboard.data ? <output className="block">{t('common.loading')}</output> : <Numbers workspaceId={workspaceId} data={dashboard.data} />}
    </section>
  );
}

function Tile({ label, value, hint, to }: { label: string; value: number; hint?: string; to?: string }) {
  const body = (
    <>
      <span className="text-sm text-muted-foreground">{label}</span>
      <span className="text-3xl font-semibold tabular-nums">{value.toLocaleString()}</span>
      {hint && <span className="text-xs text-muted-foreground">{hint}</span>}
    </>
  );
  const className = 'flex flex-col gap-1 rounded-lg border border-border bg-card p-4';
  return to ? (
    <Link to={to} className={`${className} hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring`}>
      {body}
    </Link>
  ) : (
    <div className={className}>{body}</div>
  );
}

function Numbers({ workspaceId, data }: { workspaceId: string; data: Dashboard }) {
  const { t } = useTranslation();
  const funnel = [
    { key: 'visits', value: n(data.funnel.visits) },
    { key: 'leads', value: n(data.funnel.leads) },
    { key: 'contacted', value: n(data.funnel.contacted) },
    { key: 'converted', value: n(data.funnel.converted) },
  ];
  const widest = Math.max(1, ...funnel.map((step) => step.value));

  return (
    <div className="flex flex-col gap-4">
      <ul aria-label={t('dashboard.numbers')} className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        <li>
          <Tile label={t('dashboard.tiles.newLeads')} value={n(data.leads.new)} hint={t('dashboard.tiles.newLeadsHint')} to={`/app/w/${workspaceId}/leads?status=New`} />
        </li>
        <li>
          <Tile label={t('dashboard.tiles.leadsInPeriod')} value={n(data.leads.inPeriod)} />
        </li>
        <li>
          <Tile label={t('dashboard.tiles.visits')} value={n(data.site.visitsInPeriod)} hint={data.site.published ? undefined : t('dashboard.tiles.siteNotPublished')} />
        </li>
        <li>
          <Tile
            label={t('dashboard.tiles.attempts')}
            value={n(data.tests.attemptsInPeriod)}
            hint={t('dashboard.tiles.attemptsHint', { shared: n(data.tests.sharedResultsInPeriod), published: n(data.tests.published) })}
          />
        </li>
        <li>
          <Tile label={t('dashboard.tiles.activeOffers')} value={n(data.offers.active)} hint={t('dashboard.tiles.publicOffers', { count: n(data.offers.public) })} to={`/app/w/${workspaceId}/offers`} />
        </li>
      </ul>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">{t('dashboard.funnel.title')}</CardTitle>
            <CardDescription>{t('dashboard.funnel.lead')}</CardDescription>
          </CardHeader>
          <CardContent>
            <ol aria-label={t('dashboard.funnel.title')} className="flex flex-col gap-3">
              {funnel.map((step, index) => {
                const previous = index === 0 ? null : funnel[index - 1]!.value;
                const share = previous ? Math.round((step.value / previous) * 100) : null;
                return (
                  <li key={step.key} className="flex flex-col gap-1">
                    <span className="flex justify-between text-sm">
                      <span>{t(`dashboard.funnel.${step.key}`)}</span>
                      <span className="tabular-nums text-muted-foreground">
                        <strong className="text-foreground">{step.value.toLocaleString()}</strong>
                        {share !== null && ` · ${t('dashboard.funnel.ofPrevious', { percent: share })}`}
                      </span>
                    </span>
                    <span aria-hidden className="h-2 rounded-full bg-muted">
                      <span className="block h-2 rounded-full bg-primary" style={{ width: `${(step.value / widest) * 100}%` }} />
                    </span>
                  </li>
                );
              })}
            </ol>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">{t('dashboard.status.title')}</CardTitle>
            <CardDescription>{t('dashboard.status.lead')}</CardDescription>
          </CardHeader>
          <CardContent>
            <dl className="grid grid-cols-2 gap-3">
              {data.leads.byStatus.map((status) => (
                <div key={status.key} className="flex flex-col rounded-md bg-muted px-3 py-2">
                  <dt className="text-sm text-muted-foreground">{t(`leads.statuses.${status.key}`)}</dt>
                  <dd className="text-xl font-semibold tabular-nums">
                    <Link className="hover:underline" to={`/app/w/${workspaceId}/leads?status=${status.key}`}>
                      {n(status.count).toLocaleString()}
                    </Link>
                  </dd>
                </div>
              ))}
            </dl>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t('dashboard.visits.title')}</CardTitle>
          <CardDescription>{t('dashboard.visits.lead')}</CardDescription>
        </CardHeader>
        <CardContent>
          <DailyVisits days={data.site.daily.map((d) => ({ day: d.day, count: n(d.count) }))} />
        </CardContent>
      </Card>
    </div>
  );
}

const WIDTH = 720;
const HEIGHT = 144;
const AXIS = 1;

/**
 * Page visits per day: one series, so no legend (the card title names it). Thin bars with rounded tops anchored to the
 * baseline and a gap between them, a hover/focus tooltip per day, and the same numbers as a table for screen readers.
 */
function DailyVisits({ days }: { days: { day: string; count: number }[] }) {
  const { t, i18n } = useTranslation();
  const [active, setActive] = useState<number | null>(null);
  const locale = i18n.resolvedLanguage === 'en' ? 'en-GB' : 'mk-MK';
  const label = (day: string) => new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(`${day}T00:00:00Z`));
  const max = Math.max(1, ...days.map((d) => d.count));
  const slot = WIDTH / days.length;
  const gap = Math.min(2, slot * 0.25);
  const barWidth = Math.max(1, slot - gap);
  const plot = HEIGHT - AXIS;
  const radius = Math.min(4, barWidth / 2);
  const total = days.reduce((sum, d) => sum + d.count, 0);
  const shown = active === null ? null : days[active];

  return (
    <figure className="flex flex-col gap-2">
      <div className="relative">
        <p className="sr-only">{t('dashboard.visits.summary', { total, count: days.length })}</p>
        {/* Decorative for assistive technology: the summary above and the table below carry the same numbers. */}
        <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} preserveAspectRatio="none" className="h-36 w-full" aria-hidden="true">
          <line x1={0} x2={WIDTH} y1={plot} y2={plot} className="stroke-border" strokeWidth={1} />
          {days.map((d, i) => {
            const h = d.count === 0 ? 0 : Math.max(2, (d.count / max) * (plot - 8));
            const x = i * slot + gap / 2;
            const y = plot - h;
            const r = Math.min(radius, h);
            const path = h === 0 ? '' : `M${x},${plot} V${y + r} Q${x},${y} ${x + r},${y} H${x + barWidth - r} Q${x + barWidth},${y} ${x + barWidth},${y + r} V${plot} Z`;
            return (
              <g key={d.day}>
                {path && <path d={path} className={active === i ? 'fill-primary' : 'fill-primary/70'} />}
                {/* Hit target: the whole column, larger than the bar. */}
                <rect x={i * slot} y={0} width={slot} height={plot} fill="transparent" onMouseEnter={() => setActive(i)} onMouseLeave={() => setActive(null)} />
              </g>
            );
          })}
        </svg>
        {/* Axis labels in HTML: the plot stretches to the card width, text must not. */}
        <div aria-hidden="true" className="flex justify-between text-xs text-muted-foreground">
          <span>{label(days[0]!.day)}</span>
          <span>{label(days[days.length - 1]!.day)}</span>
        </div>
        {total === 0 && <p className="absolute inset-x-0 top-12 text-center text-sm text-muted-foreground">{t('dashboard.visits.none')}</p>}
        {shown && (
          <output
            className="pointer-events-none absolute top-0 -translate-x-1/2 rounded-md border border-border bg-background px-2 py-1 text-xs shadow-sm"
            style={{ left: `${((active! + 0.5) / days.length) * 100}%` }}
          >
            <span className="text-muted-foreground">{label(shown.day)}</span> · <strong>{t('dashboard.visits.count', { count: shown.count })}</strong>
          </output>
        )}
      </div>
      <details className="text-sm">
        <summary className="cursor-pointer text-muted-foreground">{t('dashboard.visits.table')}</summary>
        <table className="mt-2 w-full text-left">
          <thead>
            <tr>
              <th scope="col" className="font-medium">
                {t('dashboard.visits.day')}
              </th>
              <th scope="col" className="text-right font-medium">
                {t('dashboard.visits.title')}
              </th>
            </tr>
          </thead>
          <tbody>
            {days.map((d) => (
              <tr key={d.day}>
                <td>{label(d.day)}</td>
                <td className="text-right tabular-nums">{d.count}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </figure>
  );
}
