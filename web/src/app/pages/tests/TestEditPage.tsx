import { zodResolver } from '@hookform/resolvers/zod';
import { ArrowDown, ArrowUp, Copy, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useFieldArray, useForm, useWatch, type Control, type UseFormRegister } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router';
import { z } from 'zod';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { num } from '../../../teacher/offers.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import {
  MAX_OPTIONS,
  MAX_POINTS,
  MAX_QUESTIONS,
  TEST_DIFFICULTIES,
  useArchiveTest,
  useDuplicateTest,
  useSaveTest,
  useTest,
  useTestAttempts,
  useTestStatus,
  type DiagnosticTest,
} from '../../../teacher/tests.ts';
import { NotFoundContent } from '../Pages.tsx';

const SLUG = /^[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9]))*$/;

/** Mirrors the API rules (the API and the database stay authoritative). Messages are i18n keys. */
const schema = z.object({
  title: z.string().trim().min(1, 'tests.validation.required').max(150, 'tests.validation.tooLong'),
  slug: z.string().trim().refine((v) => v === '' || (SLUG.test(v) && v.length >= 3 && v.length <= 60), 'tests.validation.slug'),
  subjectId: z.string().min(1, 'tests.validation.subjectRequired'),
  difficulty: z.enum(TEST_DIFFICULTIES),
  description: z.string().trim().max(2000, 'tests.validation.tooLong'),
  questions: z
    .array(
      z.object({
        id: z.string().nullable(),
        text: z.string().trim().min(1, 'tests.validation.required').max(1000, 'tests.validation.tooLong'),
        points: z.string().trim().regex(/^\d+$/, 'tests.validation.points').refine((v) => Number(v) <= MAX_POINTS, 'tests.validation.points'),
        options: z
          .array(z.object({ id: z.string().nullable(), text: z.string().trim().min(1, 'tests.validation.required').max(300, 'tests.validation.tooLong'), isCorrect: z.boolean() }))
          .max(MAX_OPTIONS),
      }),
    )
    .max(MAX_QUESTIONS),
  /** The version the form was loaded from (submitted with the save). */
  version: z.number().nullable(),
});
type TestForm = z.infer<typeof schema>;

const newOption = () => ({ id: null, text: '', isCorrect: false });
const newQuestion = () => ({ id: null, text: '', points: '1', options: [newOption(), newOption()] });

function toForm(test: DiagnosticTest | undefined): TestForm {
  if (!test) return { title: '', slug: '', subjectId: '', difficulty: 'Beginner', description: '', questions: [newQuestion()], version: null };
  return {
    title: test.title,
    slug: test.slug,
    subjectId: test.subjectId,
    difficulty: test.difficulty as TestForm['difficulty'],
    description: test.description ?? '',
    questions: test.questions.map((q) => ({
      id: q.id,
      text: q.text,
      points: String(num(q.points) ?? 0),
      options: q.options.map((o) => ({ id: o.id, text: o.text, isCorrect: o.isCorrect })),
    })),
    version: num(test.version),
  };
}

export function TestEditPage() {
  const { workspaceId = '', testId } = useParams();
  // Keyed by test: moving to another test (e.g. its duplicate) mounts a fresh query observer, so the editor always
  // refetches and never starts from another test's form state.
  return <TestEditLoader key={testId ?? 'new'} workspaceId={workspaceId} testId={testId} />;
}

function TestEditLoader({ workspaceId, testId }: { workspaceId: string; testId: string | undefined }) {
  const { t } = useTranslation();
  const test = useTest(workspaceId, testId);

  if (testId !== undefined) {
    if (test.isPending || (!test.isFetchedAfterMount && !test.isError)) return <output className="block">{t('common.loading')}</output>;
    if (test.isError) return test.error instanceof ApiError && test.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(test.error, t)}</Alert>;
  }

  return <TestEditor workspaceId={workspaceId} test={testId === undefined ? undefined : test.data} />;
}

