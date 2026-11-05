import { zodResolver } from '@hookform/resolvers/zod';
import { ArrowDown, ArrowUp, Eye, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useFieldArray, useForm, useWatch, type Control, type UseFormRegister } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { z } from 'zod';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { ITEM_SECTIONS, SITE_PALETTES, SITE_TEMPLATES, SLUG_PATTERN, usePreview, useSaveSite, useSite, useSlugAvailability, type Site } from '../../../teacher/site.ts';
import { NotFoundContent } from '../Pages.tsx';

const text = (max: number) => z.string().max(max, 'site.validation.tooLong');
const itemSchema = z.object({ title: text(200), subtitle: text(80), text: text(1500) });
const sectionSchema = z.object({
  type: z.string(),
  visible: z.boolean(),
  title: text(120),
  text: text(3000),
  showPhoto: z.boolean(),
  items: z.array(itemSchema).max(30, 'site.validation.tooManyItems'),
});
const schema = z.object({
  slug: z.string().trim().min(3, 'site.validation.slugLength').max(40, 'site.validation.slugLength').regex(SLUG_PATTERN, 'site.validation.slugFormat'),
  template: z.enum(SITE_TEMPLATES),
  palette: z.string(),
  language: z.enum(['mk', 'en']),
  sections: z.array(sectionSchema),
  /** The version the form was loaded from (submitted with the save). */
  version: z.number().nullable(),
});
type SiteForm = z.infer<typeof schema>;

const toForm = (site: Site): SiteForm => ({
  slug: site.slug,
  template: site.template as SiteForm['template'],
  palette: site.palette,
  language: site.language === 'en' ? 'en' : 'mk',
  sections: (site.draft.sections ?? []).map((s) => ({
    type: s.type ?? '',
    visible: s.visible,
    title: s.title ?? '',
    text: s.text ?? '',
    showPhoto: s.showPhoto ?? false,
    items: (s.items ?? []).map((i) => ({ title: i.title ?? '', subtitle: i.subtitle ?? '', text: i.text ?? '' })),
  })),
  version: site.version === null || site.version === undefined ? null : Number(site.version),
});

const orNull = (value: string) => (value.trim() === '' ? null : value.trim());

export function SiteEditorPage() {
  const { t } = useTranslation();
  const { workspaceId = '' } = useParams();
  const site = useSite(workspaceId);

  if (site.isPending || (!site.isFetchedAfterMount && !site.isError)) return <output className="block">{t('common.loading')}</output>;
  if (site.isError) return site.error instanceof ApiError && site.error.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(site.error, t)}</Alert>;
  return <SiteEditor workspaceId={workspaceId} site={site.data} />;
}

