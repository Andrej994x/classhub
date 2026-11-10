import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Card, CardContent, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { usePlatformUsage, USAGE_PERIODS, type UsagePeriod } from '../../../teacher/usage.ts';

const n = (value: number | string) => Number(value);

/** `/admin/usage` (R3-P6): platform-wide toolbox usage for staff; totals only, never a workspace or a person. */
export function AdminUsagePage() {
  const { t, i18n } = useTranslation();
  const [days, setDays] = useState<UsagePeriod>(30);
  const usage = usePlatformUsage(days);
  const locale = i18n.resolvedLanguage === 'en' ? 'en-GB' : 'mk-MK';
  const dayLabel = (day: string) => new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(`${day}T00:00:00Z`));

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('admin.usage.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('admin.usage.lead')}</p>
      </div>
      <fieldset className="inline-flex self-start rounded-md border border-border p-0.5">
        <legend className="sr-only">{t('admin.usage.period')}</legend>
        {USAGE_PERIODS.map((period) => (
          <label key={period} className="cursor-pointer">
            <input type="radio" name="usage-period" className="peer sr-only" checked={days === period} onChange={() => setDays(period)} />
            <span className="block rounded px-3 py-1.5 text-sm font-medium text-muted-foreground hover:bg-muted peer-checked:bg-primary peer-checked:text-primary-foreground peer-focus-visible:ring-2 peer-focus-visible:ring-ring">
              {t('dashboard.days', { count: period })}
            </span>
          </label>
        ))}
      </fieldset>
      {usage.isError ? (
        <Alert variant="error">{errorMessage(usage.error, t)}</Alert>
      ) : !usage.data ? (
        <output className="block">{t('common.loading')}</output>
      ) : (
        <>
          <p className="text-lg font-medium">{t('admin.usage.workspaces', { count: n(usage.data.workspaces) })}</p>
          <div className="grid gap-4 lg:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle className="text-base">{t('admin.usage.tools')}</CardTitle>
              </CardHeader>
              <CardContent>
                {usage.data.tools.length === 0 ? (
                  <p className="text-sm text-muted-foreground">{t('admin.usage.none')}</p>
                ) : (
                  <table className="w-full text-left text-sm">
                    <caption className="sr-only">{t('admin.usage.tools')}</caption>
                    <thead>
                      <tr>
                        <th scope="col" className="font-medium">{t('admin.usage.tool')}</th>
                        <th scope="col" className="text-right font-medium">{t('admin.usage.opens')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {usage.data.tools.map((tool) => (
                        <tr key={tool.code} className="border-t border-border">
                          <th scope="row" className="py-1 font-normal">{tool.code}</th>
                          <td className="py-1 text-right tabular-nums">{n(tool.opens)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle className="text-base">{t('admin.usage.boards')}</CardTitle>
              </CardHeader>
              <CardContent>
                {usage.data.boards.length === 0 ? (
                  <p className="text-sm text-muted-foreground">{t('admin.usage.none')}</p>
                ) : (
                  <table className="w-full text-left text-sm">
                    <caption className="sr-only">{t('admin.usage.boards')}</caption>
                    <thead>
                      <tr>
                        <th scope="col" className="font-medium">{t('admin.usage.board')}</th>
                        <th scope="col" className="text-right font-medium">{t('admin.usage.created')}</th>
                        <th scope="col" className="text-right font-medium">{t('admin.usage.reused')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {usage.data.boards.map((board) => (
                        <tr key={board.board} className="border-t border-border">
                          <th scope="row" className="py-1 font-normal">{t(`activities.boards.${board.board}`)}</th>
                          <td className="py-1 text-right tabular-nums">{n(board.created)}</td>
                          <td className="py-1 text-right tabular-nums">{n(board.reused)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </CardContent>
            </Card>
          </div>
          <Card>
            <CardHeader>
              <CardTitle className="text-base">{t('admin.usage.daily')}</CardTitle>
            </CardHeader>
            <CardContent className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <caption className="sr-only">{t('admin.usage.daily')}</caption>
                <thead>
                  <tr>
                    <th scope="col" className="font-medium">{t('admin.usage.day')}</th>
                    <th scope="col" className="text-right font-medium">{t('admin.usage.opens')}</th>
                    <th scope="col" className="text-right font-medium">{t('admin.usage.created')}</th>
                    <th scope="col" className="text-right font-medium">{t('admin.usage.reused')}</th>
                  </tr>
                </thead>
                <tbody>
                  {usage.data.daily
                    .filter((d) => n(d.toolOpens) + n(d.activitiesCreated) + n(d.activitiesReused) > 0)
                    .toReversed()
                    .map((d) => (
                      <tr key={d.day} className="border-t border-border">
                        <th scope="row" className="py-1 font-normal">{dayLabel(d.day)}</th>
                        <td className="py-1 text-right tabular-nums">{n(d.toolOpens)}</td>
                        <td className="py-1 text-right tabular-nums">{n(d.activitiesCreated)}</td>
                        <td className="py-1 text-right tabular-nums">{n(d.activitiesReused)}</td>
                      </tr>
                    ))}
                </tbody>
              </table>
            </CardContent>
          </Card>
        </>
      )}
    </section>
  );
}