/** Localized readiness problems from a 400 of publish/save (keys like "questions" or "questions[2].options"). */
function useProblemText() {
  const { t } = useTranslation();
  return (error: unknown): string[] | null => {
    if (!(error instanceof ApiError) || error.status !== 400 || Object.keys(error.fieldErrors).length === 0) return null;
    return Object.keys(error.fieldErrors).map((key) => {
      const match = /^questions\[(\d+)\]\.options$/.exec(key);
      if (match) return t('tests.problems.options', { number: Number(match[1]) + 1 });
      return key === 'questions' ? t('tests.problems.questions') : t('tests.problems.field', { field: key });
    });
  };
}

function TestEditor({ workspaceId, test }: { workspaceId: string; test: DiagnosticTest | undefined }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const catalog = useCatalog();
  const save = useSaveTest(workspaceId, test?.id);
  const form = useForm<TestForm>({ resolver: zodResolver(schema), defaultValues: toForm(test) });
  const questions = useFieldArray({ control: form.control, name: 'questions' });
  const { errors, isSubmitting, isDirty } = form.formState;
  const locked = (num(test?.attemptCount) ?? 0) > 0; // D-38
  // Status changes send the version the form was loaded from, never a newer one from a background refetch: otherwise
  // unsaved edits would be rebased onto another session's changes and a later save could overwrite them.
  const formVersion = useWatch({ control: form.control, name: 'version' });
  const language = i18n.resolvedLanguage === 'en' ? 'en' : 'mk';
  const problems = useProblemText();

  const onSubmit = form.handleSubmit(async (v) => {
    await save
      .mutateAsync({
        title: v.title.trim(),
        slug: v.slug.trim() || null,
        subjectId: v.subjectId,
        description: v.description.trim() || null,
        difficulty: v.difficulty,
        questions: v.questions.map((q) => ({
          id: q.id,
          text: q.text.trim(),
          points: Number(q.points),
          options: q.options.map((o) => ({ id: o.id, text: o.text.trim(), isCorrect: o.isCorrect })),
        })),
        version: v.version,
      })
      .then((saved) => {
        if (!test) {
          void navigate(`/app/w/${workspaceId}/tests/${saved.id}`, { replace: true });
        } else {
          form.reset(toForm(saved));
        }
      })
      .catch(() => undefined);
  });

  const saveError =
    save.error instanceof ApiError && save.error.code === 'CONCURRENCY_CONFLICT' ? t('tests.changedElsewhere') : save.error ? errorMessage(save.error, t) : null;

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{test ? test.title : t('tests.newTitle')}</h1>
        <p className="text-sm text-muted-foreground">{t('tests.editorLead')}</p>
      </div>

      {test && <StatusCard workspaceId={workspaceId} test={test} version={formVersion ?? 0} dirty={isDirty} onChanged={(changed) => form.reset(toForm(changed), { keepDirtyValues: true })} />}
      {locked && <Alert variant="info">{t('tests.locked', { count: num(test?.attemptCount) ?? 0 })}</Alert>}
      {test && <ResultsCard workspaceId={workspaceId} testId={test.id} />}

      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-6">
        <fieldset disabled={isSubmitting} aria-busy={isSubmitting} className="contents">
          <Card>
            <CardHeader>
              <CardTitle className="text-lg">{t('tests.details')}</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-4 sm:grid-cols-2">
              <FormField label={t('tests.fields.title')} maxLength={150} error={errors.title?.message && t(errors.title.message)} {...form.register('title')} />
              <FormField
                label={t('tests.fields.slug')}
                hint={t('tests.fields.slugHint')}
                autoCapitalize="none"
                spellCheck={false}
                error={errors.slug?.message && t(errors.slug.message)}
                {...form.register('slug')}
              />
              <div className="flex flex-col gap-2">
                <Label htmlFor="test-subject">{t('tests.fields.subject')}</Label>
                <select id="test-subject" disabled={locked} className="h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm" aria-invalid={errors.subjectId ? true : undefined} {...form.register('subjectId')}>
                  <option value="">{t('tests.fields.chooseSubject')}</option>
                  {catalog.data?.subjects.map((s) => (
                    <option key={s.id} value={s.id}>
                      {language === 'en' ? s.nameEn : s.nameMk}
                    </option>
                  ))}
                </select>
                {errors.subjectId?.message && <p className="text-sm text-destructive">{t(errors.subjectId.message)}</p>}
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="test-difficulty">{t('tests.fields.difficulty')}</Label>
                <select id="test-difficulty" className="h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm" {...form.register('difficulty')}>
                  {TEST_DIFFICULTIES.map((d) => (
                    <option key={d} value={d}>
                      {t(`tests.difficulties.${d}`)}
                    </option>
                  ))}
                </select>
              </div>
              <div className="flex flex-col gap-2 sm:col-span-2">
                <Label htmlFor="test-description">{t('tests.fields.description')}</Label>
                <textarea id="test-description" rows={3} maxLength={2000} className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm" {...form.register('description')} />
              </div>
            </CardContent>
          </Card>

          <div className="flex flex-col gap-3">
            <h2 className="text-lg font-semibold">{t('tests.questionsTitle')}</h2>
            <ol aria-label={t('tests.questionsTitle')} className="flex flex-col gap-3">
              {questions.fields.map((field, index) => (
                <li key={field.id}>
                  <QuestionCard
                    index={index}
                    control={form.control}
                    register={form.register}
                    locked={locked}
                    first={index === 0}
                    last={index === questions.fields.length - 1}
                    onMove={(delta) => questions.move(index, index + delta)}
                    onRemove={() => questions.remove(index)}
                    errors={errors.questions?.[index]}
                  />
                </li>
              ))}
            </ol>
            {!locked && (
              <Button type="button" variant="outline" className="self-start" disabled={questions.fields.length >= MAX_QUESTIONS} onClick={() => questions.append(newQuestion())}>
                <Plus className="h-4 w-4" aria-hidden />
                {t('tests.addQuestion')}
              </Button>
            )}
          </div>

          {Object.keys(errors).length > 0 && <Alert variant="error">{t('tests.checkFields')}</Alert>}
          {saveError && (
            <Alert variant="error">
              {problems(save.error)?.join(' ') ?? saveError}
            </Alert>
          )}
          {save.isSuccess && !isDirty && <Alert variant="success">{t('tests.saved')}</Alert>}
          <div className="sticky bottom-0 -mx-4 flex flex-wrap gap-2 border-t border-border bg-background/95 px-4 py-3 backdrop-blur md:static md:mx-0 md:border-0 md:bg-transparent md:p-0">
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? t('common.loading') : test ? t('tests.save') : t('tests.createDraft')}
            </Button>
          </div>
        </fieldset>
      </form>
    </section>
  );
}

