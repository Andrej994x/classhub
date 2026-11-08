import { ArrowLeft } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation, useNavigate, useParams } from 'react-router';
import { api } from '../../../api/client.ts';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { addDays, isDate, today, useEndSeries, useSeries, useUpdateSeries, type Series } from '../../../teacher/lessons.ts';
import { num } from '../../../teacher/offers.ts';
import { NotFoundContent } from '../Pages.tsx';

const field = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';
const dayNumber = (date: string) => Math.round(new Date(`${date}T12:00:00Z`).getTime() / 86_400_000);

/** The first occurrence on or after `date` (index and date). */
function nextOccurrence(series: Series, date: string): { index: number; date: string } {
  const step = 7 * (num(series.intervalWeeks) ?? 1);
  const index = date <= series.startDate ? 0 : Math.ceil((dayNumber(date) - dayNumber(series.startDate)) / step);
  return { index, date: addDays(series.startDate, index * step) };
}

/** `/app/w/:workspaceId/series/:seriesId`: a recurring series (R2-P5). Keyed by id, like the other editors. */
export function SeriesPage() {
  const { seriesId } = useParams();
  return <SeriesLoader key={seriesId} />;
}

function SeriesLoader() {
  const { t } = useTranslation();
  const { workspaceId = '', seriesId = '' } = useParams();
  const series = useSeries(workspaceId, seriesId);
  const workspace = useQuery({
    queryKey: ['w', workspaceId],
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}', { params: { path: { workspaceId } } })).data!,
  });

  if (series.isError) return series.error instanceof ApiError && series.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(series.error, t)}</Alert>;
  if (!series.data || !series.isFetchedAfterMount || !workspace.data) return <output className="block">{t('common.loading')}</output>;
  return <SeriesView workspaceId={workspaceId} series={series.data} todayDate={today(workspace.data.timeZone)} />;
}