function SiteEditor({ workspaceId, site }: { workspaceId: string; site: Site }) {
  const { t } = useTranslation();
  const save = useSaveSite(workspaceId);
  const preview = usePreview(workspaceId);
  const form = useForm<SiteForm>({ resolver: zodResolver(schema), defaultValues: toForm(site) });
  const { errors, isSubmitting, isDirty } = form.formState;
  const sections = useFieldArray({ control: form.control, name: 'sections' });
  const exists = site.exists;

  const [invalidSubmit, setInvalidSubmit] = useState(false);
  const onSubmit = form.handleSubmit(async (v) => {
    setInvalidSubmit(false);
    await save
      .mutateAsync({
        slug: v.slug.trim(),
        template: v.template,
        palette: v.palette,
        language: v.language,
        draft: {
          sections: v.sections.map((s) => ({
            type: s.type,
            visible: s.visible,
            title: orNull(s.title),
            text: orNull(s.text),
            showPhoto: s.showPhoto,
            items: s.items.map((i) => ({ title: orNull(i.title), subtitle: orNull(i.subtitle), text: orNull(i.text) })),
          })),
        },
        version: v.version,
      })
      // The saved draft (with its new version) becomes the form's baseline.
      .then((saved) => form.reset(toForm(saved)))
      .catch(() => undefined);
  }, () => setInvalidSubmit(true)); // never a silent no-op: say why nothing was saved

  const saveError =
    save.error instanceof ApiError && save.error.code === 'CONCURRENCY_CONFLICT'
      ? t('site.changedElsewhere')
      : save.error
        ? errorMessage(save.error, t)
        : null;

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('site.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('site.lead')}</p>
      </div>
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-6">
        <fieldset disabled={isSubmitting} aria-busy={isSubmitting} className="contents">
          <SettingsCard workspaceId={workspaceId} savedSlug={exists ? site.slug : null} form={form} />

          <div className="flex flex-col gap-3">
            <h2 className="text-lg font-semibold">{t('site.sectionsTitle')}</h2>
            <ol aria-label={t('site.sectionsTitle')} className="flex flex-col gap-3">
              {sections.fields.map((field, index) => (
                <li key={field.id}>
                  <SectionCard
                    index={index}
                    type={field.type}
                    control={form.control}
                    register={form.register}
                    first={index === 0}
                    last={index === sections.fields.length - 1}
                    onMove={(delta) => sections.move(index, index + delta)}
                    errors={errors.sections?.[index]}
                  />
                </li>
              ))}
            </ol>
          </div>

          {invalidSubmit && Object.keys(errors).length > 0 && <Alert variant="error">{t('site.validation.checkFields')}</Alert>}
          {saveError && <Alert variant="error">{saveError}</Alert>}
          {save.isSuccess && !isDirty && <Alert variant="success">{t('site.saved')}</Alert>}
          <div className="sticky bottom-0 -mx-4 flex flex-wrap gap-2 border-t border-border bg-background/95 px-4 py-3 backdrop-blur md:static md:mx-0 md:border-0 md:bg-transparent md:p-0">
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? t('common.loading') : t('site.save')}
            </Button>
            <Button type="button" variant="outline" disabled={!exists || isDirty || preview.isPending} onClick={() => preview.mutate()}>
              <Eye className="h-4 w-4" aria-hidden />
              {t('site.preview')}
            </Button>
            {(!exists || isDirty) && <p className="self-center text-sm text-muted-foreground">{t('site.previewNeedsSave')}</p>}
          </div>
        </fieldset>
      </form>

      {preview.error && <Alert variant="error">{errorMessage(preview.error, t)}</Alert>}
      {preview.data && !isDirty && (
        <Card>
          <CardHeader className="flex flex-row items-center justify-between gap-2">
            <CardTitle className="text-lg">{t('site.previewTitle')}</CardTitle>
            <a className="text-sm text-primary underline-offset-4 hover:underline" href={preview.data.url} target="_blank" rel="noopener noreferrer">
              {t('site.openPreview')}
            </a>
          </CardHeader>
          <CardContent>
            {/* No scripts run in the preview: sandboxed, same origin only (it shows the teacher's private photo). */}
            <iframe title={t('site.previewTitle')} src={preview.data.url} sandbox="allow-same-origin" className="h-[70vh] w-full rounded-md border border-border bg-white" />
          </CardContent>
        </Card>
      )}
    </section>
  );
}

