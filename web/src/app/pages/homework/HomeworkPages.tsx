import { ArrowLeft, Download } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button, buttonVariants } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { formatDate, formatDateTime } from '../../../lib/format.ts';
import { useGroups } from '../../../teacher/groups.ts';
import {
  answerFileUrl,
  myAnswerFileUrl,
  useArchiveHomework,
  useAssignHomework,
  useHomework,
  useHomeworkList,
  useMyHomework,
  useMyHomeworkList,
  useReviewHomework,
  useSaveHomework,
  useSubmitHomework,
  useUnassignHomework,
  type Homework,
  type HomeworkAssignment,
  type HomeworkStatus,
} from '../../../teacher/homework.ts';
import { useLesson } from '../../../teacher/lessons.ts';
import { formatBytes, materialFileProblem, MATERIAL_EXTENSIONS } from '../../../teacher/materials.ts';
import { num } from '../../../teacher/offers.ts';
import { StudentPicker } from '../../components/StudentPicker.tsx';
import { NotFoundContent } from '../Pages.tsx';

const field = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';
const textarea = 'w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm';
const TITLE_MAX = 160;
const DESCRIPTION_MAX = 4000;
const ANSWER_MAX = 8000;
const COMMENT_MAX = 4000;

/** The viewer's own time zone (hand-in times). */
const viewerTimeZone = () => new Intl.DateTimeFormat().resolvedOptions().timeZone;

/** A due date (a local calendar day) in the UI language. */
const dueLabel = (date: string, language: string) => formatDate(`${date}T12:00:00Z`, language, 'UTC');

const statusTone: Record<HomeworkStatus, string> = {
  Assigned: 'bg-muted text-foreground',
  Submitted: 'bg-primary/10 text-primary',
  Reviewed: 'bg-secondary text-secondary-foreground',
};

