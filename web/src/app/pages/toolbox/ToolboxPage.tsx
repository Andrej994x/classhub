import { ExternalLink, Star } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { useCatalog } from '../../../teacher/queries.ts';
import { useToolbox, type Tool, type ToolboxFilter } from '../../../teacher/toolbox.ts';
import { countToolOpened, useWorkspaceUsage } from '../../../teacher/usage.ts';

const field = 'h-10 w-full rounded-md border border-input bg-background px-3 text-sm';

/**
 * `/app/w/:workspaceId/toolbox` (R3-P1): the teaching tools catalog. Search by name or description, filter by subject
 * and category; external tools open on their official sites in a new tab, built-in boards inside ClassHub.
 */
export function ToolboxPage() {
  const { t, i18n } = useTranslation();
  const { workspaceId = '' } = useParams();
  const [filter, setFilter] = useState<ToolboxFilter>({ q: '', subjectId: '', category: '' });
  const toolbox = useToolbox(workspaceId, filter);
  const catalog = useCatalog();
  const en = i18n.resolvedLanguage === 'en';
  const set = (change: Partial<ToolboxFilter>) => setFilter((current) => ({ ...current, ...change }));

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('toolbox.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('toolbox.lead')}</p>
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
        <label className="flex flex-col gap-1 text-sm font-medium">
          {t('toolbox.search')}
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
          {t('toolbox.category')}
          <select className={field} value={filter.category} onChange={(e) => set({ category: e.target.value })}>
            <option value="">{t('toolbox.allCategories')}</option>
            {toolbox.data?.categories.map((c) => (
              <option key={c} value={c}>
                {t(`toolbox.categories.${c}`)}
              </option>
            ))}
          </select>
        </label>
      </div>

      {toolbox.isError ? (
        <Alert variant="error">{errorMessage(toolbox.error, t)}</Alert>
      ) : !toolbox.data ? (
        <output className="block">{t('common.loading')}</output>
      ) : toolbox.data.tools.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('toolbox.none')}</p>
      ) : (
        <ul aria-label={t('toolbox.tools')} className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {toolbox.data.tools.map((tool) => (
            <li key={tool.id}>
              <ToolCard workspaceId={workspaceId} tool={tool} en={en} />
            </li>
          ))}
        </ul>
      )}
      <p className="text-xs text-muted-foreground">{t('toolbox.note')}</p>
      <UsageCard workspaceId={workspaceId} tools={toolbox.data?.tools ?? []} en={en} />
    </section>
  );
}

function ToolCard({ workspaceId, tool, en }: { workspaceId: string; tool: Tool; en: boolean }) {
  const { t } = useTranslation();
  const name = en ? tool.nameEn : tool.nameMk;
  const open = 'inline-flex h-9 items-center gap-1.5 self-start rounded-md bg-primary px-3 text-sm font-medium text-primary-foreground hover:bg-primary/90 focus:outline-none focus-visible:ring-2 focus-visible:ring-ring';
  return (
    <article aria-label={name} className="flex h-full flex-col gap-2 rounded-lg border border-border bg-card p-4">
      <div className="flex items-start justify-between gap-2">
        <h2 className="font-semibold">{name}</h2>
        {tool.isFeatured && (
          <span className="inline-flex shrink-0 items-center gap-1 rounded-full bg-muted px-2 py-0.5 text-xs">
            <Star className="h-3 w-3" aria-hidden />
            {t('toolbox.featured')}
          </span>
        )}
      </div>
      <p className="text-xs text-muted-foreground">
        {t(`toolbox.categories.${tool.category}`)} · {t(`toolbox.kinds.${tool.kind}`)}
      </p>
      <p className="flex-1 text-sm">{en ? tool.descriptionEn : tool.descriptionMk}</p>
      {(en ? tool.licenseNoteEn : tool.licenseNoteMk) && <p className="text-xs text-muted-foreground">{en ? tool.licenseNoteEn : tool.licenseNoteMk}</p>}
      {tool.kind === 'External' && tool.url ? (
        <a href={tool.url} target="_blank" rel="noopener noreferrer" className={open} onClick={() => countToolOpened(workspaceId, tool.code)}>
          {t('toolbox.open', { name })}
          <ExternalLink className="h-4 w-4" aria-hidden />
          <span className="sr-only">{t('toolbox.newTab')}</span>
        </a>
      ) : (
        <Link to={`/app/w/${workspaceId}/toolbox/${tool.builtInKey}`} className={open} onClick={() => countToolOpened(workspaceId, tool.code)}>
          {t('toolbox.open', { name })}
        </Link>
      )}
    </article>
  );
}

/** The workspace's own use of the toolbox in the last 30 days (R3-P6): counts only, nobody is recorded. */
function UsageCard({ workspaceId, tools, en }: { workspaceId: string; tools: Tool[]; en: boolean }) {
  const { t } = useTranslation();
  const usage = useWorkspaceUsage(workspaceId);
  if (!usage.data) return null;
  const name = (code: string) => {
    const tool = tools.find((x) => x.code === code);
    return tool ? (en ? tool.nameEn : tool.nameMk) : code;
  };
  const empty = usage.data.tools.length === 0 && usage.data.boards.length === 0;
  return (
    <section aria-labelledby="toolbox-usage" className="flex flex-col gap-2 rounded-lg border border-border p-4">
      <h2 id="toolbox-usage" className="font-semibold">
        {t('toolbox.usage.title')}
      </h2>
      {empty ? (
        <p className="text-sm text-muted-foreground">{t('toolbox.usage.none')}</p>
      ) : (
        <dl className="grid gap-x-6 gap-y-1 text-sm sm:grid-cols-2">
          {usage.data.tools.slice(0, 6).map((tool) => (
            <div key={tool.code} className="flex justify-between gap-3">
              <dt>{name(tool.code)}</dt>
              <dd className="tabular-nums text-muted-foreground">{t('toolbox.usage.opens', { count: Number(tool.opens) })}</dd>
            </div>
          ))}
          {usage.data.boards.map((board) => (
            <div key={board.board} className="flex justify-between gap-3">
              <dt>{t(`activities.boards.${board.board}`)}</dt>
              <dd className="tabular-nums text-muted-foreground">{t('toolbox.usage.activities', { created: Number(board.created), reused: Number(board.reused) })}</dd>
            </div>
          ))}
        </dl>
      )}
      <p className="text-xs text-muted-foreground">{t('toolbox.usage.privacy')}</p>
    </section>
  );
}
