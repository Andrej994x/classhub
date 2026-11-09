import { Plus, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { useBookingActions, useBookingRequests, useBookingSettings, useSaveBookingSettings, type BookingRequest, type BookingSettings } from '../../../teacher/booking.ts';
import { localDay, localTime } from '../../../teacher/lessons.ts';
import { num } from '../../../teacher/offers.ts';
import { NotFoundContent } from '../Pages.tsx';

const field = 'h-10 rounded-md border border-input bg-background px-2 text-base sm:text-sm';
/** Monday first, as in North Macedonia; values are .NET DayOfWeek numbers. */
const WEEK = [1, 2, 3, 4, 5, 6, 0];

/** `/app/w/:workspaceId/booking`: booking requests and the self-service booking settings (R2-P6). */
export function BookingPage() {
  const { t } = useTranslation();
  const { workspaceId = '' } = useParams();
  const settings = useBookingSettings(workspaceId);

  if (settings.isError) return settings.error instanceof ApiError && settings.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(settings.error, t)}</Alert>;
  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('booking.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('booking.lead')}</p>
      </div>
      <Requests workspaceId={workspaceId} />
      {!settings.data || !settings.isFetchedAfterMount ? (
        <output className="block">{t('common.loading')}</output>
      ) : (
        <SettingsForm workspaceId={workspaceId} settings={settings.data} />
      )}
    </section>
  );
}

function Requests({ workspaceId }: { workspaceId: string }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const requests = useBookingRequests(workspaceId, page);
  const pages = Math.max(1, Math.ceil((num(requests.data?.total) ?? 0) / (num(requests.data?.pageSize) ?? 20)));
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('booking.requests.title')}</CardTitle>
        <CardDescription>{t('booking.requests.lead')}</CardDescription>
      </CardHeader>
      <CardContent>
        {requests.isError ? (
          <Alert variant="error">{errorMessage(requests.error, t)}</Alert>
        ) : !requests.data ? (
          <output className="block">{t('common.loading')}</output>
        ) : requests.data.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('booking.requests.none')}</p>
        ) : (
          <ul aria-label={t('booking.requests.title')} className="flex flex-col divide-y divide-border">
            {requests.data.items.map((request) => (
              <RequestItem key={request.id} workspaceId={workspaceId} request={request} />
            ))}
          </ul>
        )}
        {pages > 1 && (
          <nav aria-label={t('booking.requests.pages')} className="mt-3 flex items-center gap-2">
            <Button type="button" size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage(page - 1)}>
              {t('leads.previous')}
            </Button>
            <span className="text-sm">{t('leads.pageOf', { page, pages })}</span>
            <Button type="button" size="sm" variant="outline" disabled={page >= pages} onClick={() => setPage(page + 1)}>
              {t('leads.next')}
            </Button>
          </nav>
        )}
      </CardContent>
    </Card>
  );
}

