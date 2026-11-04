import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { buttonVariants } from '../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card.tsx';
import { LanguageSwitcher } from '../components/LanguageSwitcher.tsx';

export function HomePage() {
  const { t } = useTranslation();
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
          <Link className={buttonVariants()} to="/register">
            {t('nav.register')}
          </Link>
          <Link className={buttonVariants({ variant: 'outline' })} to="/login">
            {t('nav.signIn')}
          </Link>
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

export const TeacherDashboardPage = () => <AreaHome titleKey="dashboard.title" emptyKey="dashboard.empty" />;
export const StudentHomePage = () => <AreaHome titleKey="student.title" emptyKey="student.empty" />;
export const AdminHomePage = () => <AreaHome titleKey="admin.title" emptyKey="admin.empty" />;

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
