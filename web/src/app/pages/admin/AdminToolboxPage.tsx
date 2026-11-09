import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { num } from '../../../teacher/offers.ts';
import { useCatalog } from '../../../teacher/queries.ts';
import { useAdminTools, useSaveTool, type AdminTool } from '../../../teacher/toolbox.ts';

const field = 'h-10 w-full rounded-md border border-input bg-background px-2 text-sm';
const CATEGORIES = ['Geometry', 'Graphing', 'Simulations', 'Chemistry', 'Drawing', 'Other'] as const;
const BOARDS = ['drawing-board', 'geometry-board'] as const;

/** `/admin/toolbox` (R3-P1): platform staff add tools, change them, feature and (de)activate them. Never delete. */
export function AdminToolboxPage() {
  const { t } = useTranslation();
  const tools = useAdminTools();
  const [editing, setEditing] = useState<string | null>(null);

  if (tools.isError) return <Alert variant="error">{errorMessage(tools.error, t)}</Alert>;
  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('admin.toolbox.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('admin.toolbox.lead')}</p>
      </div>
      {!tools.data ? (
        <output className="block">{t('common.loading')}</output>
      ) : (
        <ul aria-label={t('admin.toolbox.title')} className="flex flex-col gap-2">
          {tools.data.map((entry) => (
            <li key={entry.tool.id} className="rounded-lg border border-border bg-card p-3">
              {editing === entry.tool.id ? (
                <ToolForm entry={entry} onDone={() => setEditing(null)} />
              ) : (
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="flex flex-col">
                    <span className="font-medium">
                      {entry.tool.nameMk} / {entry.tool.nameEn}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {entry.tool.code} · {t(`toolbox.categories.${entry.tool.category}`)} · {t(`toolbox.kinds.${entry.tool.kind}`)}
                      {entry.tool.isFeatured && ` · ${t('toolbox.featured')}`} · {entry.isActive ? t('admin.toolbox.active') : t('admin.toolbox.inactive')}
                    </span>
                  </div>
                  <Button type="button" variant="outline" size="sm" onClick={() => setEditing(entry.tool.id)}>
                    {t('admin.toolbox.edit', { name: entry.tool.nameEn })}
                  </Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t('admin.toolbox.add')}</CardTitle>
        </CardHeader>
        <CardContent>
          <ToolForm key={editing ?? 'new'} />
        </CardContent>
      </Card>
    </section>
  );
}

/** Edits a tool (with `entry`) or adds one. */
function ToolForm({ entry, onDone }: { entry?: AdminTool; onDone?: () => void }) {
  const { t, i18n } = useTranslation();
  const save = useSaveTool();
  const catalog = useCatalog();
  const tool = entry?.tool;
  const [code, setCode] = useState('');
  const [nameMk, setNameMk] = useState(tool?.nameMk ?? '');
  const [nameEn, setNameEn] = useState(tool?.nameEn ?? '');
  const [descriptionMk, setDescriptionMk] = useState(tool?.descriptionMk ?? '');
  const [descriptionEn, setDescriptionEn] = useState(tool?.descriptionEn ?? '');
  const [kind, setKind] = useState(tool?.kind ?? 'External');
  const [category, setCategory] = useState(tool?.category ?? 'Other');
  const [url, setUrl] = useState(tool?.url ?? 'https://');
  const [builtInKey, setBuiltInKey] = useState(tool?.builtInKey ?? BOARDS[0]);
  const [subjectIds, setSubjectIds] = useState<string[]>(tool?.subjectIds ?? []);
  const [licenseNoteMk, setLicenseNoteMk] = useState(tool?.licenseNoteMk ?? '');
  const [licenseNoteEn, setLicenseNoteEn] = useState(tool?.licenseNoteEn ?? '');
  const [sortOrder, setSortOrder] = useState(String(num(entry?.sortOrder) ?? 100));
  const [isFeatured, setFeatured] = useState(tool?.isFeatured ?? false);
  const [isActive, setActive] = useState(entry?.isActive ?? true);
  const [saved, setSaved] = useState(false);
  // The version the form's values came from: a refetch while editing must not make stale values look current.
  const [version] = useState(entry ? entry.version : null);
  const id = tool?.id ?? 'new';

  const submit = (event: FormEvent) => {
    event.preventDefault();
    setSaved(false);
    save.mutate(
      {
        id: tool?.id,
        input: {
          code: tool ? null : code,
          nameMk,
          nameEn,
          descriptionMk,
          descriptionEn,
          kind,
          category,
          url: kind === 'External' ? url : null,
          builtInKey: kind === 'BuiltIn' ? builtInKey : null,
          subjectIds,
          licenseNoteMk: licenseNoteMk || null,
          licenseNoteEn: licenseNoteEn || null,
          isActive,
          isFeatured,
          sortOrder: Number(sortOrder),
          version,
        },
      },
      {
        onSuccess: () => {
          setSaved(true);
          onDone?.();
        },
      },
    );
  };

  const input = (label: string, value: string, onChange: (v: string) => void, extra: Record<string, unknown> = {}) => (
    <label className="flex flex-col gap-1 text-sm font-medium">
      {label}
      <input className={field} value={value} onChange={(e) => onChange(e.target.value)} {...extra} />
    </label>
  );

  return (
    <form onSubmit={submit} aria-label={tool ? t('admin.toolbox.edit', { name: tool.nameEn }) : t('admin.toolbox.add')} className="flex flex-col gap-3">
      {save.isError && <Alert variant="error">{errorMessage(save.error, t)}</Alert>}
      {saved && !tool && <Alert variant="success">{t('admin.toolbox.saved')}</Alert>}
      <div className="grid gap-3 sm:grid-cols-2">
        {!tool && input(t('admin.toolbox.code'), code, setCode, { id: `${id}-code`, required: true, pattern: '[a-z0-9]+(-[a-z0-9]+)*' })}
        {input(t('admin.toolbox.nameMk'), nameMk, setNameMk, { required: true, maxLength: 100 })}
        {input(t('admin.toolbox.nameEn'), nameEn, setNameEn, { required: true, maxLength: 100 })}
        {input(t('admin.toolbox.descriptionMk'), descriptionMk, setDescriptionMk, { required: true, maxLength: 600 })}
        {input(t('admin.toolbox.descriptionEn'), descriptionEn, setDescriptionEn, { required: true, maxLength: 600 })}
        <label className="flex flex-col gap-1 text-sm font-medium">
          {t('admin.toolbox.kind')}
          <select className={field} value={kind} onChange={(e) => setKind(e.target.value)}>
            <option value="External">{t('toolbox.kinds.External')}</option>
            <option value="BuiltIn">{t('toolbox.kinds.BuiltIn')}</option>
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm font-medium">
          {t('toolbox.category')}
          <select className={field} value={category} onChange={(e) => setCategory(e.target.value)}>
            {CATEGORIES.map((c) => (
              <option key={c} value={c}>
                {t(`toolbox.categories.${c}`)}
              </option>
            ))}
          </select>
        </label>
        {kind === 'External' ? (
          input(t('admin.toolbox.url'), url, setUrl, { type: 'url', required: true, maxLength: 400 })
        ) : (
          <label className="flex flex-col gap-1 text-sm font-medium">
            {t('admin.toolbox.board')}
            <select className={field} value={builtInKey} onChange={(e) => setBuiltInKey(e.target.value)}>
              {BOARDS.map((b) => (
                <option key={b} value={b}>
                  {b}
                </option>
              ))}
            </select>
          </label>
        )}
        {input(t('admin.toolbox.licenseMk'), licenseNoteMk, setLicenseNoteMk, { maxLength: 300 })}
        {input(t('admin.toolbox.licenseEn'), licenseNoteEn, setLicenseNoteEn, { maxLength: 300 })}
        {input(t('admin.toolbox.sortOrder'), sortOrder, setSortOrder, { type: 'number', min: 0, max: 10000 })}
      </div>
      <fieldset className="flex flex-col gap-1">
        <legend className="text-sm font-medium">{t('toolbox.subject')}</legend>
        <div className="flex flex-wrap gap-x-4 gap-y-1">
          {catalog.data?.subjects.map((s) => (
            <label key={s.id} className="inline-flex items-center gap-1.5 text-sm">
              <input
                type="checkbox"
                checked={subjectIds.includes(s.id)}
                onChange={(e) => setSubjectIds((current) => (e.target.checked ? [...current, s.id] : current.filter((x) => x !== s.id)))}
              />
              {i18n.resolvedLanguage === 'en' ? s.nameEn : s.nameMk}
            </label>
          ))}
        </div>
      </fieldset>
      <div className="flex flex-wrap gap-4">
        <label className="inline-flex items-center gap-1.5 text-sm">
          <input type="checkbox" checked={isFeatured} onChange={(e) => setFeatured(e.target.checked)} />
          {t('toolbox.featured')}
        </label>
        <label className="inline-flex items-center gap-1.5 text-sm">
          <input type="checkbox" checked={isActive} onChange={(e) => setActive(e.target.checked)} />
          {t('admin.toolbox.active')}
        </label>
      </div>
      <div className="flex gap-2">
        <Button type="submit" disabled={save.isPending}>
          {tool ? t('admin.toolbox.save') : t('admin.toolbox.create')}
        </Button>
        {onDone && (
          <Button type="button" variant="outline" onClick={onDone}>
            {t('admin.toolbox.cancel')}
          </Button>
        )}
      </div>
    </form>
  );
}