function RequestItem({ workspaceId, request }: { workspaceId: string; request: BookingRequest }) {
  const { t, i18n } = useTranslation();
  const actions = useBookingActions(workspaceId, request.id);
  const [mode, setMode] = useState<'none' | 'decline' | 'reschedule' | 'cancel'>('none');
  const [reason, setReason] = useState('');
  const [date, setDate] = useState(localDay(request.startLocal));
  const [time, setTime] = useState(localTime(request.startLocal));
  const version = num(request.version) ?? 0;
  const error = actions.confirm.error ?? actions.decline.error ?? actions.reschedule.error ?? actions.cancel.error;
  const when = `${new Date(`${localDay(request.startLocal)}T12:00:00Z`).toLocaleDateString(i18n.resolvedLanguage === 'en' ? 'en-GB' : 'mk-MK', { timeZone: 'UTC', weekday: 'short', day: 'numeric', month: 'long' })} ${localTime(request.startLocal)}–${localTime(request.endLocal)}`;
  const close = () => {
    setMode('none');
    setReason('');
  };

  return (
    <li className="flex flex-col gap-2 py-3" aria-label={`${request.name}, ${when}`}>
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <div className="flex flex-col">
          <span className="font-medium">
            {when} · {request.offerTitle}
          </span>
          <span className="text-sm text-muted-foreground">
            <Link className="hover:underline" to={`/app/w/${workspaceId}/leads/${request.leadId}`}>
              {request.name}
            </Link>{' '}
            · {request.email}
            {request.phone && ` · ${request.phone}`}
          </span>
        </div>
        <span className="rounded-full bg-muted px-2 py-0.5 text-xs">{t(`booking.statuses.${request.status}`)}</span>
      </div>
      {request.reason && <p className="text-sm text-muted-foreground">{request.reason}</p>}
      {error && <Alert variant="error">{error instanceof ApiError && error.code === 'LESSON_OVERLAP' ? t('booking.requests.overlap') : errorMessage(error, t)}</Alert>}

      {mode === 'none' && request.status === 'Pending' && (
        <div className="flex flex-wrap gap-2">
          <Button type="button" size="sm" disabled={actions.confirm.isPending} onClick={() => actions.confirm.mutate(version)}>
            {t('booking.requests.confirm')}
          </Button>
          <Button type="button" size="sm" variant="outline" onClick={() => setMode('decline')}>
            {t('booking.requests.decline')}
          </Button>
        </div>
      )}
      {mode === 'none' && request.status === 'Confirmed' && (
        <div className="flex flex-wrap gap-2">
          {request.lessonId && (
            <Link className="self-center text-sm text-primary hover:underline" to={`/app/w/${workspaceId}/lessons/${request.lessonId}`}>
              {t('booking.requests.openLesson')}
            </Link>
          )}
          <Button type="button" size="sm" variant="outline" onClick={() => setMode('reschedule')}>
            {t('booking.requests.reschedule')}
          </Button>
          <Button type="button" size="sm" variant="outline" onClick={() => setMode('cancel')}>
            {t('booking.requests.cancel')}
          </Button>
        </div>
      )}
      {(mode === 'decline' || mode === 'cancel') && (
        <form
          className="flex flex-col gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (mode === 'decline') actions.decline.mutate({ version, reason: reason.trim() || null }, { onSuccess: close });
            else if (reason.trim()) actions.cancel.mutate({ version, reason: reason.trim() }, { onSuccess: close });
          }}
        >
          <Label htmlFor={`booking-reason-${request.id}`}>{mode === 'decline' ? t('booking.requests.declineReason') : t('booking.requests.cancelReason')}</Label>
          <textarea id={`booking-reason-${request.id}`} rows={2} maxLength={500} className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm" value={reason} onChange={(e) => setReason(e.target.value)} />
          <div className="flex gap-2">
            <Button type="submit" size="sm" variant="outline" disabled={mode === 'cancel' && !reason.trim()}>
              {mode === 'decline' ? t('booking.requests.declineConfirm') : t('booking.requests.cancelConfirm')}
            </Button>
            <Button type="button" size="sm" variant="ghost" onClick={close}>
              {t('common.cancel')}
            </Button>
          </div>
        </form>
      )}
      {mode === 'reschedule' && (
        <form
          className="flex flex-wrap items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            actions.reschedule.mutate({ version, startLocal: `${date}T${time}:00` }, { onSuccess: close });
          }}
        >
          <div className="flex flex-col gap-1">
            <Label htmlFor={`booking-date-${request.id}`}>{t('lessons.fields.date')}</Label>
            <input id={`booking-date-${request.id}`} type="date" className={field} value={date} onChange={(e) => setDate(e.target.value)} />
          </div>
          <div className="flex flex-col gap-1">
            <Label htmlFor={`booking-time-${request.id}`}>{t('lessons.fields.time')}</Label>
            <input id={`booking-time-${request.id}`} type="time" step={300} className={field} value={time} onChange={(e) => setTime(e.target.value)} />
          </div>
          <Button type="submit" size="sm">
            {t('booking.requests.rescheduleConfirm')}
          </Button>
          <Button type="button" size="sm" variant="ghost" onClick={close}>
            {t('common.cancel')}
          </Button>
        </form>
      )}
    </li>
  );
}