function SettingsCard({ workspaceId, savedSlug, form }: { workspaceId: string; savedSlug: string | null; form: ReturnType<typeof useForm<SiteForm>> }) {
  const { t } = useTranslation();
  const { errors } = form.formState;
  const slug = useWatch({ control: form.control, name: 'slug' });
  const palette = useWatch({ control: form.control, name: 'palette' });
  const [checked, setChecked] = useState(slug);
  useEffect(() => {
    const timer = setTimeout(() => setChecked(slug.trim()), 400);
    return () => clearTimeout(timer);
  }, [slug]);
  const shapeOk = SLUG_PATTERN.test(checked) && checked.length >= 3 && checked.length <= 40;
  const availability = useSlugAvailability(workspaceId, checked, shapeOk && checked !== savedSlug);
  const availabilityText =
    checked === savedSlug || !shapeOk || !availability.data
      ? null
      : availability.data.available
        ? t('site.slug.available')
        : t(`site.slug.${availability.data.reason ?? 'taken'}`);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('site.settings')}</CardTitle>
        <CardDescription>{t('site.settingsHint')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        <div className="flex flex-col gap-2">
          <FormField
            label={t('site.fields.slug')}
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            hint={t('site.fields.slugHint')}
            error={errors.slug?.message && t(errors.slug.message)}
            {...form.register('slug')}
          />
          <p className="text-sm text-muted-foreground" aria-live="polite">
            classhub.app/<strong>{slug || '…'}</strong>
            {availabilityText && <span data-testid="slug-availability"> · {availabilityText}</span>}
          </p>
        </div>

        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-sm font-medium">{t('site.fields.template')}</legend>
          <div className="grid gap-2 sm:grid-cols-3">
            {SITE_TEMPLATES.map((template) => (
              <label key={template} className="flex min-h-11 flex-col gap-1 rounded-md border border-input px-3 py-2 text-sm has-[:checked]:border-primary has-[:checked]:bg-muted">
                <span className="flex items-center gap-2">
                  <input type="radio" value={template} className="h-4 w-4" {...form.register('template')} />
                  <span className="font-medium">{t(`site.templates.${template}`)}</span>
                </span>
                <span className="text-xs text-muted-foreground">{t(`site.templates.${template}Hint`)}</span>
              </label>
            ))}
          </div>
        </fieldset>

        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-sm font-medium">{t('site.fields.palette')}</legend>
          <div className="flex flex-wrap gap-2">
            {SITE_PALETTES.map((p) => (
              <label key={p.key} className="flex min-h-11 items-center gap-2 rounded-md border border-input px-3 py-2 text-sm has-[:checked]:border-primary has-[:checked]:bg-muted">
                <input type="radio" value={p.key} className="h-4 w-4" {...form.register('palette')} />
                <span aria-hidden className="h-5 w-5 rounded-full border border-border" style={{ background: `linear-gradient(135deg, ${p.primary} 50%, ${p.background} 50%)` }} />
                {t(`site.palettes.${p.key}`)}
              </label>
            ))}
          </div>
          <p className="sr-only">{t(`site.palettes.${palette}`)}</p>
        </fieldset>

        <div className="flex max-w-xs flex-col gap-2">
          <Label htmlFor="site-language">{t('site.fields.language')}</Label>
          <select id="site-language" className="h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm" {...form.register('language')}>
            <option value="mk">{t('language.mk')}</option>
            <option value="en">{t('language.en')}</option>
          </select>
          <p className="text-xs text-muted-foreground">{t('site.fields.languageHint')}</p>
        </div>
      </CardContent>
    </Card>
  );
}

interface SectionCardProps {
  index: number;
  type: string;
  control: Control<SiteForm>;
  register: UseFormRegister<SiteForm>;
  first: boolean;
  last: boolean;
  onMove: (delta: -1 | 1) => void;
  errors: NonNullable<ReturnType<typeof useForm<SiteForm>>['formState']['errors']['sections']>[number] | undefined;
}

