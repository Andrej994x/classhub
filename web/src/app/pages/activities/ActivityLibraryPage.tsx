import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { formatDateTime } from '../../../lib/format.ts';
import { BOARDS, useActivities, useDeleteActivity, useDuplicateActivity, useRenameActivity, type ActivityFilter, type ActivityItem } from '../../../teacher/activities.ts';
import { num } from '../../../teacher/offers.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import { useToolbox } from '../../../teacher/toolbox.ts';

const field = 'h-10 w-full rounded-md border border-input bg-background px-3 text-sm';
const viewerTimeZone = () => new Intl.DateTimeFormat().resolvedOptions().timeZone;
const ALL_TOOLS = { q: '', subjectId: '', category: '' };

/**
 * `/app/w/:workspaceId/activities` (R3-P2): the teacher's saved board activities. Search and filter, open on the board
 * (once the board is available), rename, duplicate and delete.
 */
export function ActivityLibraryPage() {
  const { t, i18n } = useTranslation();
  const { workspaceId = '' } = useParams();
  const [filter, setFilter] = useState<ActivityFilter>({ q: '', subjectId: '', toolType: '', page: 1 });
  const activities = useActivities(workspaceId, filter);
  const catalog = useCatalog();
  // A board's activities open on the board once it is in the toolbox.
  const toolbox = useToolbox(workspaceId, ALL_TOOLS);
  const openable = new Set(toolbox.data?.tools.filter((tool) => tool.kind === 'BuiltIn').map((tool) => tool.builtInKey) ?? []);
  const en = i18n.resolvedLanguage === 'en';
  const subjectName = (id: string | null | undefined) => {
    const subject = catalog.data?.subjects.find((s) => s.id === id);
    return subject ? (en ? subject.nameEn : subject.nameMk) : null;
  };
  const set = (change: Partial<ActivityFilter>) => setFilter((current) => ({ ...current, page: 1, ...change }));
  const pages = activities.data ? Math.max(1, Math.ceil(n(activities.data.total) / n(activities.data.pageSize))) : 1;
  // Deleting the only activity of a later page: that page is gone, so show the one before it.
  const deleted = () => {
    if (filter.page > 1 && activities.data?.items.length === 1) setFilter((current) => ({ ...current, page: current.page - 1 }));
  };

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('activities.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('activities.lead')}</p>
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
        <label className="flex flex-col gap-1 text-sm font-medium">
          {t('activities.search')}
          <input type="search" className={field} value={filter.q} maxLength={100} onChange={(e) => set({ q: e.target.value })} />
        </label>
        <label className="flex flex-col gap-1 text-sm font-medium">
          {t('toolbox.subject')}
          <select className={field} value={filter.subjectId} onChange={(e) => set({ subjectId: e.target.value })}>
            <option value="">{t('toolbox.allSubjects')}</option>
            {catalog.data?.subjects.map((s) => (
              <option key={s.id} value={s.id}>
                {en ? s.nameEn : s.nameMk}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm font-medium">
          {t('activities.board')}
          <select className={field} value={filter.toolType} onChange={(e) => set({ toolType: e.target.value })}>
            <option value="">{t('activities.allBoards')}</option>
            {BOARDS.map((board) => (
              <option key={board} value={board}>
                {t(`activities.boards.${board}`)}
              </option>
            ))}
          </select>
        </label>
      </div>

      {activities.isError ? (
        <Alert variant="error">{errorMessage(activities.error, t)}</Alert>
      ) : !activities.data ? (
        <output className="block">{t('common.loading')}</output>
      ) : activities.data.items.length === 0 ? (
        <p className="text-sm text-muted-foreground">{filter.q || filter.subjectId || filter.toolType ? t('activities.noMatch') : t('activities.empty')}</p>
      ) : (
        <ul aria-label={t('activities.title')} className="flex flex-col gap-3">
          {activities.data.items.map((item) => (
            <li key={`${item.id}-${item.version}`}>
              <ActivityRow workspaceId={workspaceId} item={item} subject={subjectName(item.subjectId)} canOpen={openable.has(item.toolType)} onDeleted={deleted} />
            </li>
          ))}
        </ul>
      )}

      {pages > 1 && (
        <nav aria-label={t('leads.pagination')} className="flex items-center gap-2">
          <Button type="button" size="sm" variant="outline" disabled={filter.page <= 1} onClick={() => setFilter({ ...filter, page: filter.page - 1 })}>
            {t('leads.previous')}
          </Button>
          <span className="text-sm">{t('leads.pageOf', { page: filter.page, pages })}</span>
          <Button type="button" size="sm" variant="outline" disabled={filter.page >= pages} onClick={() => setFilter({ ...filter, page: filter.page + 1 })}>
            {t('leads.next')}
          </Button>
        </nav>
      )}
    </section>
  );
}

const n = (value: number | string | null | undefined) => num(value) ?? 0;

function ActivityRow({ workspaceId, item, subject, canOpen, onDeleted }: { workspaceId: string; item: ActivityItem; subject: string | null; canOpen: boolean; onDeleted: () => void }) {
  const { t, i18n } = useTranslation();
  const rename = useRenameActivity(workspaceId);
  const duplicate = useDuplicateActivity(workspaceId);
  const remove = useDeleteActivity(workspaceId);
  const catalog = useCatalog();
  const [editing, setEditing] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [title, setTitle] = useState(item.title);
  const [subjectId, setSubjectId] = useState(item.subjectId ?? '');
  const en = i18n.resolvedLanguage === 'en';
  const error = rename.error ?? duplicate.error ?? remove.error;

  const save = (event: FormEvent) => {
    event.preventDefault();
    rename.mutate({ item, title: title.trim(), subjectId: subjectId || null }, { onSuccess: () => setEditing(false) });
  };

  return (
    <article aria-label={item.title} className="flex flex-col gap-2 rounded-lg border border-border bg-card p-4">
      {error && <Alert variant="error">{errorMessage(error, t)}</Alert>}
      {editing ? (
        <form onSubmit={save} aria-label={t('activities.rename', { title: item.title })} className="grid gap-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
          <label className="flex flex-col gap-1 text-sm font-medium">
            {t('activities.titleField')}
            <input className={field} value={title} required maxLength={150} onChange={(e) => setTitle(e.target.value)} />
          </label>
          <label className="flex flex-col gap-1 text-sm font-medium">
            {t('toolbox.subject')}
            <select className={field} value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
              <option value="">{t('activities.noSubject')}</option>
              {catalog.data?.subjects.map((s) => (
                <option key={s.id} value={s.id}>
                  {en ? s.nameEn : s.nameMk}
                </option>
              ))}
            </select>
          </label>
          <div className="flex gap-2">
            <Button type="submit" disabled={rename.isPending || !title.trim()}>
              {t('activities.save')}
            </Button>
            <Button type="button" variant="outline" onClick={() => setEditing(false)}>
              {t('activities.cancel')}
            </Button>
          </div>
        </form>
      ) : (
        <div className="flex flex-col gap-0.5">
          <h2 className="font-semibold">{item.title}</h2>
          <p className="text-sm text-muted-foreground">
            {[t(`activities.boards.${item.toolType}`), subject, t('activities.changed', { when: formatDateTime(item.updatedAtUtc, i18n.resolvedLanguage ?? 'mk', viewerTimeZone()) })]
              .filter(Boolean)
              .join(' · ')}
          </p>
        </div>
      )}
      {!editing && (
        <div className="flex flex-wrap gap-2">
          {canOpen ? (
            <Link
              to={`/app/w/${workspaceId}/toolbox/${item.toolType}?activity=${item.id}`}
              className="inline-flex h-9 items-center rounded-md bg-primary px-3 text-sm font-medium text-primary-foreground hover:bg-primary/90 focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
              {t('activities.open', { title: item.title })}
            </Link>
          ) : (
            <span className="inline-flex h-9 items-center text-sm text-muted-foreground">{t('activities.boardSoon')}</span>
          )}
          <Button type="button" size="sm" variant="outline" onClick={() => setEditing(true)}>
            {t('activities.renameButton')}
          </Button>
          <Button type="button" size="sm" variant="outline" disabled={duplicate.isPending} onClick={() => duplicate.mutate(item)}>
            {t('activities.duplicate')}
          </Button>
          {confirming ? (
            <>
              <Button type="button" size="sm" variant="destructive" disabled={remove.isPending} onClick={() => remove.mutate(item, { onSuccess: onDeleted })}>
                {t('activities.deleteConfirm')}
              </Button>
              <Button type="button" size="sm" variant="outline" onClick={() => setConfirming(false)}>
                {t('activities.cancel')}
              </Button>
            </>
          ) : (
            <Button type="button" size="sm" variant="outline" onClick={() => setConfirming(true)}>
              {t('activities.delete')}
            </Button>
          )}
        </div>
      )}
    </article>
  );
}