type Interval = { start: string; end: string };

/** Starts from the loaded settings; later refetches never touch the open form, and saving keeps the new version. */
function SettingsForm({ workspaceId, settings }: { workspaceId: string; settings: BookingSettings }) {
  const { t, i18n } = useTranslation();
  const save = useSaveBookingSettings(workspaceId);
  const [enabled, setEnabled] = useState(settings.enabled);
  const [step, setStep] = useState(String(num(settings.slotStepMinutes) ?? 30));
  const [notice, setNotice] = useState(String(num(settings.minNoticeHours) ?? 12));
  const [horizon, setHorizon] = useState(String(num(settings.horizonDays) ?? 30));
  const [hours, setHours] = useState<Record<number, Interval[]>>(() => {
    const byDay: Record<number, Interval[]> = {};
    for (const item of settings.workingHours) {
      const day = num(item.dayOfWeek) ?? 0;
      (byDay[day] ??= []).push({ start: item.start.slice(0, 5), end: item.end.slice(0, 5) });
    }
    return byDay;
  });
  const [off, setOff] = useState(settings.timeOff.map((o) => ({ startDate: o.startDate, endDate: o.endDate, note: o.note ?? '' })));
  const [invalid, setInvalid] = useState(false);
  const [version, setVersion] = useState(num(settings.version) ?? 0);
  const weekday = (day: number) =>
    new Date(Date.UTC(2030, 0, 6 + day, 12)).toLocaleDateString(i18n.resolvedLanguage === 'en' ? 'en-GB' : 'mk-MK', { weekday: 'long', timeZone: 'UTC' });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const intervals = WEEK.flatMap((day) => (hours[day] ?? []).map((i) => ({ day, ...i })));
    const bad =
      intervals.some((i) => !i.start || !i.end || i.end <= i.start) ||
      off.some((o) => !o.startDate || !o.endDate || o.endDate < o.startDate) ||
      !(Number(notice) >= 0 && Number(notice) <= 168) ||
      !(Number(horizon) >= 7 && Number(horizon) <= 90);
    setInvalid(bad);
    if (bad) return;
    save.mutate(
      {
        enabled,
        slotStepMinutes: Number(step),
        minNoticeHours: Number(notice),
        horizonDays: Number(horizon),
        workingHours: intervals.map((i) => ({ dayOfWeek: i.day, start: `${i.start}:00`, end: `${i.end}:00` })),
        timeOff: off.map((o) => ({ startDate: o.startDate, endDate: o.endDate, note: o.note.trim() || null })),
        version,
      },
      { onSuccess: (saved) => setVersion(num(saved.version) ?? 0) },
    );
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('booking.settings.title')}</CardTitle>
        <CardDescription>{t('booking.settings.lead', { zone: settings.timeZone })}</CardDescription>
      </CardHeader>
      <CardContent>
        <form noValidate className="flex flex-col gap-5" onSubmit={submit}>
          <label className="flex items-center gap-2 text-sm font-medium">
            <input type="checkbox" className="h-4 w-4" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
            {t('booking.settings.enabled')}
          </label>
          <div className="grid gap-4 sm:grid-cols-3">
            <div className="flex flex-col gap-1">
              <Label htmlFor="booking-step">{t('booking.settings.step')}</Label>
              <select id="booking-step" className={field} value={step} onChange={(e) => setStep(e.target.value)}>
                {[15, 30, 60].map((s) => (
                  <option key={s} value={String(s)}>
                    {t('lessons.minutes', { count: s })}
                  </option>
                ))}
              </select>
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor="booking-notice">{t('booking.settings.notice')}</Label>
              <input id="booking-notice" type="number" min={0} max={168} className={field} value={notice} onChange={(e) => setNotice(e.target.value)} />
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor="booking-horizon">{t('booking.settings.horizon')}</Label>
              <input id="booking-horizon" type="number" min={7} max={90} className={field} value={horizon} onChange={(e) => setHorizon(e.target.value)} />
            </div>
          </div>

          <fieldset className="flex flex-col gap-3">
            <legend className="mb-1 text-sm font-medium">{t('booking.settings.hours')}</legend>
            {WEEK.map((day) => (
              <div key={day} className="flex flex-col gap-2 sm:flex-row sm:items-start">
                <span className="w-32 pt-2 text-sm capitalize">{weekday(day)}</span>
                <div className="flex flex-1 flex-col gap-2">
                  {(hours[day] ?? []).map((interval, index) => (
                    <div key={index} className="flex items-center gap-2">
                      <input
                        type="time"
                        step={900}
                        aria-label={t('booking.settings.from', { day: weekday(day) })}
                        className={field}
                        value={interval.start}
                        onChange={(e) => setHours({ ...hours, [day]: (hours[day] ?? []).map((i, n) => (n === index ? { ...i, start: e.target.value } : i)) })}
                      />
                      <span aria-hidden>–</span>
                      <input
                        type="time"
                        step={900}
                        aria-label={t('booking.settings.to', { day: weekday(day) })}
                        className={field}
                        value={interval.end}
                        onChange={(e) => setHours({ ...hours, [day]: (hours[day] ?? []).map((i, n) => (n === index ? { ...i, end: e.target.value } : i)) })}
                      />
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        aria-label={t('booking.settings.removeHours', { day: weekday(day) })}
                        onClick={() => setHours({ ...hours, [day]: (hours[day] ?? []).filter((_, n) => n !== index) })}
                      >
                        <Trash2 className="h-4 w-4" aria-hidden />
                      </Button>
                    </div>
                  ))}
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    className="self-start"
                    aria-label={t('booking.settings.addHours', { day: weekday(day) })}
                    onClick={() => setHours({ ...hours, [day]: [...(hours[day] ?? []), { start: '09:00', end: '17:00' }] })}
                  >
                    <Plus className="h-4 w-4" aria-hidden />
                    {t('booking.settings.add')}
                  </Button>
                </div>
              </div>
            ))}
          </fieldset>

          <fieldset className="flex flex-col gap-2">
            <legend className="mb-1 text-sm font-medium">{t('booking.settings.timeOff')}</legend>
            {off.map((o, index) => (
              <div key={index} className="flex flex-wrap items-center gap-2">
                <input type="date" aria-label={t('booking.settings.offFrom')} className={field} value={o.startDate} onChange={(e) => setOff(off.map((x, n) => (n === index ? { ...x, startDate: e.target.value } : x)))} />
                <span aria-hidden>–</span>
                <input type="date" aria-label={t('booking.settings.offTo')} className={field} value={o.endDate} onChange={(e) => setOff(off.map((x, n) => (n === index ? { ...x, endDate: e.target.value } : x)))} />
                <input aria-label={t('booking.settings.offNote')} maxLength={120} className={`${field} flex-1`} value={o.note} onChange={(e) => setOff(off.map((x, n) => (n === index ? { ...x, note: e.target.value } : x)))} />
                <Button type="button" size="sm" variant="ghost" aria-label={t('booking.settings.removeOff')} onClick={() => setOff(off.filter((_, n) => n !== index))}>
                  <Trash2 className="h-4 w-4" aria-hidden />
                </Button>
              </div>
            ))}
            <Button type="button" size="sm" variant="outline" className="self-start" onClick={() => setOff([...off, { startDate: '', endDate: '', note: '' }])}>
              <Plus className="h-4 w-4" aria-hidden />
              {t('booking.settings.addOff')}
            </Button>
          </fieldset>

          {invalid && <Alert variant="error">{t('booking.settings.invalid')}</Alert>}
          {save.error && <Alert variant="error">{save.error instanceof ApiError && save.error.code === 'CONCURRENCY_CONFLICT' ? t('booking.settings.changedElsewhere') : errorMessage(save.error, t)}</Alert>}
          {save.isSuccess && <Alert variant="success">{t('booking.settings.saved')}</Alert>}
          <Button type="submit" className="self-start" disabled={save.isPending}>
            {save.isPending ? t('common.loading') : t('booking.settings.save')}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