function StatusCard({ workspaceId, test, version, dirty, onChanged }: { workspaceId: string; test: DiagnosticTest; version: number; dirty: boolean; onChanged: (test: DiagnosticTest) => void }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const status = useTestStatus(workspaceId, test.id);
  const duplicate = useDuplicateTest(workspaceId, test.id);
  const archive = useArchiveTest(workspaceId, test.id);
  const [confirmArchive, setConfirmArchive] = useState(false);
  const problems = useProblemText();
  const busy = status.isPending || duplicate.isPending || archive.isPending;
  const error = status.error ?? duplicate.error ?? archive.error;
  const errorText =
    error instanceof ApiError && error.code === 'CONCURRENCY_CONFLICT' ? t('tests.changedElsewhere') : error ? (problems(error)?.join(' ') ?? errorMessage(error, t)) : null;

  return (
    <Card data-testid="test-status">
      <CardHeader>
        <CardTitle className="text-lg">
          {t('tests.status')}: {t(`tests.statuses.${test.status}`)}
        </CardTitle>
        <CardDescription>{t('tests.statusHints.' + test.status)}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {errorText && <Alert variant="error">{errorText}</Alert>}
        <div className="flex flex-wrap items-center gap-2">
          {test.status === 'Published' ? (
            <Button type="button" variant="outline" disabled={busy} onClick={() => status.mutate({ publish: false, version }, { onSuccess: onChanged })}>
              {t('tests.deactivate')}
            </Button>
          ) : (
            <Button type="button" disabled={busy || dirty} onClick={() => status.mutate({ publish: true, version }, { onSuccess: onChanged })}>
              {t('tests.publish')}
            </Button>
          )}
          <Button
            type="button"
            variant="outline"
            disabled={busy || dirty}
            onClick={() => duplicate.mutate(undefined, { onSuccess: (copy) => void navigate(`/app/w/${workspaceId}/tests/${copy.id}`) })}
          >
            <Copy className="h-4 w-4" aria-hidden />
            {t('tests.duplicate')}
          </Button>
          {!confirmArchive ? (
            <Button type="button" variant="ghost" disabled={busy} onClick={() => setConfirmArchive(true)}>
              {t('tests.archive')}
            </Button>
          ) : (
            <span className="flex flex-wrap items-center gap-2">
              <span className="text-sm">{t('tests.confirmArchive')}</span>
              <Button type="button" variant="destructive" disabled={busy} onClick={() => archive.mutate(undefined, { onSuccess: () => void navigate(`/app/w/${workspaceId}/tests`) })}>
                {t('tests.archiveConfirm')}
              </Button>
              <Button type="button" variant="ghost" onClick={() => setConfirmArchive(false)}>
                {t('tests.cancel')}
              </Button>
            </span>
          )}
          {dirty && test.status !== 'Published' && <p className="text-sm text-muted-foreground">{t('tests.saveFirst')}</p>}
        </div>
      </CardContent>
    </Card>
  );
}

