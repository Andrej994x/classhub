import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { formatDate } from '../../../lib/format.ts';
import { num } from '../../../teacher/offers.ts';
import { usePlan } from '../../../teacher/plan.ts';
import { NotFoundContent } from '../Pages.tsx';

const browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;

/**
 * The teacher's plan (R1-P10): what it allows and what is in use. There is no self-service upgrade in the MVP; the
 * ClassHub team activates Pro (D-30).
 */
export function PlanPage() {
  const { t, i18n } = useTranslation();
  const { workspaceId = '' } = useParams();
  const plan = usePlan(workspaceId);
  const language = i18n.resolvedLanguage ?? 'mk';

  if (plan.isError) return plan.error instanceof ApiError && plan.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(plan.error, t)}</Alert>;
  if (!plan.data) return <output className="block">{t('common.loading')}</output>;
  const data = plan.data;
  const name = language === 'en' ? data.nameEn : data.nameMk;

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('plan.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('plan.lead')}</p>
      </div>

      <Card className="max-w-3xl">
        <CardHeader>
          <CardTitle className="text-xl">{t('plan.current', { name })}</CardTitle>
          <CardDescription>
            {data.paidUntilUtc ? t('plan.paidUntil', { date: formatDate(data.paidUntilUtc, language, browserTimeZone) }) : t('plan.freeForever')}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <ul aria-label={t('plan.limits')} className="flex flex-col divide-y divide-border">
            {data.limits.map((limit) => {
              const allowed = num(limit.limit) ?? 0;
              const used = limit.used === null || limit.used === undefined ? null : (num(limit.used) ?? 0);
              const full = used !== null && used >= allowed;
              return (
                <li key={limit.key} className="flex flex-col gap-1 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <span className="font-medium">{t(`plan.keys.${limit.key}`)}</span>
                  <span className={`text-sm tabular-nums ${full ? 'font-semibold text-destructive' : 'text-muted-foreground'}`}>
                    {limit.key === 'analytics-level'
                      ? t(`plan.analytics.${allowed >= 2 ? 'advanced' : 'basic'}`)
                      : used !== null
                        ? t('plan.usedOf', { used, limit: allowed.toLocaleString() })
                        : limit.key === 'storage-mb'
                          ? t('plan.storage', { limit: allowed.toLocaleString() })
                          : t('plan.upTo', { limit: allowed.toLocaleString() })}
                  </span>
                </li>
              );
            })}
          </ul>
        </CardContent>
      </Card>

      {data.code !== 'pro' && (
        <Alert variant="info" className="max-w-3xl">
          <strong className="block">{t('plan.upgradeTitle')}</strong>
          {t('plan.upgradeText')}
        </Alert>
      )}
    </section>
  );
}
