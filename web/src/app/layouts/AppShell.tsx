import { Menu } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, NavLink, Outlet } from 'react-router';
import { Button } from '../../components/ui/button.tsx';
import { Sheet } from '../../components/ui/sheet.tsx';
import { cn } from '../../lib/utils.ts';
import { LanguageSwitcher } from '../components/LanguageSwitcher.tsx';

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
        <li key={item.to}>
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
export function AppShell({ areaKey, nav, actions }: { areaKey: string; nav: NavItem[]; actions?: ReactNode }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  return (
    <div className="min-h-dvh bg-background text-foreground">
      <header className="sticky top-0 z-30 flex h-14 items-center gap-3 border-b border-border bg-background/95 px-4 backdrop-blur">
        <Button variant="ghost" size="icon" className="md:hidden" aria-label={t('common.openMenu')} onClick={() => setOpen(true)}>
          <Menu className="h-5 w-5" aria-hidden />
        </Button>
        <Link to="/" className="font-semibold">
          {t('app.name')}
        </Link>
        <span className="hidden text-sm text-muted-foreground sm:inline">· {t(areaKey)}</span>
        <div className="ml-auto flex items-center gap-2">
          {actions}
          <LanguageSwitcher />
        </div>
      </header>

      <div className="mx-auto flex max-w-7xl">
        <nav aria-label={t('nav.main')} className="hidden w-60 shrink-0 border-r border-border p-4 md:block">
          <NavLinks items={nav} />
        </nav>
        <main className="min-w-0 flex-1 p-4 md:p-8">
          <Outlet />
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

export const TeacherLayout = () => (
  <AppShell
    areaKey="layout.teacherArea"
    nav={[
      { to: '/app', labelKey: 'nav.dashboard', end: true },
      { to: '/app/settings', labelKey: 'nav.settings' },
    ]}
  />
);

export const StudentLayout = () => <AppShell areaKey="layout.studentArea" nav={[{ to: '/student', labelKey: 'nav.studentHome', end: true }]} />;

export const AdminLayout = () => <AppShell areaKey="layout.adminArea" nav={[{ to: '/admin', labelKey: 'nav.adminHome', end: true }]} />;

/** Centered card layout for sign-in, registration and password screens. */
export function AuthLayout() {
  const { t } = useTranslation();
  return (
    <div className="flex min-h-dvh flex-col bg-muted/40">
      <header className="flex h-14 items-center justify-between px-4">
        <Link to="/" className="font-semibold">
          {t('app.name')}
        </Link>
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
