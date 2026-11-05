import { Plus } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { buttonVariants } from '../../../components/ui/button.tsx';
import { Card, CardDescription, CardHeader } from '../../../components/ui/card.tsx';
import { num } from '../../../teacher/offers.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import { useTests } from '../../../teacher/tests.ts';
import { NotFoundContent } from '../Pages.tsx';

export function TestsPage() {
  const { t, i18n } = useTranslation();
  const { workspaceId = '' } = useParams();
  const tests = useTests(workspaceId);
  const catalog = useCatalog();
  const language = i18n.resolvedLanguage === 'en' ? 'en' : 'mk';

  if (tests.isPending) return <output className="block">{t('common.loading')}</output>;
  if (tests.isError) return tests.error instanceof ApiError && tests.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(tests.error, t)}</Alert>;

  const subject = (id: string) => {
    const found = catalog.data?.subjects.find((s) => s.id === id);
    return found ? (language === 'en' ? found.nameEn : found.nameMk) : '';
  };

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">{t('tests.title')}</h1>
          <p className="text-sm text-muted-foreground">{t('tests.lead')}</p>
        </div>
        <Link className={buttonVariants()} to={`/app/w/${workspaceId}/tests/new`}>
          <Plus className="h-4 w-4" aria-hidden />
          {t('tests.create')}
        </Link>
      </div>
      {tests.data.length === 0 ? (
        <Card>
          <CardHeader>
            <CardDescription>{t('tests.empty')}</CardDescription>
          </CardHeader>
        </Card>
      ) : (
        <ul aria-label={t('tests.title')} className="flex flex-col gap-3">
          {tests.data.map((test) => (
            <li key={test.id}>
              <Link
                to={`/app/w/${workspaceId}/tests/${test.id}`}
                className="flex flex-col gap-1 rounded-lg border border-border bg-card p-4 hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                <span className="flex flex-wrap items-center gap-2">
                  <span className="font-semibold">{test.title}</span>
                  <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${test.status === 'Published' ? 'bg-primary text-primary-foreground' : 'bg-secondary text-secondary-foreground'}`}>
                    {t(`tests.statuses.${test.status}`)}
                  </span>
                  {test.hiddenByModeration && <span className="rounded-full bg-destructive px-2 py-0.5 text-xs font-medium text-destructive-foreground">{t('moderation.hiddenBadge')}</span>}
                </span>
                <span className="text-sm text-muted-foreground">
                  {[subject(test.subjectId), t(`tests.difficulties.${test.difficulty}`), t('tests.questionCount', { count: num(test.questionCount) ?? 0 }), t('tests.points', { count: num(test.totalPoints) ?? 0 }), t('tests.attempts', { count: num(test.attemptCount) ?? 0 })]
                    .filter(Boolean)
                    .join(' · ')}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
