import { ArrowLeft } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { useGroups } from '../../../teacher/groups.ts';
import { isDate, localDay, localTime, useCancelLesson, useCompleteLesson, useLesson, useSaveLesson, type Lesson, type LessonInput } from '../../../teacher/lessons.ts';
import { num } from '../../../teacher/offers.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import { useStarted } from '../../../lib/useStarted.ts';
import { StudentPicker } from '../../components/StudentPicker.tsx';
import { NotFoundContent } from '../Pages.tsx';

const field = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';
const durations = [30, 45, 60, 90, 120];

/** `/lessons/new?date=…` or `/lessons/:lessonId`. */
/**
 * Keyed by the route's id: opening another entity (also right after creating one, which replaces `/new`) is a fresh
 * mount, so the editor again waits for this visit's refetch instead of staying on "loading".
 */
export function LessonEditPage() {
  const { lessonId } = useParams();
  return <LessonEditLoader key={lessonId ?? 'new'} />;
}

function LessonEditLoader() {
  const { t } = useTranslation();
  const { workspaceId = '', lessonId } = useParams();
  const lesson = useLesson(workspaceId, lessonId);

  if (lessonId && lesson.isError) return lesson.error instanceof ApiError && lesson.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(lesson.error, t)}</Alert>;
  // Start from the server's current state; later refetches never touch the open form.
  if (lessonId && (!lesson.data || !lesson.isFetchedAfterMount)) return <output className="block">{t('common.loading')}</output>;
  return <LessonEditor key={lessonId ?? 'new'} workspaceId={workspaceId} lesson={lessonId ? lesson.data : undefined} />;
}

interface Values {
  kind: 'Individual' | 'Group';
  studentId: string;
  groupId: string;
  subjectId: string;
  title: string;
  topic: string;
  date: string;
  time: string;
  duration: string;
  mode: 'Online' | 'InPerson';
  meetingUrl: string;
  location: string;
}

function initial(lesson: Lesson | undefined, date: string | null): Values {
  if (!lesson) {
    return {
      kind: 'Individual', studentId: '', groupId: '', subjectId: '', title: '', topic: '', date: isDate(date) ? date : '', time: '17:00',
      duration: '60', mode: 'Online', meetingUrl: '', location: '',
    };
  }
  const minutes = Math.round((new Date(lesson.endUtc).getTime() - new Date(lesson.startUtc).getTime()) / 60_000);
  return {
    kind: lesson.kind as Values['kind'],
    studentId: lesson.kind === 'Individual' ? (lesson.participants[0]?.studentId ?? '') : '',
    groupId: lesson.groupId ?? '',
    subjectId: lesson.subjectId ?? '',
    title: lesson.title,
    topic: lesson.topic ?? '',
    date: localDay(lesson.startLocal),
    time: localTime(lesson.startLocal),
    duration: String(minutes),
    mode: lesson.mode as Values['mode'],
    meetingUrl: lesson.meetingUrl ?? '',
    location: lesson.location ?? '',
  };
}

