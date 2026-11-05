import { useEffect, useId, useState, type FormEvent } from 'react';
import { ContactForm } from './ContactForm.tsx';

export interface DiagnosticTestProps {
  siteSlug?: string;
  testSlug?: string;
  language?: string;
}

interface PublicTest {
  title: string;
  totalPoints: number;
  questions: { id: string; text: string; points: number; selectSeveral: boolean; options: { id: string; text: string }[] }[];
}

interface Result {
  attemptId: string;
  points: number;
  maxPoints: number;
  questionsRight: number;
  totalQuestions: number;
}

const TEXT = {
  mk: {
    loading: 'Тестот се вчитува…',
    unavailable: 'Тестот моментално не е достапен.',
    question: 'Прашање {n} од {total}',
    points: '{n} бода',
    several: 'Изберете ги сите точни одговори.',
    unanswered: 'Одговоривте на {answered} од {total} прашања. Неодговорените носат 0 бодови.',
    submit: 'Заврши го тестот',
    submitting: 'Се пресметува…',
    failed: 'Резултатот не можеше да се пресмета. Обидете се повторно.',
    tooMany: 'Тестот е решаван многу пати во последниот час. Обидете се повторно подоцна.',
    resultTitle: 'Вашиот резултат',
    score: '{points} од {max} бода',
    correct: 'Точно одговорени прашања: {correct} од {total}.',
    next: 'Сакате час за да напредувате? Испратете барање — по желба со резултатот.',
    again: 'Реши повторно',
  },
  en: {
    loading: 'Loading the test…',
    unavailable: 'The test is not available at the moment.',
    question: 'Question {n} of {total}',
    points: '{n} points',
    several: 'Choose every correct answer.',
    unanswered: 'You answered {answered} of {total} questions. Unanswered ones earn 0 points.',
    submit: 'Finish the test',
    submitting: 'Scoring…',
    failed: 'The result could not be calculated. Please try again.',
    tooMany: 'This test was taken very often in the last hour. Please try again later.',
    resultTitle: 'Your result',
    score: '{points} of {max} points',
    correct: 'Questions answered correctly: {correct} of {total}.',
    next: 'Want a lesson to improve? Send a request — with your result if you like.',
    again: 'Take it again',
  },
} as const;

export type DiagnosticTestKey = keyof (typeof TEXT)['mk'];
export const DIAGNOSTIC_TEST_TEXT = TEXT;

const fill = (text: string, values: Record<string, string | number>) => text.replace(/\{(\w+)\}/g, (_, key: string) => String(values[key] ?? ''));

/**
 * Runs a published diagnostic test (R1-P7). Questions and options come from the API without correct answers; the
 * server scores the choices and returns only totals plus an attempt id, which the contact form uses for an optional
 * lesson request (type Diagnostic, the score shared only on request, D-18). Anonymous: nothing personal is sent.
 */
