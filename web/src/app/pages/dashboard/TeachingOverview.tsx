import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { formatDateTime, formatMoney } from '../../../lib/format.ts';
import { useTeachingDashboard, type TeachingDashboard } from '../../../teacher/dashboard.ts';
import { num } from '../../../teacher/offers.ts';

const n = (value: number | string | null | undefined) => num(value) ?? 0;

/** "2026-11" → "Nov 2026" (no time zone involved: a month of payment days). */
function monthLabel(month: string, language: string): string {
  return new Intl.DateTimeFormat(language === 'en' ? 'en-GB' : 'mk-MK', { month: 'short', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${month}-01T00:00:00Z`));
}

function Tile({ label, value, hint, to }: { label: string; value: number; hint?: string; to: string }) {
  return (
    <Link to={to} className="flex h-full flex-col gap-1 rounded-lg border border-border bg-card p-4 hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring">
      <span className="text-sm text-muted-foreground">{label}</span>
      <span className="text-3xl font-semibold tabular-nums">{value.toLocaleString()}</span>
      {hint && <span className="text-xs text-muted-foreground">{hint}</span>}
    </Link>
  );
}

/**
 * The teacher's teaching numbers (R2-P12): lessons of the next 7 days, active students, what students owe, lessons left
 * in packages and payments received per month. Money is the teacher's own income from lessons, always per currency.
 */
export function TeachingOverview({ workspaceId }: { workspaceId: string }) {
  const { t } = useTranslation();
  const teaching = useTeachingDashboard(workspaceId);

  if (teaching.isError) {
    return teaching.error instanceof ApiError && teaching.error.status === 404 ? null : <Alert variant="error">{errorMessage(teaching.error, t)}</Alert>;
  }

  return (
    <section aria-labelledby="dashboard-teaching" className="flex flex-col gap-4">
      <h2 id="dashboard-teaching" className="text-lg font-semibold">
        {t('dashboard.teaching.title')}
      </h2>
      {teaching.data ? <Teaching workspaceId={workspaceId} data={teaching.data} /> : <output className="block">{t('common.loading')}</output>}
    </section>
  );
}

function Teaching({ workspaceId, data }: { workspaceId: string; data: TeachingDashboard }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'mk';
  const base = `/app/w/${workspaceId}`;
  const finance = data.finance;
  const currencies = [...new Set(finance.income.flatMap((m) => m.received.map((r) => r.currency)))].toSorted();

  return (
    <div className="flex flex-col gap-4">
      <ul aria-label={t('dashboard.teaching.title')} className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <li>
          <Tile label={t('dashboard.teaching.lessonsAhead')} value={n(data.lessons.next7Days)} hint={t('dashboard.teaching.lessonsAheadHint')} to={`${base}/calendar`} />
        </li>
        <li>
          <Tile label={t('dashboard.teaching.activeStudents')} value={n(data.activeStudents)} to={`${base}/students`} />
        </li>
        <li>
          <Tile
            label={t('dashboard.teaching.remainingLessons')}
            value={n(finance.remainingLessons)}
            hint={t('dashboard.teaching.runningLowCount', { count: n(finance.studentsRunningLow) })}
            to={`${base}/finance`}
          />
        </li>
        <li>
          <Tile label={t('dashboard.teaching.unpaidLessons')} value={n(finance.unpaidLessons)} hint={t('dashboard.teaching.unpaidLessonsHint')} to={`${base}/finance`} />
        </li>
      </ul>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">{t('dashboard.teaching.nextLessons')}</CardTitle>
            <CardDescription>{t('dashboard.teaching.nextLessonsLead')}</CardDescription>
          </CardHeader>
          <CardContent>
            {data.lessons.next.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('dashboard.teaching.noLessons')}</p>
            ) : (
              <ol aria-label={t('dashboard.teaching.nextLessons')} className="flex flex-col divide-y divide-border">
                {data.lessons.next.map((lesson) => (
                  <li key={lesson.id} className="flex flex-col gap-0.5 py-2 first:pt-0 last:pb-0">
                    <Link to={`${base}/lessons/${lesson.id}`} className="font-medium text-primary underline-offset-4 hover:underline">
                      {lesson.title}
                    </Link>
                    <span className="text-sm tabular-nums">{formatDateTime(lesson.startUtc, language, lesson.timeZone)}</span>
                    {lesson.students.length > 0 && <span className="text-sm text-muted-foreground">{lesson.students.join(', ')}</span>}
                  </li>
                ))}
              </ol>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">{t('dashboard.teaching.owed')}</CardTitle>
            <CardDescription>{t('dashboard.teaching.owedLead')}</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {finance.outstanding.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('dashboard.teaching.nothingOwed')}</p>
            ) : (
              <div className="flex flex-col gap-1">
                <ul aria-label={t('dashboard.teaching.owed')} className="flex flex-wrap gap-x-6 gap-y-1">
                  {finance.outstanding.map((amount) => (
                    <li key={amount.currency} className="text-2xl font-semibold tabular-nums">
                      {formatMoney(n(amount.amountMinor), amount.currency, language)}
                    </li>
                  ))}
                </ul>
                <p className="text-sm text-muted-foreground">{t('dashboard.teaching.studentsOwing', { count: n(finance.studentsOwing) })}</p>
              </div>
            )}
            {finance.runningLow.length > 0 && (
              <div className="flex flex-col gap-2">
                <h3 className="text-sm font-medium">{t('dashboard.teaching.runningLow')}</h3>
                <ul aria-label={t('dashboard.teaching.runningLow')} className="flex flex-col gap-1 text-sm">
                  {finance.runningLow.map((student) => (
                    <li key={student.studentId} className="flex justify-between gap-3">
                      <Link to={`${base}/students/${student.studentId}`} className="text-primary underline-offset-4 hover:underline">
                        {student.name}
                      </Link>
                      <span className="tabular-nums text-muted-foreground">{t('dashboard.teaching.lessonsLeft', { count: n(student.remainingLessons) })}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t('dashboard.teaching.income')}</CardTitle>
          <CardDescription>{t('dashboard.teaching.incomeLead')}</CardDescription>
        </CardHeader>
        <CardContent>
          {currencies.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('dashboard.teaching.noIncome')}</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <caption className="sr-only">{t('dashboard.teaching.income')}</caption>
                <thead>
                  <tr>
                    <th scope="col" className="py-1 font-medium">
                      {t('dashboard.teaching.month')}
                    </th>
                    {currencies.map((currency) => (
                      <th key={currency} scope="col" className="py-1 text-right font-medium">
                        {currency}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {finance.income.map((month) => (
                    <tr key={month.month} className="border-t border-border">
                      <th scope="row" className="py-1 font-normal">
                        {monthLabel(month.month, language)}
                      </th>
                      {currencies.map((currency) => {
                        const received = month.received.find((r) => r.currency === currency);
                        return (
                          <td key={currency} className="py-1 text-right tabular-nums">
                            {received ? formatMoney(n(received.amountMinor), currency, language) : '–'}
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
