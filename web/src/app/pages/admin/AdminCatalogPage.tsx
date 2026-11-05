import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '../../../api/errors.ts';
import { useAdminCatalog, useSaveCatalogEntry, type CatalogEntry, type CatalogKind } from '../../../admin/moderation.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { num } from '../../../teacher/offers.ts';

const field = 'h-10 w-full rounded-md border border-input bg-background px-2 text-sm';
const KINDS: CatalogKind[] = ['categories', 'subjects', 'levels'];

/** Reference data for platform staff (R1-P11): add, rename, reorder, (de)activate. Never delete. */
export function AdminCatalogPage() {
  const { t } = useTranslation();
  const catalog = useAdminCatalog();

  if (catalog.isError) return <Alert variant="error">{errorMessage(catalog.error, t)}</Alert>;
  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('admin.catalog.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('admin.catalog.lead')}</p>
      </div>
      {!catalog.data ? (
        <output className="block">{t('common.loading')}</output>
      ) : (
        KINDS.map((kind) => (
          <Card key={kind}>
            <CardHeader>
              <CardTitle className="text-base">{t(`admin.catalog.${kind}`)}</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-col gap-2">
              <ul aria-label={t(`admin.catalog.${kind}`)} className="flex flex-col gap-2">
                {catalog.data[kind].map((entry) => (
                  <li key={`${entry.id}-${entry.isActive}-${entry.nameMk}-${entry.nameEn}-${entry.sortOrder}`}>
                    <EntryRow kind={kind} entry={entry} categories={catalog.data.categories} />
                  </li>
                ))}
              </ul>
              <EntryRow kind={kind} categories={catalog.data.categories} />
            </CardContent>
          </Card>
        ))
      )}
    </section>
  );
}

/** One editable entry, or (without `entry`) the row that adds a new one. */
function EntryRow({ kind, entry, categories }: { kind: CatalogKind; entry?: CatalogEntry; categories: CatalogEntry[] }) {
  const { t } = useTranslation();
  const save = useSaveCatalogEntry(kind);
  const [code, setCode] = useState('');
  const [nameMk, setNameMk] = useState(entry?.nameMk ?? '');
  const [nameEn, setNameEn] = useState(entry?.nameEn ?? '');
  const [sortOrder, setSortOrder] = useState(String(num(entry?.sortOrder) ?? 0));
  const [categoryId, setCategoryId] = useState(entry?.categoryId ?? categories[0]?.id ?? '');
  const prefix = `${kind}-${entry?.id ?? 'new'}`;
  const valid = nameMk.trim() && nameEn.trim() && /^\d{1,5}$/.test(sortOrder) && (entry || /^[a-z0-9]+(-[a-z0-9]+)*$/.test(code));

  const submit = (isActive: boolean) => (event?: FormEvent) => {
    event?.preventDefault();
    if (!valid) return;
    save.mutate(
      {
        id: entry?.id,
        input: { code: entry ? null : code, nameMk: nameMk.trim(), nameEn: nameEn.trim(), sortOrder: Number(sortOrder), isActive, categoryId: kind === 'subjects' ? categoryId : null },
      },
      { onSuccess: () => !entry && (setCode(''), setNameMk(''), setNameEn('')) },
    );
  };

  return (
    <form
      noValidate
      aria-label={entry ? entry.code : t('admin.catalog.add')}
      onSubmit={submit(entry?.isActive ?? true)}
      className={`grid gap-2 rounded-md border border-border p-2 sm:grid-cols-[8rem_1fr_1fr_5rem_auto] sm:items-end ${entry && !entry.isActive ? 'opacity-70' : ''}`}
    >
      <div className="flex flex-col gap-1">
        <label htmlFor={`${prefix}-code`} className="text-xs text-muted-foreground">
          {t('admin.catalog.code')}
        </label>
        {entry ? (
          <code id={`${prefix}-code`} className="py-2 text-sm">
            {entry.code}
          </code>
        ) : (
          <input id={`${prefix}-code`} className={field} maxLength={40} title={t('admin.catalog.codeHint')} value={code} onChange={(e) => setCode(e.target.value)} />
        )}
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor={`${prefix}-mk`} className="text-xs text-muted-foreground">
          {t('admin.catalog.nameMk')}
        </label>
        <input id={`${prefix}-mk`} className={field} maxLength={100} value={nameMk} onChange={(e) => setNameMk(e.target.value)} />
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor={`${prefix}-en`} className="text-xs text-muted-foreground">
          {t('admin.catalog.nameEn')}
        </label>
        <input id={`${prefix}-en`} className={field} maxLength={100} value={nameEn} onChange={(e) => setNameEn(e.target.value)} />
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor={`${prefix}-order`} className="text-xs text-muted-foreground">
          {t('admin.catalog.sortOrder')}
        </label>
        <input id={`${prefix}-order`} inputMode="numeric" className={field} value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} />
      </div>
      <div className="flex flex-wrap items-center gap-2">
        {kind === 'subjects' && (
          <select aria-label={t('admin.catalog.category')} className={field} value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.nameMk}
              </option>
            ))}
          </select>
        )}
        <Button type="submit" size="sm" disabled={save.isPending || !valid}>
          {entry ? t('admin.catalog.save') : t('admin.catalog.add')}
        </Button>
        {entry && (
          <Button type="button" size="sm" variant="outline" disabled={save.isPending || !valid} onClick={() => submit(!entry.isActive)()}>
            {entry.isActive ? t('admin.catalog.deactivate') : t('admin.catalog.activate')}
          </Button>
        )}
        {entry && !entry.isActive && <span className="text-xs text-muted-foreground">{t('admin.catalog.inactive')}</span>}
      </div>
      {save.error && (
        <Alert variant="error" className="sm:col-span-5">
          {errorMessage(save.error, t)}
        </Alert>
      )}
    </form>
  );
}