function SeriesView({ workspaceId, series, todayDate }: { workspaceId: string; series: Series; todayDate: string }) {
  const { t, i18n } = useTranslation();
  const location = useLocation();
  const created = (location.state as { created?: number } | null)?.created;
  const locale = i18n.resolvedLanguage === 'en' ? 'en-GB' : 'mk-MK';
  const date = (value: string, options: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'long', year: 'numeric' }) =>
    new Date(`${value}T12:00:00Z`).toLocaleDateString(locale, { timeZone: 'UTC', ...options });
  const weekday = date(series.startDate, { weekday: 'long' });
  const time = series.startTime.slice(0, 5);

  return (
    <section className="flex flex-col gap-6">
      <Link to={`/app/w/${workspaceId}/calendar?date=${series.startDate}`} className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline">
        <ArrowLeft className="h-4 w-4" aria-hidden />
        {t('lessons.back')}
      </Link>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{series.title}</h1>
        <p className="text-sm text-muted-foreground">
          {t(num(series.intervalWeeks) === 2 ? 'lessons.series.summaryBiweekly' : 'lessons.series.summaryWeekly', { weekday, time, minutes: num(series.durationMinutes) })}
          {' · '}
          {series.count !== null
            ? t('lessons.series.endsAfter', { count: num(series.count) ?? 0 })
            : series.endDate
              ? t('lessons.series.endsOn', { date: date(series.endDate) })
              : t('lessons.series.noEnd')}
        </p>
      </div>
      {created !== undefined && <Alert variant="success">{t('lessons.series.created', { count: created })}</Alert>}
      {series.replacedBySeriesId && (
        <Alert variant="info">
          {t('lessons.series.replaced')}{' '}
          <Link className="underline" to={`/app/w/${workspaceId}/series/${series.replacedBySeriesId}`}>
            {t('lessons.series.openNew')}
          </Link>
        </Alert>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('lessons.series.skippedTitle')}</CardTitle>
          <CardDescription>{t('lessons.series.skippedLead')}</CardDescription>
        </CardHeader>
        <CardContent>
          {series.skips.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('lessons.series.noSkips')}</p>
          ) : (
            <ul aria-label={t('lessons.series.skippedTitle')} className="flex flex-col gap-1 text-sm">
              {series.skips.map((skip) => (
                <li key={skip.date}>
                  <Link className="font-medium hover:underline" to={`/app/w/${workspaceId}/calendar?view=day&date=${skip.date}`}>
                    {date(skip.date, { weekday: 'short', day: 'numeric', month: 'long' })}
                  </Link>
                  {' · '}
                  {t(`lessons.series.reasons.${skip.reason}`)}
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      {!series.replacedBySeriesId && <EditSeries workspaceId={workspaceId} series={series} todayDate={todayDate} />}
      {!series.replacedBySeriesId && <EndSeries workspaceId={workspaceId} series={series} todayDate={todayDate} />}
    </section>
  );
}

/** "All future" (D-24): from the next lesson on, a new series with these settings continues this one. */
function EditSeries({ workspaceId, series, todayDate }: { workspaceId: string; series: Series; todayDate: string }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const update = useUpdateSeries(workspaceId, series.id);
  const next = nextOccurrence(series, todayDate);
  const left = series.count === null ? null : Math.max(1, (num(series.count) ?? 0) - next.index);
  const [values, setValues] = useState({
    title: series.title,
    startDate: next.date,
    time: series.startTime.slice(0, 5),
    duration: String(num(series.durationMinutes) ?? 60),
    interval: String(num(series.intervalWeeks) ?? 1),
    endKind: series.count !== null ? 'count' : series.endDate ? 'date' : 'never',
    count: String(left ?? 10),
    endDate: series.endDate ?? '',
    meetingUrl: series.meetingUrl ?? '',
    location: series.location ?? '',
  });
  const set = (patch: Partial<typeof values>) => setValues({ ...values, ...patch });
  const [submitted, setSubmitted] = useState(false);
  const invalid =
    !values.title.trim() ||
    !isDate(values.startDate) ||
    values.startDate < todayDate ||
    (values.endKind === 'count' && !(Number(values.count) >= 1 && Number(values.count) <= 200)) ||
    (values.endKind === 'date' && !(isDate(values.endDate) && values.endDate >= values.startDate));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitted(true);
    if (invalid) return;
    try {
      const result = await update.mutateAsync({
        scope: 'AllFuture',
        fromLessonId: null,
        version: num(series.version) ?? 0,
        series: {
          kind: series.kind as 'Individual' | 'Group',
          studentId: series.studentId,
          groupId: series.groupId,
          subjectId: series.subjectId,
          title: values.title.trim(),
          topic: series.topic,
          startDate: values.startDate,
          startTime: `${values.time}:00`,
          durationMinutes: Number(values.duration),
          intervalWeeks: Number(values.interval) as 1 | 2,
          endDate: values.endKind === 'date' ? values.endDate : null,
          count: values.endKind === 'count' ? Number(values.count) : null,
          mode: series.mode as 'Online' | 'InPerson',
          meetingUrl: series.mode === 'Online' ? values.meetingUrl.trim() || null : null,
          location: series.mode === 'InPerson' ? values.location.trim() || null : null,
        },
      });
      void navigate(`/app/w/${workspaceId}/series/${result.series.id}`, { replace: true, state: { created: result.created } });
    } catch {
      // shown below
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('lessons.series.editTitle')}</CardTitle>
        <CardDescription>{t('lessons.series.editLead')}</CardDescription>
      </CardHeader>
      <CardContent>
        <form noValidate className="grid gap-4 sm:grid-cols-2" onSubmit={(e) => void submit(e)}>
          <FormField label={t('lessons.fields.title')} maxLength={120} value={values.title} onChange={(e) => set({ title: e.target.value })} />
          <FormField label={t('lessons.series.firstDate')} type="date" min={todayDate} value={values.startDate} onChange={(e) => set({ startDate: e.target.value })} />
          <FormField label={t('lessons.fields.time')} type="time" step={300} value={values.time} onChange={(e) => set({ time: e.target.value })} />
          <div className="flex flex-col gap-2">
            <Label htmlFor="series-duration">{t('lessons.fields.duration')}</Label>
            <select id="series-duration" className={field} value={values.duration} onChange={(e) => set({ duration: e.target.value })}>
              {[...new Set([30, 45, 60, 90, 120, Number(values.duration)])].toSorted((a, b) => a - b).map((d) => (
                <option key={d} value={String(d)}>
                  {t('lessons.minutes', { count: d })}
                </option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="series-interval">{t('lessons.series.repeat')}</Label>
            <select id="series-interval" className={field} value={values.interval} onChange={(e) => set({ interval: e.target.value })}>
              <option value="1">{t('lessons.series.weekly')}</option>
              <option value="2">{t('lessons.series.biweekly')}</option>
            </select>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="series-end">{t('lessons.series.ends')}</Label>
            <select id="series-end" className={field} value={values.endKind} onChange={(e) => set({ endKind: e.target.value })}>
              <option value="count">{t('lessons.series.afterCount')}</option>
              <option value="date">{t('lessons.series.onDate')}</option>
              <option value="never">{t('lessons.series.noEnd')}</option>
            </select>
            {values.endKind === 'count' && (
              <input type="number" min={1} max={200} aria-label={t('lessons.series.countLabel')} className={field} value={values.count} onChange={(e) => set({ count: e.target.value })} />
            )}
            {values.endKind === 'date' && (
              <input type="date" aria-label={t('lessons.series.endDateLabel')} className={field} value={values.endDate} onChange={(e) => set({ endDate: e.target.value })} />
            )}
          </div>
          {series.mode === 'Online' ? (
            <FormField label={t('lessons.fields.meetingUrl')} type="url" inputMode="url" placeholder="https://" maxLength={500} value={values.meetingUrl} onChange={(e) => set({ meetingUrl: e.target.value })} />
          ) : (
            <FormField label={t('lessons.fields.location')} maxLength={200} value={values.location} onChange={(e) => set({ location: e.target.value })} />
          )}
          {submitted && invalid && (
            <Alert variant="error" className="sm:col-span-2">
              {t('lessons.series.invalid')}
            </Alert>
          )}
          {update.error && (
            <Alert variant="error" className="sm:col-span-2">
              {update.error instanceof ApiError && update.error.code === 'CONCURRENCY_CONFLICT' ? t('lessons.changedElsewhere') : errorMessage(update.error, t)}
            </Alert>
          )}
          <Button type="submit" className="self-start" disabled={update.isPending}>
            {update.isPending ? t('common.loading') : t('lessons.series.applyFuture')}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}

function EndSeries({ workspaceId, series, todayDate }: { workspaceId: string; series: Series; todayDate: string }) {
  const { t } = useTranslation();
  const end = useEndSeries(workspaceId, series.id);
  const [fromDate, setFromDate] = useState(todayDate);
  const [confirming, setConfirming] = useState(false);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('lessons.series.endTitle')}</CardTitle>
        <CardDescription>{t('lessons.series.endLead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <FormField label={t('lessons.series.endFrom')} type="date" min={todayDate} value={fromDate} onChange={(e) => setFromDate(e.target.value)} />
        {end.error && <Alert variant="error">{end.error instanceof ApiError && end.error.code === 'CONCURRENCY_CONFLICT' ? t('lessons.changedElsewhere') : errorMessage(end.error, t)}</Alert>}
        {end.isSuccess && <Alert variant="success">{t('lessons.series.ended')}</Alert>}
        {confirming ? (
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm">{t('lessons.series.endConfirm')}</span>
            <Button
              type="button"
              variant="outline"
              disabled={end.isPending || !isDate(fromDate)}
              onClick={() => end.mutate({ fromDate, version: num(series.version) ?? 0 }, { onSuccess: () => setConfirming(false) })}
            >
              {t('lessons.series.endYes')}
            </Button>
            <Button type="button" variant="ghost" onClick={() => setConfirming(false)}>
              {t('common.cancel')}
            </Button>
          </div>
        ) : (
          <Button type="button" variant="outline" className="self-start" disabled={!isDate(fromDate)} onClick={() => setConfirming(true)}>
            {t('lessons.series.end')}
          </Button>
        )}
      </CardContent>
    </Card>
  );
}
