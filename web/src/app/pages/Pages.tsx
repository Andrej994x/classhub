import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link, Navigate, useParams } from 'react-router';
import { api } from '../../api/client.ts';
import { ApiError, errorMessage } from '../../api/errors.ts';
import { useMe, useMyWorkspaces } from '../../auth/session.ts';
import { Alert } from '../../components/ui/alert.tsx';
import { OnboardingChecklist } from './profile/OnboardingChecklist.tsx';
import { buttonVariants } from '../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card.tsx';
import { LanguageSwitcher } from '../components/LanguageSwitcher.tsx';

export function HomePage() {
  const { t } = useTranslation();
  const me = useMe();
  return (
    <div className="min-h-dvh bg-background">
      <header className="flex h-14 items-center justify-between px-4">
        <span className="font-semibold">{t('app.name')}</span>
        <LanguageSwitcher />
      </header>
      <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-16">
        <h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">{t('home.title')}</h1>
        <p className="text-lg text-muted-foreground">{t('home.lead')}</p>
        <div className="flex flex-wrap gap-3">
          {me.data ? (
            <Link className={buttonVariants()} to="/app">
              {t('nav.openWorkspace')}
            </Link>
          ) : (
            <>
              <Link className={buttonVariants()} to="/register">
                {t('nav.register')}
              </Link>
              <Link className={buttonVariants({ variant: 'outline' })} to="/login">
                {t('nav.signIn')}
              </Link>
            </>
          )}
        </div>
      </main>
    </div>
  );
}

function AreaHome({ titleKey, emptyKey }: { titleKey: string; emptyKey: string }) {
  const { t } = useTranslation();
  return (
    <section className="flex flex-col gap-6">
      <h1 className="text-2xl font-semibold tracking-tight">{t(titleKey)}</h1>
      <Card>
        <CardHeader>
          <CardDescription>{t(emptyKey)}</CardDescription>
        </CardHeader>
      </Card>
    </section>
  );
}

/** `/app`: opens the teacher's workspace (one per teacher in the MVP). */
export function TeacherHomePage() {
  const { t } = useTranslation();
  const workspaces = useMyWorkspaces();
  if (workspaces.isPending) return <output className="block">{t('common.loading')}</output>;
  if (workspaces.isError) return <Alert variant="error">{errorMessage(workspaces.error, t)}</Alert>;
  const first = workspaces.data[0];
  return first ? <Navigate to={`/app/w/${first.id}`} replace /> : <AreaHome titleKey="dashboard.title" emptyKey="dashboard.noWorkspace" />;
}

/** `/app/w/:workspaceId`: the workspace dashboard. Other teachers' workspaces answer 404 ("not found"). */
export function WorkspaceDashboardPage() {
  const { t } = useTranslation();
  const { workspaceId = '' } = useParams();
  const workspace = useQuery({
    queryKey: ['w', workspaceId],
    queryFn: async () => (await api.client.GET('/api/w/{workspaceId}', { params: { path: { workspaceId } } })).data!,
  });

  if (workspace.isPending) return <output className="block">{t('common.loading')}</output>;
  if (workspace.isError) {
    return workspace.error instanceof ApiError && workspace.error.status === 404 ? (
      <NotFoundContent />
    ) : (
      <Alert variant="error">{errorMessage(workspace.error, t)}</Alert>
    );
  }
  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <p className="text-sm text-muted-foreground">{t('dashboard.title')}</p>
        <h1 className="text-2xl font-semibold tracking-tight">{workspace.data.name}</h1>
      </div>
      <OnboardingChecklist workspaceId={workspaceId} />
    </section>
  );
}
export const StudentHomePage = () => <AreaHome titleKey="student.title" emptyKey="student.empty" />;
export const AdminHomePage = () => <AreaHome titleKey="admin.title" emptyKey="admin.empty" />;

/** "Not found" inside an area layout (unknown route, or a workspace the user is not a member of). */
export function NotFoundContent() {
  const { t } = useTranslation();
  return (
    <Card>
      <CardHeader>
        <h1 className="text-xl font-semibold tracking-tight">{t('notFound.title')}</h1>
        <CardDescription>{t('notFound.lead')}</CardDescription>
      </CardHeader>
    </Card>
  );
}

export function NotFoundPage() {
  const { t } = useTranslation();
  return (
    <main className="mx-auto flex min-h-dvh max-w-md flex-col justify-center px-4">
      <Card>
        <CardHeader>
          <CardTitle>
            <span className="sr-only">404 · </span>
            {t('notFound.title')}
          </CardTitle>
          <CardDescription>{t('notFound.lead')}</CardDescription>
        </CardHeader>
        <CardContent>
          <Link className={buttonVariants({ variant: 'outline' })} to="/">
            {t('common.backHome')}
          </Link>
        </CardContent>
      </Card>
    </main>
  );
}