function SectionCard({ index, type, control, register, first, last, onMove, errors }: SectionCardProps) {
  const { t } = useTranslation();
  const name = t(`site.sectionTypes.${type}`);
  const hasItems = (ITEM_SECTIONS as readonly string[]).includes(type);
  const items = useFieldArray({ control, name: `sections.${index}.items` });
  const visible = useWatch({ control, name: `sections.${index}.visible` });
  const showsText = type !== 'experience' && type !== 'testimonials' && type !== 'faq';
  const message = (key: string | undefined) => (key ? t(key) : undefined);

  return (
    <Card data-section={type} className={visible ? undefined : 'opacity-75'}>
      <CardHeader className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <CardTitle className="text-base">{name}</CardTitle>
        <div className="flex flex-wrap items-center gap-2">
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" className="h-4 w-4" aria-label={t('site.showSection', { name })} {...register(`sections.${index}.visible`)} />
            {t('site.visible')}
          </label>
          <Button type="button" variant="ghost" size="sm" disabled={first} aria-label={t('site.moveUp', { name })} onClick={() => onMove(-1)}>
            <ArrowUp className="h-4 w-4" aria-hidden />
          </Button>
          <Button type="button" variant="ghost" size="sm" disabled={last} aria-label={t('site.moveDown', { name })} onClick={() => onMove(1)}>
            <ArrowDown className="h-4 w-4" aria-hidden />
          </Button>
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <FormField label={t(type === 'hero' ? 'site.fields.headline' : 'site.fields.sectionTitle', { name })} maxLength={120} error={message(errors?.title?.message)} {...register(`sections.${index}.title`)} />
        {showsText && (
          <div className="flex flex-col gap-2">
            <Label htmlFor={`section-${index}-text`}>{t(type === 'hero' ? 'site.fields.message' : 'site.fields.text', { name })}</Label>
            <textarea id={`section-${index}-text`} rows={type === 'about' ? 6 : 3} maxLength={3000} aria-invalid={errors?.text ? true : undefined} className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm" {...register(`sections.${index}.text`)} />
            {errors?.text?.message && <p className="text-sm text-destructive">{t(errors.text.message)}</p>}
            {(type === 'subjects' || type === 'offers') && <p className="text-xs text-muted-foreground">{t(`site.fields.${type}Hint`)}</p>}
          </div>
        )}
        {type === 'hero' && (
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" className="h-4 w-4" {...register(`sections.${index}.showPhoto`)} />
            {t('site.fields.showPhoto')}
          </label>
        )}
        {hasItems && (
          <div className="flex flex-col gap-3">
            {items.fields.map((item, itemIndex) => (
              <fieldset key={item.id} className="flex flex-col gap-3 rounded-md border border-border p-3">
                <legend className="px-1 text-sm font-medium">{t(`site.items.${type}.label`, { number: itemIndex + 1 })}</legend>
                <FormField label={t(`site.items.${type}.title`)} maxLength={200} error={message(errors?.items?.[itemIndex]?.title?.message)} {...register(`sections.${index}.items.${itemIndex}.title`)} />
                {type === 'experience' && (
                  <FormField label={t('site.items.experience.subtitle')} maxLength={80} error={message(errors?.items?.[itemIndex]?.subtitle?.message)} {...register(`sections.${index}.items.${itemIndex}.subtitle`)} />
                )}
                <div className="flex flex-col gap-2">
                  <Label htmlFor={`section-${index}-item-${itemIndex}`}>{t(`site.items.${type}.text`)}</Label>
                  <textarea id={`section-${index}-item-${itemIndex}`} rows={3} maxLength={1500} aria-invalid={errors?.items?.[itemIndex]?.text ? true : undefined} className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm" {...register(`sections.${index}.items.${itemIndex}.text`)} />
                  {errors?.items?.[itemIndex]?.text?.message && <p className="text-sm text-destructive">{t(errors.items[itemIndex].text.message)}</p>}
                </div>
                <Button type="button" variant="ghost" size="sm" className="self-start" onClick={() => items.remove(itemIndex)}>
                  <Trash2 className="h-4 w-4" aria-hidden />
                  {t('site.removeItem')}
                </Button>
              </fieldset>
            ))}
            {errors?.items?.message && <Alert variant="error">{t(errors.items.message)}</Alert>}
            <Button type="button" variant="outline" size="sm" className="self-start" disabled={items.fields.length >= 30} onClick={() => items.append({ title: '', subtitle: '', text: '' })}>
              <Plus className="h-4 w-4" aria-hidden />
              {t(`site.items.${type}.add`)}
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