function LessonEditor({ workspaceId, lesson }: { workspaceId: string; lesson: Lesson | undefined }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const catalog = useCatalog();
  const groups = useGroups(workspaceId, false);
  const save = useSaveLesson(workspaceId, lesson?.id);
  const [values, setValues] = useState(() => initial(lesson, params.get('date')));
  const [version, setVersion] = useState<number | null>(lesson ? (num(lesson.version) ?? 0) : null);
  const [current, setCurrent] = useState(lesson);
  const [submitted, setSubmitted] = useState(false);
  const set = (patch: Partial<Values>) => setValues({ ...values, ...patch });
  const editable = !current || current.status === 'Scheduled';
  const durationOptions = durations.includes(Number(values.duration)) ? durations : [...durations, Number(values.duration)].toSorted((a, b) => a - b);

  const missing = {
    who: values.kind === 'Individual' ? !values.studentId : !values.groupId,
    title: !values.title.trim(),
    date: !isDate(values.date),
    time: !/^\d{2}:\d{2}$/.test(values.time),
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitted(true);
    if (Object.values(missing).some(Boolean)) return;
    const input: LessonInput = {
      kind: values.kind,
      studentId: values.kind === 'Individual' ? values.studentId : null,
      groupId: values.kind === 'Group' ? values.groupId : null,
      subjectId: values.subjectId || null,
      title: values.title.trim(),
      topic: values.topic.trim() || null,
      startLocal: `${values.date}T${values.time}:00`,
      durationMinutes: Number(values.duration),
      mode: values.mode,
      meetingUrl: values.mode === 'Online' ? values.meetingUrl.trim() || null : null,
      location: values.mode === 'InPerson' ? values.location.trim() || null : null,
    };
    try {
      const saved = await save.mutateAsync({ input, version });
      if (!lesson) {
        // The page remounts for the new lesson (keyed by id) and loads it fresh.
        void navigate(`/app/w/${workspaceId}/lessons/${saved.id}`, { replace: true });
        return;
      }
      setVersion(num(saved.version) ?? 0);
      setCurrent(saved);
    } catch {
      // shown below
    }
  };
  const saveError =
    save.error instanceof ApiError && save.error.code === 'CONCURRENCY_CONFLICT'
      ? t('lessons.changedElsewhere')
      : save.error instanceof ApiError && save.error.code === 'VALIDATION_FAILED' && save.error.fieldErrors.startLocal
        ? t('lessons.validation.timeSkipped')
        : save.error instanceof ApiError && save.error.code === 'VALIDATION_FAILED' && save.error.fieldErrors.meetingUrl
          ? t('lessons.validation.meetingUrl')
          : save.error
            ? errorMessage(save.error, t)
            : null;
  const currentStudent = current?.kind === 'Individual' ? current.participants[0] : undefined;

  return (
    <section className="flex flex-col gap-6">
      <Link to={`/app/w/${workspaceId}/calendar?date=${values.date || ''}`} className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline">
        <ArrowLeft className="h-4 w-4" aria-hidden />
        {t('lessons.back')}
      </Link>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{current ? current.title : t('lessons.newTitle')}</h1>
        {current && (
          <p className="text-sm text-muted-foreground">
            {t(`lessons.statuses.${current.status}`)}
            {current.status === 'Cancelled' && current.cancellationReason && ` · ${current.cancellationReason}`}
          </p>
        )}
      </div>

      <form noValidate onSubmit={(e) => void submit(e)}>
        <fieldset disabled={!editable} className="contents">
          <Card>
            <CardHeader>
              <CardTitle className="text-lg">{t('lessons.details')}</CardTitle>
              {!editable && <CardDescription>{t('lessons.finalLead')}</CardDescription>}
            </CardHeader>
            <CardContent className="grid gap-4 sm:grid-cols-2">
              <fieldset className="flex flex-col gap-2 sm:col-span-2">
                <legend className="text-sm font-medium">{t('lessons.fields.kind')}</legend>
                <div className="flex gap-4">
                  {(['Individual', 'Group'] as const).map((kind) => (
                    <label key={kind} className="flex items-center gap-2 text-sm">
                      <input type="radio" name="lesson-kind" className="h-4 w-4" checked={values.kind === kind} onChange={() => set({ kind })} />
                      {t(`lessons.kinds.${kind}`)}
                    </label>
                  ))}
                </div>
              </fieldset>
              <div className="sm:col-span-2">
                {values.kind === 'Individual' ? (
                  <StudentPicker
                    workspaceId={workspaceId}
                    id="lesson-student"
                    label={t('lessons.fields.student')}
                    value={values.studentId}
                    onChange={(studentId) => set({ studentId })}
                    currentLabel={currentStudent && [currentStudent.firstName, currentStudent.lastName].filter(Boolean).join(' ')}
                    error={submitted && missing.who ? t('lessons.validation.student') : undefined}
                  />
                ) : (
                  <div className="flex flex-col gap-2">
                    <Label htmlFor="lesson-group">{t('lessons.fields.group')}</Label>
                    <select id="lesson-group" className={field} value={values.groupId} onChange={(e) => set({ groupId: e.target.value })}>
                      <option value="">{t('lessons.fields.chooseGroup')}</option>
                      {current?.groupId && !groups.data?.some((g) => g.id === current.groupId) && <option value={current.groupId}>{current.groupName}</option>}
                      {groups.data?.map((g) => (
                        <option key={g.id} value={g.id}>
                          {g.name}
                        </option>
                      ))}
                    </select>
                    {submitted && missing.who && <p className="text-sm text-destructive">{t('lessons.validation.group')}</p>}
                  </div>
                )}
              </div>
              <FormField label={t('lessons.fields.title')} maxLength={120} value={values.title} error={submitted && missing.title ? t('lessons.validation.title') : undefined} onChange={(e) => set({ title: e.target.value })} />
              <div className="flex flex-col gap-2">
                <Label htmlFor="lesson-subject">{t('lessons.fields.subject')}</Label>
                <select id="lesson-subject" className={field} value={values.subjectId} onChange={(e) => set({ subjectId: e.target.value })}>
                  <option value="">{t('lessons.fields.noSubject')}</option>
                  {values.subjectId && !catalog.data?.subjects.some((s) => s.id === values.subjectId) && (
                    <option value={values.subjectId}>{catalog.data ? t('groups.fields.retiredSubject') : t('common.loading')}</option>
                  )}
                  {catalog.data?.subjects.map((s) => (
                    <option key={s.id} value={s.id}>
                      {i18n.resolvedLanguage === 'en' ? s.nameEn : s.nameMk}
                    </option>
                  ))}
                </select>
              </div>
              <FormField label={t('lessons.fields.date')} type="date" value={values.date} error={submitted && missing.date ? t('lessons.validation.date') : undefined} onChange={(e) => set({ date: e.target.value })} />
              <FormField label={t('lessons.fields.time')} type="time" step={300} value={values.time} error={submitted && missing.time ? t('lessons.validation.time') : undefined} onChange={(e) => set({ time: e.target.value })} />
              <div className="flex flex-col gap-2">
                <Label htmlFor="lesson-duration">{t('lessons.fields.duration')}</Label>
                <select id="lesson-duration" className={field} value={values.duration} onChange={(e) => set({ duration: e.target.value })}>
                  {durationOptions.map((d) => (
                    <option key={d} value={String(d)}>
                      {t('lessons.minutes', { count: d })}
                    </option>
                  ))}
                </select>
              </div>
              <fieldset className="flex flex-col gap-2">
                <legend className="text-sm font-medium">{t('lessons.fields.mode')}</legend>
                <div className="flex gap-4">
                  {(['Online', 'InPerson'] as const).map((mode) => (
                    <label key={mode} className="flex items-center gap-2 text-sm">
                      <input type="radio" name="lesson-mode" className="h-4 w-4" checked={values.mode === mode} onChange={() => set({ mode })} />
                      {t(`lessons.modes.${mode}`)}
                    </label>
                  ))}
                </div>
              </fieldset>
              {values.mode === 'Online' ? (
                <FormField label={t('lessons.fields.meetingUrl')} type="url" inputMode="url" placeholder="https://" maxLength={500} value={values.meetingUrl} onChange={(e) => set({ meetingUrl: e.target.value })} />
              ) : (
                <FormField label={t('lessons.fields.location')} maxLength={200} value={values.location} onChange={(e) => set({ location: e.target.value })} />
              )}
              <div className="flex flex-col gap-2 sm:col-span-2">
                <Label htmlFor="lesson-topic">{t('lessons.fields.topic')}</Label>
                <textarea id="lesson-topic" rows={2} maxLength={500} className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm" value={values.topic} onChange={(e) => set({ topic: e.target.value })} />
              </div>
              {current?.kind === 'Group' && (
                <p className="text-sm text-muted-foreground sm:col-span-2">
                  {t('lessons.participants', { names: current.participants.map((p) => [p.firstName, p.lastName].filter(Boolean).join(' ')).join(', ') })}
                </p>
              )}
              {saveError && (
                <Alert variant="error" className="sm:col-span-2">
                  {saveError}
                </Alert>
              )}
              {save.isSuccess && lesson && (
                <Alert variant="success" className="sm:col-span-2">
                  {t('lessons.saved')}
                </Alert>
              )}
              {editable && (
                <Button type="submit" className="self-start" disabled={save.isPending}>
                  {save.isPending ? t('common.loading') : lesson ? t('lessons.save') : t('lessons.create')}
                </Button>
              )}
            </CardContent>
          </Card>
        </fieldset>
      </form>

      {current && current.status === 'Scheduled' && version !== null && (
        <StatusCard
          workspaceId={workspaceId}
          lesson={current}
          version={version}
          onChanged={(changed) => {
            setCurrent(changed);
            setVersion(num(changed.version) ?? 0);
          }}
        />
      )}
    </section>
  );
}