function StatusBadge({ status, late }: { status: string; late?: boolean }) {
  const { t } = useTranslation();
  return (
    <span className="inline-flex flex-wrap gap-1">
      <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${statusTone[status as HomeworkStatus] ?? statusTone.Assigned}`}>{t(`homework.status.${status}`)}</span>
      {late && <span className="rounded-full border border-destructive px-2 py-0.5 text-xs font-medium text-destructive">{t('homework.late')}</span>}
    </span>
  );
}

/** The teacher's homework (R2-P9). */
export function HomeworkPage() {
  const { t, i18n } = useTranslation();
  const { workspaceId = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const archived = params.get('archived') === 'true';
  const [q, setQ] = useState('');
  const [page, setPage] = useState(1);
  const list = useHomeworkList(workspaceId, { q, archived, page });
  const pages = Math.max(1, Math.ceil((num(list.data?.total) ?? 0) / (num(list.data?.pageSize) ?? 20)));

  if (list.isError && !list.data) return list.error instanceof ApiError && list.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(list.error, t)}</Alert>;

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">{t('homework.title')}</h1>
          <p className="text-sm text-muted-foreground">{t('homework.lead')}</p>
        </div>
        <Link className={buttonVariants()} to={`/app/w/${workspaceId}/homework/new`}>
          {t('homework.add')}
        </Link>
      </div>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div className="flex flex-col gap-1 sm:w-80">
          <Label htmlFor="homework-search">{t('homework.search')}</Label>
          <input
            id="homework-search"
            type="search"
            className={field}
            value={q}
            onChange={(e) => {
              setQ(e.target.value);
              setPage(1);
            }}
          />
        </div>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="h-4 w-4"
            checked={archived}
            onChange={(e) => {
              setParams(e.target.checked ? { archived: 'true' } : {}, { replace: true });
              setPage(1);
            }}
          />
          {t('homework.showArchived')}
        </label>
      </div>
      {!list.data ? (
        <output className="block">{t('common.loading')}</output>
      ) : list.data.items.length === 0 ? (
        <Card>
          <CardHeader>
            <CardDescription>{q.trim() ? t('homework.noMatches') : archived ? t('homework.noArchived') : t('homework.empty')}</CardDescription>
          </CardHeader>
        </Card>
      ) : (
        <ul aria-label={t('homework.title')} className="grid gap-3 sm:grid-cols-2">
          {list.data.items.map((h) => (
            <li key={h.id}>
              <Link
                to={`/app/w/${workspaceId}/homework/${h.id}`}
                className="flex h-full flex-col gap-1 rounded-lg border border-border bg-card p-4 hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                <span className="font-semibold">{h.title}</span>
                {h.dueDate && <span className="text-sm text-muted-foreground">{t('homework.due', { date: dueLabel(h.dueDate, i18n.resolvedLanguage ?? 'mk') })}</span>}
                <span className="text-sm text-muted-foreground">
                  {t('homework.counts', { assigned: num(h.assigned) ?? 0, submitted: num(h.submitted) ?? 0, reviewed: num(h.reviewed) ?? 0 })}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
      {pages > 1 && (
        <nav aria-label={t('homework.pages')} className="flex items-center gap-2">
          <Button type="button" size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage(page - 1)}>
            {t('leads.previous')}
          </Button>
          <span className="text-sm">{t('leads.pageOf', { page, pages })}</span>
          <Button type="button" size="sm" variant="outline" disabled={page >= pages} onClick={() => setPage(page + 1)}>
            {t('leads.next')}
          </Button>
        </nav>
      )}
    </section>
  );
}

/** Keyed by the route's id like the other editors: another homework (or the one just created) is a fresh mount. */
export function HomeworkEditPage() {
  const { homeworkId } = useParams();
  return <HomeworkLoader key={homeworkId ?? 'new'} />;
}

function HomeworkLoader() {
  const { t } = useTranslation();
  const { workspaceId = '', homeworkId } = useParams();
  const homework = useHomework(workspaceId, homeworkId);
  if (homeworkId && homework.isError) return homework.error instanceof ApiError && homework.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(homework.error, t)}</Alert>;
  if (homeworkId && (!homework.data || !homework.isFetchedAfterMount)) return <output className="block">{t('common.loading')}</output>;
  return <HomeworkEditor workspaceId={workspaceId} homework={homeworkId ? homework.data : undefined} />;
}

function HomeworkEditor({ workspaceId, homework }: { workspaceId: string; homework: Homework | undefined }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const lessonId = homework ? (homework.lesson?.id ?? null) : params.get('lessonId');
  const lesson = useLesson(workspaceId, !homework && lessonId ? lessonId : undefined);
  const save = useSaveHomework(workspaceId, homework?.id);
  const [values, setValues] = useState({ title: homework?.title ?? '', description: homework?.description ?? '', dueDate: homework?.dueDate ?? '' });
  const [version, setVersion] = useState<number | null>(homework ? (num(homework.version) ?? 0) : null);
  const [missing, setMissing] = useState(false);
  const current = useHomework(workspaceId, homework?.id).data ?? homework;
  const lessonTitle = homework?.lesson?.title ?? lesson.data?.title;
  const lessonStart = homework?.lesson ?? (lesson.data ? { startUtc: lesson.data.startUtc, timeZone: lesson.data.timeZone } : null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!values.title.trim()) {
      setMissing(true);
      return;
    }
    try {
      const saved = await save.mutateAsync({
        title: values.title.trim(),
        description: values.description.trim() || null,
        dueDate: values.dueDate || null,
        lessonId,
        version,
      });
      setVersion(num(saved.version) ?? 0);
      if (!homework) void navigate(`/app/w/${workspaceId}/homework/${saved.id}`, { replace: true });
    } catch {
      // shown below
    }
  };
  const saveError = save.error instanceof ApiError && save.error.code === 'CONCURRENCY_CONFLICT' ? t('homework.changedElsewhere') : save.error ? errorMessage(save.error, t) : null;

  return (
    <section className="flex flex-col gap-6">
      <Link to={`/app/w/${workspaceId}/homework`} className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline">
        <ArrowLeft className="h-4 w-4" aria-hidden />
        {t('homework.back')}
      </Link>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{current ? current.title : t('homework.newTitle')}</h1>
        {lessonId && lessonTitle && lessonStart && (
          <p className="text-sm text-muted-foreground">
            {t('homework.forLesson')}{' '}
            <Link className="text-primary underline-offset-4 hover:underline" to={`/app/w/${workspaceId}/lessons/${lessonId}`}>
              {lessonTitle} · {formatDateTime(lessonStart.startUtc, i18n.resolvedLanguage ?? 'mk', lessonStart.timeZone)}
            </Link>
          </p>
        )}
        {current?.archived && <p className="text-sm text-muted-foreground">{t('homework.archivedLead')}</p>}
      </div>

      <form noValidate onSubmit={(e) => void submit(e)}>
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">{t('homework.details')}</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <FormField
              label={t('homework.fields.title')}
              maxLength={TITLE_MAX}
              value={values.title}
              error={missing && !values.title.trim() ? t('homework.validation.title') : undefined}
              onChange={(e) => setValues({ ...values, title: e.target.value })}
            />
            <FormField label={t('homework.fields.dueDate')} type="date" value={values.dueDate} hint={t('homework.fields.dueHint')} onChange={(e) => setValues({ ...values, dueDate: e.target.value })} />
            <div className="flex flex-col gap-2 sm:col-span-2">
              <Label htmlFor="homework-description">{t('homework.fields.description')}</Label>
              <textarea id="homework-description" rows={5} maxLength={DESCRIPTION_MAX} className={textarea} value={values.description} onChange={(e) => setValues({ ...values, description: e.target.value })} />
            </div>
            {saveError && (
              <Alert variant="error" className="sm:col-span-2">
                {saveError}
              </Alert>
            )}
            {save.isSuccess && homework && (
              <Alert variant="success" className="sm:col-span-2">
                {t('homework.saved')}
              </Alert>
            )}
            <Button type="submit" className="self-start" disabled={save.isPending}>
              {save.isPending ? t('common.loading') : homework ? t('homework.save') : t('homework.create')}
            </Button>
          </CardContent>
        </Card>
      </form>

      {current && <Assignments workspaceId={workspaceId} homework={current} />}
      {current && version !== null && <ArchiveHomework workspaceId={workspaceId} homework={current} version={version} onChanged={(changed) => setVersion(num(changed.version) ?? 0)} />}
    </section>
  );
}

/** Assignments follow the cache: assigning, removing and reviewing are separate operations from the details form. */
function Assignments({ workspaceId, homework }: { workspaceId: string; homework: Homework }) {
  const { t } = useTranslation();
  const assign = useAssignHomework(workspaceId, homework.id);
  const unassign = useUnassignHomework(workspaceId, homework.id);
  const groups = useGroups(workspaceId, false);
  const [student, setStudent] = useState('');
  const [group, setGroup] = useState('');

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('homework.assignments.title')}</CardTitle>
        <CardDescription>{t('homework.assignments.lead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {homework.assignments.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('homework.assignments.none')}</p>
        ) : (
          <ul aria-label={t('homework.assignments.title')} className="flex flex-col divide-y divide-border">
            {homework.assignments.map((a) => (
              <AssignmentRow key={`${a.id}:${num(a.version)}`} workspaceId={workspaceId} homeworkId={homework.id} assignment={a} onRemove={() => unassign.mutate(a.id)} removing={unassign.isPending} />
            ))}
          </ul>
        )}
        {homework.archived ? (
          <p className="text-sm text-muted-foreground">{t('homework.assignments.archived')}</p>
        ) : (
          <div className="grid gap-4 sm:grid-cols-2">
            <form
              className="flex flex-col gap-2"
              onSubmit={(e) => {
                e.preventDefault();
                if (student) assign.mutate({ studentIds: [student] }, { onSuccess: () => setStudent('') });
              }}
            >
              <StudentPicker
                workspaceId={workspaceId}
                id="homework-assign-student"
                label={t('homework.assignments.toStudent')}
                value={student}
                onChange={setStudent}
                exclude={homework.assignments.map((a) => a.studentId)}
              />
              <Button type="submit" variant="outline" className="self-start" disabled={assign.isPending || !student}>
                {t('homework.assignments.assign')}
              </Button>
            </form>
            <form
              className="flex flex-col gap-2"
              onSubmit={(e) => {
                e.preventDefault();
                if (group) assign.mutate({ groupId: group }, { onSuccess: () => setGroup('') });
              }}
            >
              <Label htmlFor="homework-assign-group">{t('homework.assignments.toGroup')}</Label>
              <select id="homework-assign-group" className={field} value={group} onChange={(e) => setGroup(e.target.value)}>
                <option value="">{t('homework.assignments.chooseGroup')}</option>
                {(groups.data ?? []).map((g) => (
                  <option key={g.id} value={g.id}>
                    {g.name}
                  </option>
                ))}
              </select>
              <p className="text-xs text-muted-foreground">{t('homework.assignments.groupHint')}</p>
              <Button type="submit" variant="outline" className="self-start" disabled={assign.isPending || !group}>
                {t('homework.assignments.assign')}
              </Button>
            </form>
          </div>
        )}
        {(assign.error ?? unassign.error) && <Alert variant="error">{errorMessage(assign.error ?? unassign.error, t)}</Alert>}
      </CardContent>
    </Card>
  );
}

/** One student's assignment; keyed by its version, so a new answer resets the review draft to what was saved. */
function AssignmentRow({ workspaceId, homeworkId, assignment, onRemove, removing }: { workspaceId: string; homeworkId: string; assignment: HomeworkAssignment; onRemove: () => void; removing: boolean }) {
  const { t, i18n } = useTranslation();
  const review = useReviewHomework(workspaceId, homeworkId);
  const [comment, setComment] = useState(assignment.teacherComment ?? '');
  const language = i18n.resolvedLanguage ?? 'mk';
  const answered = assignment.status !== 'Assigned';
  const reviewError =
    review.error instanceof ApiError && review.error.code === 'CONCURRENCY_CONFLICT' ? t('homework.review.newAnswer') : review.error ? errorMessage(review.error, t) : null;

  return (
    <li className="flex flex-col gap-3 py-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <Link className="text-sm font-medium hover:underline" to={`/app/w/${workspaceId}/students/${assignment.studentId}`}>
            {assignment.studentName}
          </Link>
          <StatusBadge status={assignment.status} late={assignment.late} />
        </div>
        {!answered && (
          <Button type="button" size="sm" variant="ghost" disabled={removing} aria-label={t('homework.assignments.remove', { name: assignment.studentName })} onClick={onRemove}>
            {t('homework.assignments.removeShort')}
          </Button>
        )}
      </div>
      {answered && (
        <div className="flex flex-col gap-2 rounded-md bg-muted/50 p-3">
          {assignment.submittedAtUtc && (
            <p className="text-xs text-muted-foreground">{t('homework.submittedAt', { date: formatDateTime(assignment.submittedAtUtc, language, viewerTimeZone()) })}</p>
          )}
          {assignment.answerText && <p className="whitespace-pre-wrap text-sm">{assignment.answerText}</p>}
          {assignment.file && (
            <a
              className={buttonVariants({ variant: 'outline', size: 'sm', className: 'self-start' })}
              href={answerFileUrl(workspaceId, homeworkId, assignment.id)}
              download={assignment.file.fileName}
              aria-label={t('homework.downloadAnswer', { name: assignment.studentName })}
            >
              <Download className="h-4 w-4" aria-hidden />
              {assignment.file.fileName} · {formatBytes(num(assignment.file.sizeBytes) ?? 0, language)}
            </a>
          )}
          <form
            className="flex flex-col gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              review.mutate({ assignmentId: assignment.id, comment: comment.trim() || null, version: num(assignment.version) ?? 0 });
            }}
          >
            <Label htmlFor={`review-${assignment.id}`}>{t('homework.review.comment', { name: assignment.studentName })}</Label>
            <textarea id={`review-${assignment.id}`} rows={2} maxLength={COMMENT_MAX} className={textarea} value={comment} onChange={(e) => setComment(e.target.value)} />
            {reviewError && <Alert variant="error">{reviewError}</Alert>}
            <Button type="submit" size="sm" className="self-start" disabled={review.isPending}>
              {assignment.status === 'Reviewed' ? t('homework.review.update') : t('homework.review.submit')}
            </Button>
          </form>
        </div>
      )}
    </li>
  );
}

function ArchiveHomework({ workspaceId, homework, version, onChanged }: { workspaceId: string; homework: Homework; version: number; onChanged: (changed: Homework) => void }) {
  const { t } = useTranslation();
  const archive = useArchiveHomework(workspaceId, homework.id);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{homework.archived ? t('homework.restoreTitle') : t('homework.archiveTitle')}</CardTitle>
        <CardDescription>{homework.archived ? t('homework.restoreLead') : t('homework.archiveLead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {archive.error && (
          <Alert variant="error">{archive.error instanceof ApiError && archive.error.code === 'CONCURRENCY_CONFLICT' ? t('homework.changedElsewhere') : errorMessage(archive.error, t)}</Alert>
        )}
        <Button type="button" variant="outline" className="self-start" disabled={archive.isPending} onClick={() => archive.mutate({ archived: !homework.archived, version }, { onSuccess: onChanged })}>
          {homework.archived ? t('homework.restore') : t('homework.archive')}
        </Button>
      </CardContent>
    </Card>
  );
}

/** On the lesson page: the homework given for this lesson, and a shortcut to add some. */
export function LessonHomework({ workspaceId, lessonId }: { workspaceId: string; lessonId: string }) {
  const { t } = useTranslation();
  const list = useHomeworkList(workspaceId, { q: '', archived: false, page: 1, lessonId });
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('homework.ofLesson')}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {list.isError ? (
          <Alert variant="error">{errorMessage(list.error, t)}</Alert>
        ) : !list.data ? (
          <output className="block text-sm">{t('common.loading')}</output>
        ) : list.data.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('homework.noneForLesson')}</p>
        ) : (
          <ul aria-label={t('homework.ofLesson')} className="flex flex-col gap-1">
            {list.data.items.map((h) => (
              <li key={h.id}>
                <Link className="text-sm font-medium hover:underline" to={`/app/w/${workspaceId}/homework/${h.id}`}>
                  {h.title}
                </Link>
              </li>
            ))}
          </ul>
        )}
        <Link className={buttonVariants({ variant: 'outline', size: 'sm', className: 'self-start' })} to={`/app/w/${workspaceId}/homework/new?lessonId=${lessonId}`}>
          {t('homework.addForLesson')}
        </Link>
      </CardContent>
    </Card>
  );
}

/** In the student portal: the student's homework with its status; open items first. */
export function MyHomeworkList({ workspaceId }: { workspaceId: string }) {
  const { t, i18n } = useTranslation();
  const homework = useMyHomeworkList(workspaceId);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('homework.portal.title')}</CardTitle>
      </CardHeader>
      <CardContent>
        {homework.isError ? (
          <Alert variant="error">{errorMessage(homework.error, t)}</Alert>
        ) : !homework.data ? (
          <output className="block text-sm">{t('common.loading')}</output>
        ) : homework.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('homework.portal.none')}</p>
        ) : (
          <ul aria-label={t('homework.portal.title')} className="flex flex-col divide-y divide-border">
            {homework.data.map((h) => (
              <li key={h.id} className="py-2">
                <Link className="flex flex-col gap-1 rounded-md p-1 hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring" to={`/student/w/${workspaceId}/homework/${h.id}`}>
                  <span className="font-medium">{h.title}</span>
                  <span className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
                    <StatusBadge status={h.status} late={h.late} />
                    {h.dueDate && t('homework.due', { date: dueLabel(h.dueDate, i18n.resolvedLanguage ?? 'mk') })}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}

/** `/student/w/:workspaceId/homework/:assignmentId`: the task, the teacher's review and the answer form. */
export function StudentHomeworkPage() {
  const { t } = useTranslation();
  const { workspaceId = '', assignmentId = '' } = useParams();
  const homework = useMyHomework(workspaceId, assignmentId);
  if (homework.isError) return homework.error instanceof ApiError && homework.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(homework.error, t)}</Alert>;
  if (!homework.data || !homework.isFetchedAfterMount) return <output className="block">{t('common.loading')}</output>;
  return <StudentHomework key={`${assignmentId}:${num(homework.data.version)}`} workspaceId={workspaceId} />;
}

function StudentHomework({ workspaceId }: { workspaceId: string }) {
  const { t, i18n } = useTranslation();
  const { assignmentId = '' } = useParams();
  const homework = useMyHomework(workspaceId, assignmentId).data!;
  const submit = useSubmitHomework(workspaceId, assignmentId);
  const language = i18n.resolvedLanguage ?? 'mk';
  const [text, setText] = useState(homework.answerText ?? '');
  const [file, setFile] = useState<File | null>(null);
  const [removeFile, setRemoveFile] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const reviewed = homework.status === 'Reviewed';

  const send = async (event: FormEvent) => {
    event.preventDefault();
    const fileProblem = file ? materialFileProblem(file) : null;
    if (fileProblem) {
      setProblem(t(`materials.upload.${fileProblem}`));
      return;
    }
    if (!text.trim() && !file && (!homework.file || removeFile)) {
      setProblem(t('homework.portal.empty'));
      return;
    }
    setProblem(null);
    try {
      await submit.mutateAsync({ text, file, removeFile: removeFile && !file });
    } catch {
      // shown below
    }
  };
  const submitError =
    submit.error instanceof ApiError && submit.error.code === 'PLAN_LIMIT_REACHED'
      ? t('homework.portal.storageFull')
      : submit.error instanceof ApiError && (submit.error.code === 'CONCURRENCY_CONFLICT' || submit.error.code === 'INVALID_STATUS_TRANSITION')
        ? t('homework.portal.reviewedMeanwhile')
        : submit.error
          ? errorMessage(submit.error, t)
          : null;

  return (
    <section className="flex flex-col gap-6">
      <Link to={`/student/w/${workspaceId}`} className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline">
        <ArrowLeft className="h-4 w-4" aria-hidden />
        {t('homework.portal.back')}
      </Link>
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">{homework.title}</h1>
        <div className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
          <StatusBadge status={homework.status} late={homework.late} />
          {homework.dueDate && t('homework.due', { date: dueLabel(homework.dueDate, language) })}
        </div>
      </div>
      {homework.description && (
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">{t('homework.portal.task')}</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="whitespace-pre-wrap text-sm">{homework.description}</p>
          </CardContent>
        </Card>
      )}
      {reviewed && (
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">{t('homework.portal.review')}</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="whitespace-pre-wrap text-sm">{homework.teacherComment ?? t('homework.portal.noComment')}</p>
          </CardContent>
        </Card>
      )}
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('homework.portal.answer')}</CardTitle>
          <CardDescription>{reviewed ? t('homework.portal.final') : t('homework.portal.answerLead')}</CardDescription>
        </CardHeader>
        <CardContent>
          {reviewed ? (
            <div className="flex flex-col gap-2">
              {homework.answerText && <p className="whitespace-pre-wrap text-sm">{homework.answerText}</p>}
              {homework.file && <OwnFile workspaceId={workspaceId} assignmentId={homework.id} fileName={homework.file.fileName} />}
            </div>
          ) : (
            <form noValidate className="flex flex-col gap-4" onSubmit={(e) => void send(e)}>
              <div className="flex flex-col gap-2">
                <Label htmlFor="homework-answer">{t('homework.portal.text')}</Label>
                <textarea id="homework-answer" rows={6} maxLength={ANSWER_MAX} className={textarea} value={text} onChange={(e) => setText(e.target.value)} />
              </div>
              {homework.file && !file && (
                <div className="flex flex-wrap items-center gap-3">
                  <OwnFile workspaceId={workspaceId} assignmentId={homework.id} fileName={homework.file.fileName} />
                  <label className="flex items-center gap-2 text-sm">
                    <input type="checkbox" className="h-4 w-4" checked={removeFile} onChange={(e) => setRemoveFile(e.target.checked)} />
                    {t('homework.portal.removeFile')}
                  </label>
                </div>
              )}
              <div className="flex flex-col gap-2">
                <Label htmlFor="homework-file">{homework.file ? t('homework.portal.replaceFile') : t('homework.portal.file')}</Label>
                <input id="homework-file" type="file" accept={MATERIAL_EXTENSIONS.join(',')} className="text-sm" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
                <p className="text-xs text-muted-foreground">{t('homework.portal.fileHint', { types: MATERIAL_EXTENSIONS.join(', ') })}</p>
              </div>
              {(problem ?? submitError) && <Alert variant="error">{problem ?? submitError}</Alert>}
              <Button type="submit" className="self-start" disabled={submit.isPending}>
                {submit.isPending ? t('common.loading') : homework.status === 'Submitted' ? t('homework.portal.resubmit') : t('homework.portal.submit')}
              </Button>
            </form>
          )}
        </CardContent>
      </Card>
    </section>
  );
}

function OwnFile({ workspaceId, assignmentId, fileName }: { workspaceId: string; assignmentId: string; fileName: string }) {
  const { t } = useTranslation();
  return (
    <a className={buttonVariants({ variant: 'outline', size: 'sm' })} href={myAnswerFileUrl(workspaceId, assignmentId)} download={fileName} aria-label={t('homework.portal.ownFile', { name: fileName })}>
      <Download className="h-4 w-4" aria-hidden />
      {fileName}
    </a>
  );
}
