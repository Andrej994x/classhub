import { ArrowLeft } from 'lucide-react';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button, buttonVariants } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { formatDateTime } from '../../../lib/format.ts';
import { num } from '../../../teacher/offers.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import { useAddStudentNote, useArchiveStudent, useSaveStudent, useStudent, useStudentNotes, useStudents, type Student, type StudentInput } from '../../../teacher/students.ts';
import { NotFoundContent } from '../Pages.tsx';

const browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
const input = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const PHONE = /^\+?[0-9 ()-]{5,40}$/;

const fullName = (s: { firstName: string; lastName?: string | null }) => [s.firstName, s.lastName].filter(Boolean).join(' ');

/** The teacher's students (R2-P1): active by default, search, archived view. */
export function StudentsPage() {
  const { t } = useTranslation();
  const { workspaceId = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const filters = { q: params.get('q') ?? '', archived: params.get('archived') === 'true', page: Math.max(1, Number(params.get('page') ?? '1') || 1) };
  const [search, setSearch] = useState(filters.q);
  const students = useStudents(workspaceId, filters);

  const update = (changes: Partial<Record<'q' | 'archived' | 'page', string | null>>) =>
    setParams(
      (current) => {
        const next = new URLSearchParams(current);
        for (const [key, value] of Object.entries(changes)) {
          if (value) next.set(key, value);
          else next.delete(key);
        }
        if (!('page' in changes)) next.delete('page');
        return next;
      },
      { replace: true },
    );
  const latestUpdate = useRef(update);
  useEffect(() => {
    latestUpdate.current = update;
  });
  useEffect(() => {
    if (search === filters.q) return undefined;
    const timer = setTimeout(() => latestUpdate.current({ q: search.trim() || null }), 350);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search]);

  if (students.isError) return students.error instanceof ApiError && students.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(students.error, t)}</Alert>;
  const data = students.data;
  const total = num(data?.total) ?? 0;
  const pages = Math.max(1, Math.ceil(total / (num(data?.pageSize) ?? 20)));

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">{t('students.title')}</h1>
          <p className="text-sm text-muted-foreground">
            {t('students.lead')} {data && t('students.activeCount', { count: num(data.activeCount) ?? 0 })}
          </p>
        </div>
        <Link className={buttonVariants()} to={`/app/w/${workspaceId}/students/new`}>
          {t('students.add')}
        </Link>
      </div>

      <form aria-label={t('students.filters')} className="grid gap-3 sm:grid-cols-3" onSubmit={(e) => e.preventDefault()}>
        <div className="flex flex-col gap-1 sm:col-span-2">
          <Label htmlFor="students-search">{t('students.search')}</Label>
          <input id="students-search" type="search" className={input} value={search} maxLength={100} onChange={(e) => setSearch(e.target.value)} />
        </div>
        <label className="flex items-center gap-2 self-end text-sm sm:h-10">
          <input type="checkbox" className="h-4 w-4" checked={filters.archived} onChange={(e) => update({ archived: e.target.checked ? 'true' : null })} />
          {t('students.showArchived')}
        </label>
      </form>

      {!data ? (
        <output className="block">{t('common.loading')}</output>
      ) : data.items.length === 0 ? (
        <Card>
          <CardHeader>
            <CardDescription>{filters.q || filters.archived ? t('students.noMatches') : t('students.empty')}</CardDescription>
          </CardHeader>
        </Card>
      ) : (
        <ul aria-label={t('students.title')} className="flex flex-col gap-3">
          {data.items.map((student) => (
            <li key={student.id}>
              <Link
                to={`/app/w/${workspaceId}/students/${student.id}`}
                className="flex flex-col gap-1 rounded-lg border border-border bg-card p-4 hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                <span className="flex flex-wrap items-center gap-2">
                  <span className="font-semibold">{fullName(student)}</span>
                  {student.isMinor && <span className="rounded-full bg-secondary px-2 py-0.5 text-xs">{t('students.minor')}</span>}
                  {student.archived && <span className="text-xs text-muted-foreground">· {t('students.archived')}</span>}
                </span>
                {(student.email || student.phone) && <span className="break-all text-sm text-muted-foreground">{[student.email, student.phone].filter(Boolean).join(' · ')}</span>}
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

/** Add (`/students/new`) or edit a student; archive/restore and private notes for existing ones. */
export function StudentEditPage() {
  const { t } = useTranslation();
  const { workspaceId = '', studentId } = useParams();
  const student = useStudent(workspaceId, studentId);

  if (studentId && student.isError) return student.error instanceof ApiError && student.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(student.error, t)}</Alert>;
  if (studentId && !student.data) return <output className="block">{t('common.loading')}</output>;
  // Keyed by the student only: a background refetch (another tab saved meanwhile) must never remount a form with unsaved
  // edits. The editor keeps the version it loaded; a newer one elsewhere surfaces as a conflict on save.
  return <StudentEditor key={studentId ?? 'new'} workspaceId={workspaceId} student={studentId ? student.data : undefined} />;
}

function StudentEditor({ workspaceId, student }: { workspaceId: string; student: Student | undefined }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const catalog = useCatalog();
  const save = useSaveStudent(workspaceId, student?.id);
  const language = i18n.resolvedLanguage ?? 'mk';
  const [values, setValues] = useState<StudentInput>({
    firstName: student?.firstName ?? '',
    lastName: student?.lastName ?? null,
    email: student?.email ?? null,
    phone: student?.phone ?? null,
    isMinor: student?.isMinor ?? false,
    guardianName: student?.guardianName ?? null,
    guardianEmail: student?.guardianEmail ?? null,
    guardianPhone: student?.guardianPhone ?? null,
    subjectIds: [...(student?.subjectIds ?? [])],
  });
  const [errors, setErrors] = useState<Partial<Record<keyof StudentInput, string>>>({});
  // The version this editing session is based on: its own saves and archive changes move it, refetches do not.
  const [version, setVersion] = useState<number | null>(student ? (num(student.version) ?? 0) : null);
  const set = <K extends keyof StudentInput>(key: K, value: StudentInput[K]) => setValues((current) => ({ ...current, [key]: value }));
  const text = (key: 'firstName' | 'lastName' | 'email' | 'phone' | 'guardianName' | 'guardianEmail' | 'guardianPhone') => ({
    value: values[key] ?? '',
    error: errors[key],
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => set(key, e.target.value === '' && key !== 'firstName' ? null : e.target.value),
  });

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const found: typeof errors = {};
    if (!values.firstName.trim()) found.firstName = t('students.validation.firstName');
    for (const key of ['email', 'guardianEmail'] as const) if (values[key] && !EMAIL.test(values[key]!.trim())) found[key] = t('students.validation.email');
    for (const key of ['phone', 'guardianPhone'] as const) if (values[key] && !PHONE.test(values[key]!.trim())) found[key] = t('students.validation.phone');
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    try {
      const saved = await save.mutateAsync({ input: values, version });
      setVersion(num(saved.version) ?? 0);
      if (!student) void navigate(`/app/w/${workspaceId}/students/${saved.id}`, { replace: true });
    } catch {
      // shown below
    }
  };

  const saveError = save.error instanceof ApiError && save.error.code === 'CONCURRENCY_CONFLICT' ? t('students.changedElsewhere') : save.error ? errorMessage(save.error, t) : null;

  return (
    <section className="flex flex-col gap-6">
      <Link to={`/app/w/${workspaceId}/students`} className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline">
        <ArrowLeft className="h-4 w-4" aria-hidden />
        {t('students.back')}
      </Link>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{student ? fullName(student) : t('students.newTitle')}</h1>
        {student?.archived && <p className="text-sm text-muted-foreground">{t('students.archivedLead')}</p>}
      </div>

      <form noValidate onSubmit={(e) => void submit(e)} className="flex flex-col gap-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">{t('students.sections.details')}</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <FormField label={t('students.fields.firstName')} autoComplete="off" maxLength={60} {...text('firstName')} />
            <FormField label={t('students.fields.lastName')} autoComplete="off" maxLength={60} {...text('lastName')} />
            <FormField label={t('students.fields.email')} type="email" autoComplete="off" maxLength={254} {...text('email')} />
            <FormField label={t('students.fields.phone')} type="tel" autoComplete="off" maxLength={40} {...text('phone')} />
            <label className="flex items-center gap-2 text-sm sm:col-span-2">
              <input type="checkbox" className="h-4 w-4" checked={values.isMinor} onChange={(e) => set('isMinor', e.target.checked)} />
              {t('students.fields.isMinor')}
            </label>
            {values.isMinor && (
              <>
                <p className="text-sm text-muted-foreground sm:col-span-2">{t('students.guardianLead')}</p>
                <FormField label={t('students.fields.guardianName')} autoComplete="off" maxLength={120} {...text('guardianName')} />
                <FormField label={t('students.fields.guardianEmail')} type="email" autoComplete="off" maxLength={254} {...text('guardianEmail')} />
                <FormField label={t('students.fields.guardianPhone')} type="tel" autoComplete="off" maxLength={40} {...text('guardianPhone')} />
              </>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">{t('students.sections.subjects')}</CardTitle>
          </CardHeader>
          <CardContent>
            {!catalog.data ? (
              <output className="block">{t('common.loading')}</output>
            ) : (
              <fieldset className="grid gap-2 sm:grid-cols-3">
                <legend className="sr-only">{t('students.sections.subjects')}</legend>
                {catalog.data.subjects.map((subject) => (
                  <label key={subject.id} className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      className="h-4 w-4"
                      checked={values.subjectIds.includes(subject.id)}
                      onChange={(e) => set('subjectIds', e.target.checked ? [...values.subjectIds, subject.id] : values.subjectIds.filter((id) => id !== subject.id))}
                    />
                    {language === 'en' ? subject.nameEn : subject.nameMk}
                  </label>
                ))}
              </fieldset>
            )}
          </CardContent>
        </Card>

        {saveError && <Alert variant="error">{saveError}</Alert>}
        {save.isSuccess && student && <Alert variant="success">{t('students.saved')}</Alert>}
        <Button type="submit" className="self-start" disabled={save.isPending}>
          {save.isPending ? t('common.loading') : student ? t('students.save') : t('students.create')}
        </Button>
      </form>

      {student && version !== null && <ArchiveCard workspaceId={workspaceId} student={student} version={version} onChanged={(changed) => setVersion(num(changed.version) ?? 0)} />}
      {student && <NotesCard workspaceId={workspaceId} studentId={student.id} />}
    </section>
  );
}

function ArchiveCard({ workspaceId, student, version, onChanged }: { workspaceId: string; student: Student; version: number; onChanged: (changed: Student) => void }) {
  const { t } = useTranslation();
  const archive = useArchiveStudent(workspaceId, student.id);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{student.archived ? t('students.restoreTitle') : t('students.archiveTitle')}</CardTitle>
        <CardDescription>{student.archived ? t('students.restoreLead') : t('students.archiveLead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {archive.error && (
          <Alert variant="error">{archive.error instanceof ApiError && archive.error.code === 'CONCURRENCY_CONFLICT' ? t('students.changedElsewhere') : errorMessage(archive.error, t)}</Alert>
        )}
        <Button type="button" variant="outline" className="self-start" disabled={archive.isPending} onClick={() => archive.mutate({ archived: !student.archived, version }, { onSuccess: onChanged })}>
          {student.archived ? t('students.restore') : t('students.archive')}
        </Button>
      </CardContent>
    </Card>
  );
}

/** Private notes: only the teacher ever sees them. */
function NotesCard({ workspaceId, studentId }: { workspaceId: string; studentId: string }) {
  const { t, i18n } = useTranslation();
  const notes = useStudentNotes(workspaceId, studentId);
  const add = useAddStudentNote(workspaceId, studentId);
  const [text, setText] = useState('');
  const language = i18n.resolvedLanguage ?? 'mk';

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('students.notes.title')}</CardTitle>
        <CardDescription>{t('students.notes.lead')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <form
          className="flex flex-col gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (text.trim()) add.mutate(text.trim(), { onSuccess: () => setText('') });
          }}
        >
          <Label htmlFor="student-note">{t('students.notes.add')}</Label>
          <textarea id="student-note" rows={3} maxLength={2000} className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm" value={text} onChange={(e) => setText(e.target.value)} />
          {add.error && <Alert variant="error">{errorMessage(add.error, t)}</Alert>}
          <Button type="submit" className="self-start" disabled={add.isPending || !text.trim()}>
            {t('students.notes.save')}
          </Button>
        </form>
        {notes.data && notes.data.length > 0 && (
          <ol aria-label={t('students.notes.title')} className="flex flex-col gap-2">
            {notes.data.map((note) => (
              <li key={note.id} className="rounded-md bg-muted p-3 text-sm">
                <p className="whitespace-pre-wrap">{note.text}</p>
                <p className="mt-1 text-xs text-muted-foreground">{formatDateTime(note.createdAtUtc, language, browserTimeZone)}</p>
              </li>
            ))}
          </ol>
        )}
      </CardContent>
    </Card>
  );
}