/** Mark as held (once it has started) or cancel with a reason. Both are final. */
function StatusCard({ workspaceId, lesson, version, onChanged }: { workspaceId: string; lesson: Lesson; version: number; onChanged: (changed: Lesson) => void }) {
  const { t } = useTranslation();
  const complete = useCompleteLesson(workspaceId, lesson.id);
  const cancel = useCancelLesson(workspaceId, lesson.id);
  const [reason, setReason] = useState('');
  const [cancelling, setCancelling] = useState(false);
  const started = useStarted(lesson.startUtc);
  const error = complete.error ?? cancel.error;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('lessons.statusTitle')}</CardTitle>
        <CardDescription>{t('lessons.statusLead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {error && <Alert variant="error">{error instanceof ApiError && error.code === 'CONCURRENCY_CONFLICT' ? t('lessons.changedElsewhere') : errorMessage(error, t)}</Alert>}
        <div className="flex flex-wrap gap-2">
          <Button type="button" disabled={!started || complete.isPending} onClick={() => complete.mutate(version, { onSuccess: onChanged })}>
            {t('lessons.complete')}
          </Button>
          {!cancelling && (
            <Button type="button" variant="outline" onClick={() => setCancelling(true)}>
              {t('lessons.cancel')}
            </Button>
          )}
        </div>
        {!started && <p className="text-sm text-muted-foreground">{t('lessons.completeLater')}</p>}
        {cancelling && (
          <form
            className="flex flex-col gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              if (reason.trim()) cancel.mutate({ version, reason: reason.trim() }, { onSuccess: onChanged });
            }}
          >
            <Label htmlFor="lesson-cancel-reason">{t('lessons.cancelReason')}</Label>
            <textarea id="lesson-cancel-reason" rows={2} maxLength={500} className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm" value={reason} onChange={(e) => setReason(e.target.value)} />
            <div className="flex gap-2">
              <Button type="submit" variant="outline" disabled={cancel.isPending || !reason.trim()}>
                {t('lessons.cancelConfirm')}
              </Button>
              <Button type="button" variant="ghost" onClick={() => setCancelling(false)}>
                {t('common.cancel')}
              </Button>
            </div>
          </form>
        )}
      </CardContent>
    </Card>
  );
}
