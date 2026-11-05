import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '../../../api/errors.ts';
import { PLATFORM_OWNER, usePlans, useUpdatePlan, type AdminPlan } from '../../../admin/api.ts';
import { useMe } from '../../../auth/session.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { num } from '../../../teacher/offers.ts';

/** Plans and their limits (D-29, provisional values). Only the Platform Owner changes them (D-39). */
export function AdminPlansPage() {
  const { t } = useTranslation();
  const plans = usePlans();
  const me = useMe();
  const owner = me.data?.platformRoles.includes(PLATFORM_OWNER) ?? false;

  if (plans.isError) return <Alert variant="error">{errorMessage(plans.error, t)}</Alert>;
  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('admin.plans.title')}</h1>
        <p className="text-sm text-muted-foreground">{owner ? t('admin.plans.leadOwner') : t('admin.plans.leadAdmin')}</p>
      </div>
      {!plans.data ? (
        <output className="block">{t('common.loading')}</output>
      ) : (
        <div className="grid gap-4 lg:grid-cols-2">
          {plans.data.map((plan) => (
            <PlanCard key={`${plan.id}-${plan.version}`} plan={plan} editable={owner} />
          ))}
        </div>
      )}
    </section>
  );
}

function PlanCard({ plan, editable }: { plan: AdminPlan; editable: boolean }) {
  const { t, i18n } = useTranslation();
  const update = useUpdatePlan(plan.code);
  const [values, setValues] = useState(() => Object.fromEntries(plan.limits.map((l) => [l.key ?? '', String(num(l.value) ?? 0)])));
  const parsed = Object.entries(values).map(([key, value]) => ({ key, value: /^\d{1,7}$/.test(value) ? Number(value) : NaN }));
  const valid = parsed.every((l) => Number.isInteger(l.value) && l.value >= 0 && l.value <= 1_000_000);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (valid) update.mutate({ limits: parsed, version: num(plan.version) ?? 0 });
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{i18n.resolvedLanguage === 'en' ? plan.nameEn : plan.nameMk}</CardTitle>
        <CardDescription>{plan.isDefault ? t('admin.plans.default') : t('admin.plans.paid')}</CardDescription>
      </CardHeader>
      <CardContent>
        <form noValidate onSubmit={submit} className="flex flex-col gap-3">
          {plan.limits.map((limit) => {
            const id = `plan-${plan.code}-${limit.key}`;
            return (
              <div key={limit.key} className="flex items-center justify-between gap-3">
                <Label htmlFor={id}>{t(`plan.keys.${limit.key}`)}</Label>
                <input
                  id={id}
                  inputMode="numeric"
                  readOnly={!editable}
                  className="h-10 w-28 rounded-md border border-input bg-background px-3 text-right text-sm tabular-nums read-only:bg-muted"
                  value={values[limit.key ?? ''] ?? ''}
                  onChange={(e) => setValues({ ...values, [limit.key ?? '']: e.target.value })}
                />
              </div>
            );
          })}
          {update.error && <Alert variant="error">{errorMessage(update.error, t)}</Alert>}
          {editable && (
            <Button type="submit" className="self-start" disabled={update.isPending || !valid}>
              {update.isPending ? t('common.loading') : t('admin.plans.save')}
            </Button>
          )}
        </form>
      </CardContent>
    </Card>
  );
}
