import { Menu } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { NavLink, Outlet } from 'react-router';
import { Button } from '../../components/ui/button.tsx';
import { Sheet } from '../../components/ui/sheet.tsx';
import { cn } from '../../lib/utils.ts';
import { RequireAuth } from '../../auth/RequireAuth.tsx';
import { useMyWorkspaces } from '../../auth/session.ts';
import { AdminGate } from '../pages/admin/AdminGate.tsx';
import { LanguageSwitcher } from '../components/LanguageSwitcher.tsx';
import { AccountMenu, UnconfirmedEmailBanner } from '../components/SessionControls.tsx';

export interface NavItem {
  to: string;
  labelKey: string;
  end?: boolean;
}

function NavLinks({ items, onNavigate }: { items: NavItem[]; onNavigate?: () => void }) {
  const { t } = useTranslation();
  return (
    <ul className="flex flex-col gap-1">
      {items.map((item) => (
        <li key={item.labelKey}>
          <NavLink
            to={item.to}
            end={item.end}
            onClick={onNavigate}
            className={({ isActive }) =>
              cn('block rounded-md px-3 py-2 text-sm font-medium hover:bg-muted', isActive && 'bg-muted text-primary')
            }
          >
            {t(item.labelKey)}
          </NavLink>
        </li>
      ))}
    </ul>
  );
}

/**
 * Responsive application shell: sidebar navigation on desktop (≥ 768 px), a drawer behind a menu
 * button on phones. Used by the teacher, student and admin areas.
 */
export function AppShell({ areaKey, nav, actions, children }: { areaKey: string; nav: NavItem[]; actions?: ReactNode; children?: ReactNode }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  return (
    <div className="min-h-dvh bg-background text-foreground">
      <header className="sticky top-0 z-30 flex h-14 items-center gap-3 border-b border-border bg-background/95 px-4 backdrop-blur">
        <Button variant="ghost" size="icon" className="md:hidden" aria-label={t('common.openMenu')} onClick={() => setOpen(true)}>
          <Menu className="h-5 w-5" aria-hidden />
        </Button>
        <a href="/" className="font-semibold">
          {t('app.name')}
        </a>
        <span className="hidden text-sm text-muted-foreground sm:inline">· {t(areaKey)}</span>
        <div className="ml-auto flex items-center gap-2">
          {actions}
          <LanguageSwitcher />
          <AccountMenu />
        </div>
      </header>

      <div className="mx-auto flex max-w-7xl">
        <nav aria-label={t('nav.main')} className="hidden w-60 shrink-0 border-r border-border p-4 md:block">
          <NavLinks items={nav} />
        </nav>
        <main className="min-w-0 flex-1 p-4 md:p-8">
          <UnconfirmedEmailBanner />
          {children ?? <Outlet />}
        </main>
      </div>

      <Sheet open={open} onOpenChange={setOpen} title={t(areaKey)} closeLabel={t('common.close')}>
        <nav aria-label={t('nav.main')}>
          <NavLinks items={nav} onNavigate={() => setOpen(false)} />
        </nav>
      </Sheet>
    </div>
  );
}

function TeacherShell() {
  const workspaces = useMyWorkspaces();
  const first = workspaces.data?.[0];
  return (
    <AppShell
      areaKey="layout.teacherArea"
      nav={[
        { to: first ? `/app/w/${first.id}` : '/app', labelKey: 'nav.dashboard', end: true },
        ...(first
          ? [
              { to: `/app/w/${first.id}/leads`, labelKey: 'nav.leads' },
              { to: `/app/w/${first.id}/students`, labelKey: 'nav.students' },
              { to: `/app/w/${first.id}/groups`, labelKey: 'nav.groups' },
              { to: `/app/w/${first.id}/offers`, labelKey: 'nav.offers' },
              { to: `/app/w/${first.id}/site`, labelKey: 'nav.site' },
              { to: `/app/w/${first.id}/tests`, labelKey: 'nav.tests' },
              { to: `/app/w/${first.id}/profile`, labelKey: 'nav.profile' },
              { to: `/app/w/${first.id}/plan`, labelKey: 'nav.plan' },
            ]
          : []),
      ]}
    />
  );
}

export const TeacherLayout = () => (
  <RequireAuth>
    <TeacherShell />
  </RequireAuth>
);

export const StudentLayout = () => (
  <RequireAuth>
    <AppShell areaKey="layout.studentArea" nav={[{ to: '/student', labelKey: 'nav.studentHome', end: true }]} />
  </RequireAuth>
);

export const AdminLayout = () => (
  <RequireAuth>
    <AppShell
      areaKey="layout.adminArea"
      nav={[
        { to: '/admin', labelKey: 'nav.adminOverview', end: true },
        { to: '/admin/users', labelKey: 'nav.adminUsers' },
        { to: '/admin/audit', labelKey: 'nav.adminAudit' },
        { to: '/admin/plans', labelKey: 'nav.adminPlans' },
        { to: '/admin/reports', labelKey: 'nav.adminReports' },
        { to: '/admin/moderation', labelKey: 'nav.adminHidden' },
        { to: '/admin/catalog', labelKey: 'nav.adminCatalog' },
        { to: '/admin/security', labelKey: 'nav.adminSecurity' },
      ]}
    >
      {/* UX only: the API requires a platform role and an authenticator session on every admin endpoint. */}
      <AdminGate>
        <Outlet />
      </AdminGate>
    </AppShell>
  </RequireAuth>
);

/** Centered card layout for sign-in, registration and password screens. */
export function AuthLayout() {
  const { t } = useTranslation();
  return (
    <div className="flex min-h-dvh flex-col bg-muted/40">
      <header className="flex h-14 items-center justify-between px-4">
        <a href="/" className="font-semibold">
          {t('app.name')}
        </a>
        <LanguageSwitcher />
      </header>
      <main className="flex flex-1 items-start justify-center px-4 py-8 sm:items-center">
        <div className="w-full max-w-md">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
