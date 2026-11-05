import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate, useParams } from 'react-router';
import { api } from '../../api/client.ts';
import { ApiError, errorMessage } from '../../api/errors.ts';
import { useMyWorkspaces } from '../../auth/session.ts';
import { Alert } from '../../components/ui/alert.tsx';
import { loadFromServer } from '../../lib/navigation.ts';
import { OnboardingChecklist } from './profile/OnboardingChecklist.tsx';
import { buttonVariants } from '../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card.tsx';

/**
 * `/` belongs to the server-rendered landing page (Razor, SEO). The SPA only reaches this route through client-side
 * navigation, so it hands over to the server with a full page load.
 */
export function HomePage() {
  useEffect(() => loadFromServer('/'), []);
  return null;
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
          <a className={buttonVariants({ variant: 'outline' })} href="/">
            {t('common.backHome')}
          </a>
        </CardContent>
      </Card>
    </main>
  );
}
