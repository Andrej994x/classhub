import { CheckCircle2, Circle, Clock } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { useOnboarding } from '../../../teacher/queries.ts';

/** First steps for a new teacher, derived from the workspace's data (never ticked manually). */
export function OnboardingChecklist({ workspaceId }: { workspaceId: string }) {
  const { t } = useTranslation();
  const onboarding = useOnboarding(workspaceId);

  if (onboarding.isPending) return <output className="block text-sm text-muted-foreground">{t('common.loading')}</output>;
  if (onboarding.isError) return <Alert variant="error">{errorMessage(onboarding.error, t)}</Alert>;

  const { steps } = onboarding.data;
  // Integers may arrive as strings in the generated types (OpenAPI int32 pattern); normalize.
  const completed = Number(onboarding.data.completed);
  const total = Number(onboarding.data.total);
  const links: Record<string, string> = {
    profile: `/app/w/${workspaceId}/profile`,
    subjects: `/app/w/${workspaceId}/profile#teaching`,
    'first-offer': `/app/w/${workspaceId}/offers/new`,
    'public-page': `/app/w/${workspaceId}/site`,
    publish: `/app/w/${workspaceId}/site`,
  };
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('onboarding.title')}</CardTitle>
        <CardDescription>{t('onboarding.progress', { completed, total })}</CardDescription>
        <progress
          value={completed}
          max={total}
          aria-label={t('onboarding.title')}
          className="mt-2 h-2 w-full overflow-hidden rounded-full [&::-moz-progress-bar]:bg-primary [&::-webkit-progress-bar]:bg-muted [&::-webkit-progress-value]:bg-primary"
        />
      </CardHeader>
      <CardContent>
        <ol aria-label={t('onboarding.title')} className="flex flex-col gap-3">
          {steps.map((step) => {
            const label = t(`onboarding.steps.${step.key}`);
            const link = links[step.key];
            return (
              <li key={step.key} className="flex items-start gap-3" data-step={step.key} data-completed={step.completed}>
                {step.completed ? (
                  <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden />
                ) : step.available ? (
                  <Circle className="mt-0.5 h-5 w-5 shrink-0 text-muted-foreground" aria-hidden />
                ) : (
                  <Clock className="mt-0.5 h-5 w-5 shrink-0 text-muted-foreground" aria-hidden />
                )}
                <div className="flex flex-col">
                  {step.available && !step.completed && link ? (
                    <Link to={link} className="font-medium text-primary underline-offset-4 hover:underline">
                      {label}
                    </Link>
                  ) : (
                    <span className={step.completed ? 'font-medium' : 'font-medium text-muted-foreground'}>{label}</span>
                  )}
                  <span className="text-sm text-muted-foreground">
                    {step.completed ? t('onboarding.done') : step.available ? t('onboarding.todo') : t('onboarding.soon')}
                  </span>
                </div>
              </li>
            );
          })}
        </ol>
      </CardContent>
    </Card>
  );
}
