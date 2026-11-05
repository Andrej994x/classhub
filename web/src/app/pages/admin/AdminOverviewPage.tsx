import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { errorMessage } from '../../../api/errors.ts';
import { useAdminOverview } from '../../../admin/api.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { num } from '../../../teacher/offers.ts';

const n = (value: number | string | null | undefined) => num(value) ?? 0;

/** Platform numbers (aggregates only). */
export function AdminOverviewPage() {
  const { t } = useTranslation();
  const overview = useAdminOverview();

  if (overview.isError) return <Alert variant="error">{errorMessage(overview.error, t)}</Alert>;
  const data = overview.data;
  const tiles = data
    ? [
        { key: 'users', value: n(data.users), hint: t('admin.overview.suspended', { count: n(data.suspendedUsers) }), to: '/admin/users' },
        { key: 'teachers', value: n(data.teachers) },
        { key: 'activeTeachers', value: n(data.activeTeachers), hint: t('admin.overview.activeHint') },
        { key: 'newUsers', value: n(data.newUsersLast7Days), hint: t('admin.overview.newUsersHint', { count: n(data.newUsersLast30Days) }) },
        { key: 'publishedSites', value: n(data.publishedSites) },
        { key: 'staff', value: n(data.platformStaff), to: '/admin/users?staff=true' },
      ]
    : [];

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('admin.overview.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('admin.overview.lead')}</p>
      </div>
      {!data ? (
        <output className="block">{t('common.loading')}</output>
      ) : (
        <ul aria-label={t('admin.overview.title')} className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {tiles.map((tile) => {
            const body = (
              <>
                <span className="text-sm text-muted-foreground">{t(`admin.overview.${tile.key}`)}</span>
                <span className="text-3xl font-semibold tabular-nums">{tile.value.toLocaleString()}</span>
                {tile.hint && <span className="text-xs text-muted-foreground">{tile.hint}</span>}
              </>
            );
            const className = 'flex h-full flex-col gap-1 rounded-lg border border-border bg-card p-4';
            return (
              <li key={tile.key}>
                {tile.to ? (
                  <Link to={tile.to} className={`${className} hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring`}>
                    {body}
                  </Link>
                ) : (
                  <div className={className}>{body}</div>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