/** How often the test was taken, and the results visitors shared with a contact request (D-18). */
function ResultsCard({ workspaceId, testId }: { workspaceId: string; testId: string }) {
  const { t, i18n } = useTranslation();
  const attempts = useTestAttempts(workspaceId, testId);
  const language = i18n.resolvedLanguage ?? 'mk';
  const timeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
  if (!attempts.data) return null;
  const total = num(attempts.data.total) ?? 0;

  return (
    <Card data-testid="test-results">
      <CardHeader>
        <CardTitle className="text-lg">{t('tests.results.title')}</CardTitle>
        <CardDescription>
          {t('tests.results.summary', { count: total })} {total > 0 && t('tests.results.shared', { count: num(attempts.data.sharedCount) ?? 0 })}
        </CardDescription>
      </CardHeader>
      {attempts.data.shared.length > 0 && (
        <CardContent>
          <ul aria-label={t('tests.results.sharedList')} className="flex flex-col gap-2">
            {attempts.data.shared.map((attempt) => (
              <li key={attempt.attemptId} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border p-3 text-sm">
                <span>
                  <strong>{t('tests.results.score', { points: num(attempt.points) ?? 0, max: num(attempt.maxPoints) ?? 0 })}</strong>
                  {' · '}
                  {new Intl.DateTimeFormat(language === 'en' ? 'en-GB' : 'mk-MK', { dateStyle: 'medium', timeStyle: 'short', timeZone }).format(new Date(attempt.completedAtUtc))}
                </span>
                {attempt.leadId && (
                  <Link className="text-primary underline-offset-4 hover:underline" to={`/app/w/${workspaceId}/leads/${attempt.leadId}`}>
                    {t('tests.results.openRequest')}
                  </Link>
                )}
              </li>
            ))}
          </ul>
        </CardContent>
      )}
    </Card>
  );
}

interface QuestionCardProps {
  index: number;
  control: Control<TestForm>;
  register: UseFormRegister<TestForm>;
  locked: boolean;
  first: boolean;
  last: boolean;
  onMove: (delta: -1 | 1) => void;
  onRemove: () => void;
  errors: NonNullable<ReturnType<typeof useForm<TestForm>>['formState']['errors']['questions']>[number] | undefined;
}

