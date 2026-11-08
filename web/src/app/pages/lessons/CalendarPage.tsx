import { useQuery } from '@tanstack/react-query';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link, useParams, useSearchParams } from 'react-router';
import { api } from '../../../api/client.ts';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button, buttonVariants } from '../../../components/ui/button.tsx';
import { cn } from '../../../lib/utils.ts';
import { addDays, addMonths, isDate, localDay, localTime, monthGrid, startOfMonth, startOfWeek, today, useLessons, type Lesson } from '../../../teacher/lessons.ts';
import { NotFoundContent } from '../Pages.tsx';

type View = 'day' | 'week' | 'month';
const views: View[] = ['day', 'week', 'month'];

function useWorkspace(workspaceId: string) {
  return useQuery({
    queryKey: ['w', workspaceId],
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}', { params: { path: { workspaceId } } })).data!,
  });
}

/** `/app/w/:workspaceId/calendar?view=week&date=YYYY-MM-DD`: the teacher's lessons in the workspace time zone. */
export function CalendarPage() {
  const { t } = useTranslation();
  const { workspaceId = '' } = useParams();
  const workspace = useWorkspace(workspaceId);
  if (workspace.isError) return workspace.error instanceof ApiError && workspace.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(workspace.error, t)}</Alert>;
  if (!workspace.data) return <output className="block">{t('common.loading')}</output>;
  return <Calendar workspaceId={workspaceId} timeZone={workspace.data.timeZone} />;
}