export function DiagnosticTest({ siteSlug = '', testSlug = '', language = 'mk' }: DiagnosticTestProps) {
  const lang = language === 'en' ? 'en' : 'mk';
  const t = (key: DiagnosticTestKey, values: Record<string, string | number> = {}) => fill(TEXT[lang][key], values);
  const id = useId();
  const [test, setTest] = useState<PublicTest | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [answers, setAnswers] = useState<Record<string, string[]>>({});
  const [state, setState] = useState<'idle' | 'sending'>('idle');
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<Result | null>(null);
  const base = `/api/public/sites/${encodeURIComponent(siteSlug)}/tests/${encodeURIComponent(testSlug)}`;

  useEffect(() => {
    let active = true;
    fetch(base, { credentials: 'omit', cache: 'no-store' })
      .then(async (response) => {
        if (!response.ok) throw new Error(String(response.status));
        return (await response.json()) as PublicTest;
      })
      .then(
        (data) => active && setTest(data),
        () => active && setLoadFailed(true),
      );
    return () => {
      active = false;
    };
  }, [base]);

  const choose = (questionId: string, optionId: string, several: boolean, checked: boolean) =>
    setAnswers((current) => {
      const chosen = current[questionId] ?? [];
      const next = several ? (checked ? [...chosen, optionId] : chosen.filter((o) => o !== optionId)) : [optionId];
      return { ...current, [questionId]: next };
    });

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!test || state === 'sending') return;
    setState('sending');
    setError(null);
    try {
      const response = await fetch(`${base}/attempts`, {
        method: 'POST',
        credentials: 'omit',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          answers: Object.entries(answers)
            .filter(([, optionIds]) => optionIds.length > 0)
            .map(([questionId, optionIds]) => ({ questionId, optionIds })),
        }),
      });
      if (!response.ok) {
        setError(response.status === 429 ? t('tooMany') : t('failed'));
        return;
      }
      setResult((await response.json()) as Result);
    } catch {
      setError(t('failed'));
    } finally {
      setState('idle');
    }
  }

  if (loadFailed) return <p className="text-sm text-site-muted">{t('unavailable')}</p>;
  if (!test) return <p className="text-sm text-site-muted">{t('loading')}</p>;

  if (result) {
    const percent = result.maxPoints > 0 ? Math.round((result.points / result.maxPoints) * 100) : 0;
    return (
      <div className="flex flex-col gap-4">
        <div className="rounded-xl border border-site-muted/30 p-4" aria-live="polite">
          <h2 className="text-lg font-semibold">{t('resultTitle')}</h2>
          <p className="mt-1 text-3xl font-bold text-site-primary" data-testid="test-score">
            {t('score', { points: result.points, max: result.maxPoints })} <span className="text-lg text-site-muted">({percent}%)</span>
          </p>
          <p className="mt-1 text-sm">{t('correct', { correct: result.questionsRight, total: result.totalQuestions })}</p>
        </div>
        <p className="text-sm">{t('next')}</p>
        <ContactForm slug={siteSlug} language={lang} attemptId={result.attemptId} />
        <button
          type="button"
          className="self-start text-sm text-site-primary underline underline-offset-4"
          onClick={() => {
            setResult(null);
            setAnswers({});
          }}
        >
          {t('again')}
        </button>
      </div>
    );
  }

  const answered = test.questions.filter((q) => (answers[q.id] ?? []).length > 0).length;
  return (
    <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-6" aria-label={test.title}>
      <ol className="flex flex-col gap-6">
        {test.questions.map((question, index) => (
          <li key={question.id}>
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-1 font-semibold">
                <span className="block text-xs font-medium uppercase tracking-wide text-site-muted">
                  {t('question', { n: index + 1, total: test.questions.length })} · {t('points', { n: question.points })}
                </span>
                {question.text}
              </legend>
              {question.selectSeveral && <p className="text-sm text-site-muted">{t('several')}</p>}
              {question.options.map((option) => (
                <label key={option.id} className="flex items-center gap-3 rounded-lg border border-site-muted/30 px-3 py-2 has-[:checked]:border-site-primary">
                  <input
                    type={question.selectSeveral ? 'checkbox' : 'radio'}
                    name={`${id}-${question.id}`}
                    value={option.id}
                    checked={(answers[question.id] ?? []).includes(option.id)}
                    onChange={(e) => choose(question.id, option.id, question.selectSeveral, e.target.checked)}
                    className="size-4 shrink-0 accent-site-primary"
                  />
                  <span>{option.text}</span>
                </label>
              ))}
            </fieldset>
          </li>
        ))}
      </ol>
      {answered < test.questions.length && <p className="text-sm text-site-muted">{t('unanswered', { answered, total: test.questions.length })}</p>}
      {error && (
        <p role="alert" className="text-sm font-medium text-red-700">
          {error}
        </p>
      )}
      <p>
        <button
          type="submit"
          disabled={state === 'sending'}
          className="rounded-full bg-site-primary px-6 py-2 font-semibold text-site-on-primary disabled:opacity-60 focus:outline-none focus-visible:ring-2 focus-visible:ring-site-text"
        >
          {state === 'sending' ? t('submitting') : t('submit')}
        </button>
      </p>
    </form>
  );
}