function QuestionCard({ index, control, register, locked, first, last, onMove, onRemove, errors }: QuestionCardProps) {
  const { t } = useTranslation();
  const options = useFieldArray({ control, name: `questions.${index}.options` });
  const values = useWatch({ control, name: `questions.${index}.options` });
  const number = index + 1;
  const message = (key: string | undefined) => (key ? t(key) : undefined);
  const noCorrect = values.length > 0 && !values.some((o) => o.isCorrect);

  return (
    <Card data-question={number}>
      <CardHeader className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <CardTitle className="text-base">{t('tests.questionLabel', { number })}</CardTitle>
        <div className="flex flex-wrap items-center gap-1">
          <Button type="button" variant="ghost" size="sm" disabled={first} aria-label={t('tests.moveUp', { number })} onClick={() => onMove(-1)}>
            <ArrowUp className="h-4 w-4" aria-hidden />
          </Button>
          <Button type="button" variant="ghost" size="sm" disabled={last} aria-label={t('tests.moveDown', { number })} onClick={() => onMove(1)}>
            <ArrowDown className="h-4 w-4" aria-hidden />
          </Button>
          {!locked && (
            <Button type="button" variant="ghost" size="sm" aria-label={t('tests.removeQuestion', { number })} onClick={onRemove}>
              <Trash2 className="h-4 w-4" aria-hidden />
            </Button>
          )}
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <div className="flex flex-col gap-2">
          <Label htmlFor={`question-${index}-text`}>{t('tests.fields.questionText', { number })}</Label>
          <textarea id={`question-${index}-text`} rows={2} maxLength={1000} aria-invalid={errors?.text ? true : undefined} className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm" {...register(`questions.${index}.text`)} />
          {errors?.text?.message && <p className="text-sm text-destructive">{t(errors.text.message)}</p>}
        </div>
        <div className="max-w-40">
          <FormField label={t('tests.fields.points', { number })} inputMode="numeric" disabled={locked} error={message(errors?.points?.message)} {...register(`questions.${index}.points`)} />
        </div>
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-sm font-medium">{t('tests.optionsLegend', { number })}</legend>
          {options.fields.map((option, optionIndex) => (
            <div key={option.id} className="flex flex-col gap-1">
              <div className="flex items-center gap-2">
                <label className="flex shrink-0 items-center gap-1 text-sm">
                  <input type="checkbox" className="h-4 w-4" disabled={locked} aria-label={t('tests.correctLabel', { number, option: optionIndex + 1 })} {...register(`questions.${index}.options.${optionIndex}.isCorrect`)} />
                  <span aria-hidden>{t('tests.correct')}</span>
                </label>
                <input
                  aria-label={t('tests.optionLabel', { number, option: optionIndex + 1 })}
                  maxLength={300}
                  aria-invalid={errors?.options?.[optionIndex]?.text ? true : undefined}
                  className="h-11 min-w-0 flex-1 rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm"
                  {...register(`questions.${index}.options.${optionIndex}.text`)}
                />
                {!locked && (
                  <Button type="button" variant="ghost" size="sm" aria-label={t('tests.removeOption', { number, option: optionIndex + 1 })} onClick={() => options.remove(optionIndex)}>
                    <Trash2 className="h-4 w-4" aria-hidden />
                  </Button>
                )}
              </div>
              {errors?.options?.[optionIndex]?.text?.message && <p className="text-sm text-destructive">{t(errors.options[optionIndex].text.message)}</p>}
            </div>
          ))}
          {noCorrect && <p className="text-sm text-muted-foreground">{t('tests.noCorrectHint')}</p>}
          {!locked && (
            <Button type="button" variant="outline" size="sm" className="self-start" disabled={options.fields.length >= MAX_OPTIONS} onClick={() => options.append(newOption())}>
              <Plus className="h-4 w-4" aria-hidden />
              {t('tests.addOption')}
            </Button>
          )}
        </fieldset>
      </CardContent>
    </Card>
  );
}