function Calendar({ workspaceId, timeZone }: { workspaceId: string; timeZone: string }) {
  const { t, i18n } = useTranslation();
  const [params, setParams] = useSearchParams();
  const view: View = views.includes(params.get('view') as View) ? (params.get('view') as View) : 'week';
  const now = today(timeZone);
  const date = isDate(params.get('date')) ? params.get('date')! : now;
  const locale = i18n.resolvedLanguage === 'en' ? 'en-GB' : 'mk-MK';

  const days = view === 'day' ? [date] : view === 'week' ? Array.from({ length: 7 }, (_, i) => addDays(startOfWeek(date), i)) : monthGrid(date);
  const lessons = useLessons(workspaceId, days[0]!, days.at(-1)!);
  const byDay = new Map<string, Lesson[]>();
  for (const lesson of lessons.data ?? []) {
    const day = localDay(lesson.startLocal);
    byDay.set(day, [...(byDay.get(day) ?? []), lesson]);
  }

  const go = (next: { view?: View; date?: string }) => setParams({ view: next.view ?? view, date: next.date ?? date }, { replace: true });
  const step = (direction: 1 | -1) =>
    go({ date: view === 'day' ? addDays(date, direction) : view === 'week' ? addDays(date, 7 * direction) : addMonths(date, direction) });
  const label = (day: string, options: Intl.DateTimeFormatOptions) => new Date(`${day}T12:00:00Z`).toLocaleDateString(locale, { timeZone: 'UTC', ...options });
  const title =
    view === 'day'
      ? label(date, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
      : view === 'week'
        ? `${label(days[0]!, { day: 'numeric', month: 'short' })} – ${label(days[6]!, { day: 'numeric', month: 'short', year: 'numeric' })}`
        : label(startOfMonth(date), { month: 'long', year: 'numeric' });

  return (
    <section className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">{t('lessons.calendar.title')}</h1>
          <p className="text-sm text-muted-foreground">{t('lessons.calendar.timeZone', { zone: timeZone })}</p>
        </div>
        <Link className={buttonVariants()} to={`/app/w/${workspaceId}/lessons/new?date=${date}`}>
          {t('lessons.add')}
        </Link>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <fieldset className="inline-flex rounded-md border border-border p-0.5">
          <legend className="sr-only">{t('lessons.calendar.view')}</legend>
          {views.map((v) => (
            <button
              key={v}
              type="button"
              aria-pressed={view === v}
              className={cn('rounded px-3 py-1.5 text-sm', view === v ? 'bg-primary text-primary-foreground' : 'hover:bg-muted')}
              onClick={() => go({ view: v })}
            >
              {t(`lessons.calendar.views.${v}`)}
            </button>
          ))}
        </fieldset>
        <div className="flex items-center gap-1">
          <Button type="button" size="sm" variant="outline" aria-label={t('lessons.calendar.previous')} onClick={() => step(-1)}>
            <ChevronLeft className="h-4 w-4" aria-hidden />
          </Button>
          <Button type="button" size="sm" variant="outline" onClick={() => go({ date: now })}>
            {t('lessons.calendar.today')}
          </Button>
          <Button type="button" size="sm" variant="outline" aria-label={t('lessons.calendar.next')} onClick={() => step(1)}>
            <ChevronRight className="h-4 w-4" aria-hidden />
          </Button>
        </div>
        <h2 className="text-base font-medium" aria-live="polite">
          {title}
        </h2>
      </div>

      {lessons.isError && <Alert variant="error">{errorMessage(lessons.error, t)}</Alert>}
      {view === 'month' ? (
        <MonthView workspaceId={workspaceId} days={days} month={date.slice(0, 7)} now={now} byDay={byDay} label={label} onOpenDay={(day) => go({ view: 'day', date: day })} />
      ) : (
        <ol aria-label={t('lessons.calendar.days')} className={cn('grid gap-3', view === 'week' && 'md:grid-cols-7 md:gap-2')}>
          {days.map((day) => (
            <li key={day} className="flex min-w-0 flex-col gap-2 rounded-lg border border-border bg-card p-2">
              <h3 className={cn('text-sm font-medium', day === now && 'text-primary')}>
                <button type="button" className="hover:underline" onClick={() => go({ view: 'day', date: day })}>
                  {label(day, { weekday: 'short', day: 'numeric', month: 'short' })}
                </button>
              </h3>
              {(byDay.get(day) ?? []).length === 0 ? (
                <p className="text-xs text-muted-foreground">{t('lessons.calendar.free')}</p>
              ) : (
                <ul className="flex flex-col gap-1.5">
                  {byDay.get(day)!.map((lesson) => (
                    <li key={lesson.id}>
                      <LessonChip workspaceId={workspaceId} lesson={lesson} />
                    </li>
                  ))}
                </ul>
              )}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

function who(lesson: Lesson) {
  return lesson.groupName ?? lesson.participants.map((p) => [p.firstName, p.lastName].filter(Boolean).join(' ')).join(', ');
}

function LessonChip({ workspaceId, lesson, compact = false }: { workspaceId: string; lesson: Lesson; compact?: boolean }) {
  const { t } = useTranslation();
  const status = lesson.status as 'Scheduled' | 'Completed' | 'Cancelled';
  return (
    <Link
      to={`/app/w/${workspaceId}/lessons/${lesson.id}`}
      className={cn(
        'flex flex-col rounded-md border-l-4 bg-muted/60 px-2 py-1 text-xs hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring',
        status === 'Scheduled' && 'border-primary',
        status === 'Completed' && 'border-emerald-600',
        status === 'Cancelled' && 'border-border text-muted-foreground line-through',
      )}
    >
      <span className="font-medium">
        {localTime(lesson.startLocal)}–{localTime(lesson.endLocal)} {lesson.title}
      </span>
      {!compact && <span className="truncate">{who(lesson)}</span>}
      {status !== 'Scheduled' && <span className="sr-only">{t(`lessons.statuses.${status}`)}</span>}
    </Link>
  );
}

function MonthView({
  workspaceId,
  days,
  month,
  now,
  byDay,
  label,
  onOpenDay,
}: {
  workspaceId: string;
  days: string[];
  month: string;
  now: string;
  byDay: Map<string, Lesson[]>;
  label: (day: string, options: Intl.DateTimeFormatOptions) => string;
  onOpenDay: (day: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <div>
      <div aria-hidden className="grid grid-cols-7 gap-1 pb-1 text-center text-xs text-muted-foreground">
        {days.slice(0, 7).map((day) => (
          <span key={day}>{label(day, { weekday: 'short' })}</span>
        ))}
      </div>
      <ol aria-label={t('lessons.calendar.days')} className="grid grid-cols-7 gap-1">
        {days.map((day) => {
          const lessons = byDay.get(day) ?? [];
          return (
            <li key={day} className={cn('flex min-h-14 min-w-0 flex-col gap-1 rounded-md border border-border p-1 sm:min-h-24', !day.startsWith(month) && 'opacity-50')}>
              <button
                type="button"
                className={cn('self-start rounded px-1 text-xs hover:underline', day === now && 'bg-primary text-primary-foreground')}
                aria-label={`${label(day, { weekday: 'long', day: 'numeric', month: 'long' })}: ${t('lessons.calendar.count', { count: lessons.length })}`}
                onClick={() => onOpenDay(day)}
              >
                {Number(day.slice(8))}
              </button>
              {/* Phones: a count; wider screens: up to three lessons. */}
              {lessons.length > 0 && <span className="text-center text-xs font-medium sm:hidden">{lessons.length}</span>}
              <ul className="hidden flex-col gap-1 sm:flex">
                {lessons.slice(0, 3).map((lesson) => (
                  <li key={lesson.id}>
                    <LessonChip workspaceId={workspaceId} lesson={lesson} compact />
                  </li>
                ))}
                {lessons.length > 3 && (
                  <li>
                    <button type="button" className="text-xs text-primary hover:underline" onClick={() => onOpenDay(day)}>
                      {t('lessons.calendar.more', { count: lessons.length - 3 })}
                    </button>
                  </li>
                )}
              </ul>
            </li>
          );
        })}
      </ol>
    </div>
  );
}
