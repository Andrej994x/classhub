import { CalendarClock, MapPin, Video } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { formatDateTime } from '../../../lib/format.ts';
import { useMyLessons, type MyLesson } from '../../../student/portal.ts';
import { num } from '../../../teacher/offers.ts';

const n = (value: number | string | null | undefined) => num(value) ?? 0;

const linkClass = 'text-primary underline-offset-4 hover:underline';

/** Where the lesson happens: the join link while it is ahead, or the place. */
function Place({ lesson }: { lesson: MyLesson }) {
  const { t } = useTranslation();
  if (lesson.mode === 'Online') {
    return (
      <span className="inline-flex items-center gap-1 text-sm">
        <Video className="h-4 w-4" aria-hidden />
        {lesson.meetingUrl ? (
          <a href={lesson.meetingUrl} target="_blank" rel="noopener noreferrer" className={linkClass}>
            {t('studentPortal.lessons.join')}
          </a>
        ) : (
          t('lessons.modes.Online')
        )}
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 text-sm">
      <MapPin className="h-4 w-4" aria-hidden />
      {lesson.location ?? t('lessons.modes.InPerson')}
    </span>
  );
}

/**
 * The student's lessons with one teacher (R2-P12): the next lesson first, then the ones after it, and the recent past
 * lessons with their own attendance and the summary the teacher shared. Times are in the lesson's time zone.
 */
export function MyLessons({ workspaceId }: { workspaceId: string }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'mk';
  const lessons = useMyLessons(workspaceId);

  if (lessons.isError) return <Alert variant="error">{errorMessage(lessons.error, t)}</Alert>;
  if (!lessons.data) return <output className="block">{t('common.loading')}</output>;
  const [next, ...later] = lessons.data.upcoming;
  const { past, attendance } = lessons.data;
  const when = (lesson: MyLesson) => formatDateTime(lesson.startUtc, language, lesson.timeZone);

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('studentPortal.lessons.next')}</CardTitle>
          {!next && <CardDescription>{t('student.empty')}</CardDescription>}
        </CardHeader>
        {next && (
          <CardContent className="flex flex-col gap-3">
            <div className="flex flex-col gap-1">
              <p className="text-xl font-semibold">{next.title}</p>
              <p className="inline-flex items-center gap-1 tabular-nums">
                <CalendarClock className="h-4 w-4" aria-hidden />
                {when(next)}
              </p>
              {next.topic && <p className="text-sm text-muted-foreground">{next.topic}</p>}
              <Place lesson={next} />
            </div>
            {later.length > 0 && (
              <div className="flex flex-col gap-2">
                <h3 className="text-sm font-medium">{t('studentPortal.lessons.later')}</h3>
                <ul aria-label={t('studentPortal.lessons.later')} className="flex flex-col divide-y divide-border text-sm">
                  {later.map((lesson) => (
                    <li key={lesson.id} className="flex flex-wrap justify-between gap-x-3 py-1.5">
                      <span>{lesson.title}</span>
                      <span className="tabular-nums text-muted-foreground">{when(lesson)}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </CardContent>
        )}
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('studentPortal.lessons.past')}</CardTitle>
          <CardDescription>
            {n(attendance.held) === 0
              ? t('studentPortal.lessons.noneHeld')
              : t('studentPortal.lessons.attendance', { present: n(attendance.present), held: n(attendance.held), absent: n(attendance.absent), excused: n(attendance.excused) })}
          </CardDescription>
        </CardHeader>
        {past.length > 0 && (
          <CardContent>
            <ul aria-label={t('studentPortal.lessons.past')} className="flex flex-col divide-y divide-border">
              {past.map((lesson) => (
                <li key={lesson.id} className="flex flex-col gap-1 py-2 first:pt-0 last:pb-0">
                  <div className="flex flex-wrap items-baseline justify-between gap-x-3">
                    <span className="font-medium">{lesson.title}</span>
                    <span className="text-sm tabular-nums text-muted-foreground">{when(lesson)}</span>
                  </div>
                  <span className="text-sm">
                    {lesson.attendance ? t(`lessons.attendance.statuses.${lesson.attendance}`) : t('studentPortal.lessons.notRecorded')}
                  </span>
                  {lesson.sharedSummary && <p className="whitespace-pre-line rounded-md bg-muted px-3 py-2 text-sm">{lesson.sharedSummary}</p>}
                </li>
              ))}
            </ul>
          </CardContent>
        )}
      </Card>
    </>
  );
}
