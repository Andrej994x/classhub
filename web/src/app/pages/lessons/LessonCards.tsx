import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { localDay, localTime, useLessonAttendance, useLessonHistory, useLessonNotes, type Attendance, type AttendanceItem, type Lesson } from '../../../teacher/lessons.ts';
import { num } from '../../../teacher/offers.ts';

const STATUSES: Attendance[] = ['Present', 'Absent', 'Excused'];
const name = (p: { firstName: string; lastName?: string | null }) => [p.firstName, p.lastName].filter(Boolean).join(' ');

/** Each participant's attendance (D-22); students not marked otherwise are present. */
export function AttendanceFields({ lesson, value, onChange }: { lesson: Lesson; value: Record<string, Attendance>; onChange: (next: Record<string, Attendance>) => void }) {
  const { t } = useTranslation();
  return (
    <ul aria-label={t('lessons.attendance.title')} className="flex flex-col gap-2">
      {lesson.participants.map((p) => (
        <li key={p.studentId}>
          <fieldset className="flex flex-wrap items-center gap-3">
            <legend className="mr-2 text-sm font-medium">{name(p)}</legend>
            {STATUSES.map((status) => (
              <label key={status} className="flex items-center gap-1 text-sm">
                <input type="radio" name={`attendance-${p.studentId}`} className="h-4 w-4" checked={(value[p.studentId] ?? 'Present') === status} onChange={() => onChange({ ...value, [p.studentId]: status })} />
                {t(`lessons.attendance.statuses.${status}`)}
              </label>
            ))}
          </fieldset>
        </li>
      ))}
    </ul>
  );
}

export const attendanceItems = (lesson: Lesson, value: Record<string, Attendance>): AttendanceItem[] =>
  lesson.participants.map((p) => ({ studentId: p.studentId, status: value[p.studentId] ?? 'Present' }));

export const attendanceOf = (lesson: Lesson): Record<string, Attendance> =>
  Object.fromEntries(lesson.participants.filter((p) => p.attendance).map((p) => [p.studentId, p.attendance as Attendance]));

/** Correct the attendance of a held lesson (Finance is told). */
export function AttendanceCard({ workspaceId, lesson, version, onChanged }: { workspaceId: string; lesson: Lesson; version: number; onChanged: (changed: Lesson) => void }) {
  const { t } = useTranslation();
  const save = useLessonAttendance(workspaceId, lesson.id);
  const [value, setValue] = useState(() => attendanceOf(lesson));
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('lessons.attendance.title')}</CardTitle>
        <CardDescription>{t('lessons.attendance.lead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <AttendanceFields lesson={lesson} value={value} onChange={setValue} />
        {save.error && <Alert variant="error">{save.error instanceof ApiError && save.error.code === 'CONCURRENCY_CONFLICT' ? t('lessons.changedElsewhere') : errorMessage(save.error, t)}</Alert>}
        {save.isSuccess && <Alert variant="success">{t('lessons.saved')}</Alert>}
        <Button type="button" className="self-start" disabled={save.isPending} onClick={() => save.mutate({ version, attendance: attendanceItems(lesson, value) }, { onSuccess: onChanged })}>
          {t('lessons.attendance.save')}
        </Button>
      </CardContent>
    </Card>
  );
}

/** The private note (only the teacher) and the summary shared with the lesson's students (D-40). */
export function NotesCard({ workspaceId, lesson, version, onChanged }: { workspaceId: string; lesson: Lesson; version: number; onChanged: (changed: Lesson) => void }) {
  const { t } = useTranslation();
  const save = useLessonNotes(workspaceId, lesson.id);
  const [privateNote, setPrivateNote] = useState(lesson.privateNote ?? '');
  const [sharedSummary, setSharedSummary] = useState(lesson.sharedSummary ?? '');
  const area = 'w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm';
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('lessons.notes.title')}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <div className="flex flex-col gap-1">
          <Label htmlFor="lesson-private-note">{t('lessons.notes.private')}</Label>
          <textarea id="lesson-private-note" rows={3} maxLength={4000} className={area} value={privateNote} onChange={(e) => setPrivateNote(e.target.value)} />
          <p className="text-xs text-muted-foreground">{t('lessons.notes.privateHint')}</p>
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="lesson-shared-summary">{t('lessons.notes.shared')}</Label>
          <textarea id="lesson-shared-summary" rows={3} maxLength={2000} className={area} value={sharedSummary} onChange={(e) => setSharedSummary(e.target.value)} />
          <p className="text-xs text-muted-foreground">{t('lessons.notes.sharedHint')}</p>
        </div>
        {save.error && <Alert variant="error">{save.error instanceof ApiError && save.error.code === 'CONCURRENCY_CONFLICT' ? t('lessons.changedElsewhere') : errorMessage(save.error, t)}</Alert>}
        {save.isSuccess && <Alert variant="success">{t('lessons.saved')}</Alert>}
        <Button
          type="button"
          className="self-start"
          disabled={save.isPending}
          onClick={() => save.mutate({ version, privateNote: privateNote.trim() || null, sharedSummary: sharedSummary.trim() || null }, { onSuccess: onChanged })}
        >
          {t('lessons.notes.save')}
        </Button>
      </CardContent>
    </Card>
  );
}

/** A student's or group's lessons, newest first, with the student's attendance. */
export function LessonHistory({ workspaceId, studentId, groupId }: { workspaceId: string; studentId?: string; groupId?: string }) {
  const { t, i18n } = useTranslation();
  const [page, setPage] = useState(1);
  const history = useLessonHistory(workspaceId, { studentId, groupId }, page);
  const pages = Math.max(1, Math.ceil((num(history.data?.total) ?? 0) / (num(history.data?.pageSize) ?? 10)));
  const date = (value: string) =>
    new Date(`${localDay(value)}T12:00:00Z`).toLocaleDateString(i18n.resolvedLanguage === 'en' ? 'en-GB' : 'mk-MK', { timeZone: 'UTC', day: 'numeric', month: 'short', year: 'numeric' });
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('lessons.history.title')}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {history.isError ? (
          <Alert variant="error">{errorMessage(history.error, t)}</Alert>
        ) : !history.data ? (
          <output className="block text-sm">{t('common.loading')}</output>
        ) : history.data.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('lessons.history.none')}</p>
        ) : (
          <ul aria-label={t('lessons.history.title')} className="flex flex-col divide-y divide-border">
            {history.data.items.map((item) => (
              <li key={item.id} className="flex flex-wrap items-baseline justify-between gap-2 py-2 text-sm">
                <Link className="font-medium hover:underline" to={`/app/w/${workspaceId}/lessons/${item.id}`}>
                  {date(item.startLocal)} {localTime(item.startLocal)} · {item.title}
                  {item.groupName && studentId && ` (${item.groupName})`}
                </Link>
                <span className="text-muted-foreground">
                  {t(`lessons.statuses.${item.status}`)}
                  {item.attendance && ` · ${t(`lessons.attendance.statuses.${item.attendance}`)}`}
                </span>
              </li>
            ))}
          </ul>
        )}
        {pages > 1 && (
          <nav aria-label={t('lessons.history.pages')} className="flex items-center gap-2">
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
