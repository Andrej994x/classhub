import { zodResolver } from '@hookform/resolvers/zod';
import { useForm, useWatch } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router';
import { z } from 'zod';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { Label } from '../../../components/ui/label.tsx';
import { moneyInput, parseMoney } from '../../../lib/format.ts';
import { num, OFFER_KINDS, OFFER_MODES, useOffer, useSaveOffer, type Offer } from '../../../teacher/offers.ts';
import { useCatalog, useWorkspace } from '../../../teacher/queries.ts';
import { NotFoundContent } from '../Pages.tsx';

const CURRENCIES = ['MKD', 'EUR', 'USD', 'CHF', 'GBP', 'RSD', 'ALL', 'BGN', 'TRY'];
const inRange = (value: string, min: number, max: number) => /^\d+$/.test(value.trim()) && Number(value) >= min && Number(value) <= max;
const whole = (min: number, max: number) => z.string().trim().regex(/^\d+$/, 'offers.validation.number').refine((v) => Number(v) >= min && Number(v) <= max, 'offers.validation.range');

/** Mirrors the API rules (the API and the database stay authoritative). Messages are i18n keys. */
const schema = z
  .object({
    kind: z.enum(OFFER_KINDS),
    title: z.string().trim().min(1, 'offers.validation.titleRequired').max(120, 'offers.validation.tooLong'),
    description: z.string().trim().max(2000, 'offers.validation.tooLong'),
    subjectId: z.string().min(1, 'offers.validation.subjectRequired'),
    duration: whole(15, 600),
    price: z.string(),
    currency: z.string().length(3),
    mode: z.enum(OFFER_MODES),
    lessonCount: z.string(),
    seats: z.string(),
    isActive: z.boolean(),
    showOnPublicSite: z.boolean(),
    /** The version of the offer the form was loaded from (submitted with the save; never a refreshed one). */
    version: z.number().nullable(),
  })
  .superRefine((v, ctx) => {
    if (parseMoney(v.price, v.currency) === null) ctx.addIssue({ code: 'custom', path: ['price'], message: 'offers.validation.price' });
    if (v.kind === 'Package' && !inRange(v.lessonCount, 1, 200)) {
      ctx.addIssue({ code: 'custom', path: ['lessonCount'], message: 'offers.validation.lessons' });
    }
    if (v.kind === 'GroupProgram' && !inRange(v.seats, 1, 500)) {
      ctx.addIssue({ code: 'custom', path: ['seats'], message: 'offers.validation.seats' });
    }
  });
type OfferForm = z.infer<typeof schema>;

function toForm(offer: Offer | undefined, currency: string): OfferForm {
  if (!offer) {
    return { kind: 'SingleLesson', title: '', description: '', subjectId: '', duration: '60', price: '', currency, mode: 'Online', lessonCount: '', seats: '', isActive: true, showOnPublicSite: true, version: null };
  }
  return {
    kind: offer.kind as OfferForm['kind'],
    title: offer.title,
    description: offer.description ?? '',
    subjectId: offer.subjectId,
    duration: String(num(offer.durationMinutes)),
    price: moneyInput(num(offer.priceMinor) ?? 0, offer.currency),
    currency: offer.currency,
    mode: offer.mode as OfferForm['mode'],
    lessonCount: offer.lessonCount === null || offer.lessonCount === undefined ? '' : String(num(offer.lessonCount)),
    seats: offer.seats === null || offer.seats === undefined ? '' : String(num(offer.seats)),
    isActive: offer.isActive,
    showOnPublicSite: offer.showOnPublicSite,
    version: num(offer.version),
  };
}

/** Create (`/offers/new`) or edit (`/offers/:offerId`) an offer. */
export function OfferEditPage() {
  const { t } = useTranslation();
  const { workspaceId = '', offerId } = useParams();
  const offer = useOffer(workspaceId, offerId);
  const workspace = useWorkspace(workspaceId);
  const catalog = useCatalog();

  // Wait for the offer fetched now (not a cached copy): its version is the one the form will submit.
  const offerLoading = offerId !== undefined && (offer.isPending || (!offer.isFetchedAfterMount && !offer.isError));
  if (offerLoading || workspace.isPending || catalog.isPending) return <output className="block">{t('common.loading')}</output>;
  const failed = (offerId ? offer.error : null) ?? workspace.error ?? catalog.error;
  if (failed) return failed instanceof ApiError && failed.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(failed, t)}</Alert>;

  return (
    <OfferForm
      key={offerId ?? 'new'}
      workspaceId={workspaceId}
      offer={offerId ? offer.data : undefined}
      currency={workspace.data!.currency}
      catalog={catalog.data!}
    />
  );
}

function OfferForm({ workspaceId, offer, currency, catalog }: { workspaceId: string; offer: Offer | undefined; currency: string; catalog: NonNullable<ReturnType<typeof useCatalog>['data']> }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const language = i18n.resolvedLanguage === 'en' ? 'en' : 'mk';
  const save = useSaveOffer(workspaceId, offer?.id);
  const form = useForm<OfferForm>({ resolver: zodResolver(schema), defaultValues: toForm(offer, currency) });
  const { errors, isSubmitting } = form.formState;
  const kind = useWatch({ control: form.control, name: 'kind' });
  const message = (key: string | undefined) => (key ? t(key) : undefined);
  const currencies = CURRENCIES.includes(form.getValues('currency')) ? CURRENCIES : [form.getValues('currency'), ...CURRENCIES];
  const selectClass = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';

  const onSubmit = form.handleSubmit(async (v) => {
    await save
      .mutateAsync({
        kind: v.kind,
        title: v.title.trim(),
        description: v.description.trim() || null,
        subjectId: v.subjectId,
        durationMinutes: Number(v.duration),
        priceMinor: parseMoney(v.price, v.currency)!,
        currency: v.currency,
        mode: v.mode,
        lessonCount: v.kind === 'Package' ? Number(v.lessonCount) : null,
        seats: v.kind === 'GroupProgram' ? Number(v.seats) : null,
        isActive: v.isActive,
        showOnPublicSite: v.showOnPublicSite,
        // Tied to the values being submitted: a background refetch of the offer cannot make stale values pass.
        version: v.version,
      })
      .then(() => navigate(`/app/w/${workspaceId}/offers`))
      .catch(() => undefined);
  });

  return (
    <section className="flex flex-col gap-6">
      <h1 className="text-2xl font-semibold tracking-tight">{offer ? t('offers.editTitle') : t('offers.newTitle')}</h1>
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-6">
        <fieldset disabled={isSubmitting} aria-busy={isSubmitting} className="contents">
          <Card>
            <CardHeader>
              <CardTitle className="text-lg">{t('offers.sections.what')}</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              <fieldset className="flex flex-col gap-2">
                <legend className="mb-1 text-sm font-medium">{t('offers.fields.kind')}</legend>
                <div className="grid gap-2 sm:grid-cols-3">
                  {OFFER_KINDS.map((k) => (
                    <label key={k} className="flex min-h-11 items-center gap-2 rounded-md border border-input px-3 py-2 text-sm has-[:checked]:border-primary has-[:checked]:bg-muted">
                      <input type="radio" value={k} className="h-4 w-4" {...form.register('kind')} />
                      {t(`offers.kinds.${k}`)}
                    </label>
                  ))}
                </div>
              </fieldset>
              <FormField label={t('offers.fields.title')} error={message(errors.title?.message)} {...form.register('title')} />
              <div className="flex flex-col gap-2">
                <Label htmlFor="offer-description">{t('offers.fields.description')}</Label>
                <textarea id="offer-description" rows={4} maxLength={2000} className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm" {...form.register('description')} />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="offer-subject">{t('offers.fields.subject')}</Label>
                <select id="offer-subject" className={selectClass} aria-invalid={errors.subjectId ? true : undefined} {...form.register('subjectId')}>
                  <option value="">{t('profile.fields.choose')}</option>
                  {catalog.categories.map((category) => (
                    <optgroup key={category.id} label={language === 'en' ? category.nameEn : category.nameMk}>
                      {catalog.subjects
                        .filter((s) => s.categoryId === category.id)
                        .map((s) => (
                          <option key={s.id} value={s.id}>
                            {language === 'en' ? s.nameEn : s.nameMk}
                          </option>
                        ))}
                    </optgroup>
                  ))}
                </select>
                {errors.subjectId?.message && <p className="text-sm text-destructive">{t(errors.subjectId.message)}</p>}
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-lg">{t('offers.sections.terms')}</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-4 sm:grid-cols-2">
              <FormField label={t('offers.fields.duration')} inputMode="numeric" error={message(errors.duration?.message)} hint={t('offers.fields.durationHint')} {...form.register('duration')} />
              <div className="grid grid-cols-[1fr_auto] items-end gap-2">
                <FormField label={t('offers.fields.price')} inputMode="decimal" error={message(errors.price?.message)} hint={t('offers.fields.priceHint')} {...form.register('price')} />
                <div className="flex flex-col gap-2">
                  <Label htmlFor="offer-currency" className="sr-only">
                    {t('offers.fields.currency')}
                  </Label>
                  <select id="offer-currency" className={selectClass} {...form.register('currency')}>
                    {currencies.map((c) => (
                      <option key={c} value={c}>
                        {c}
                      </option>
                    ))}
                  </select>
                </div>
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="offer-mode">{t('offers.fields.mode')}</Label>
                <select id="offer-mode" className={selectClass} {...form.register('mode')}>
                  {OFFER_MODES.map((m) => (
                    <option key={m} value={m}>
                      {t(`profile.modes.${m}`)}
                    </option>
                  ))}
                </select>
              </div>
              {kind === 'Package' && (
                <FormField label={t('offers.fields.lessonCount')} inputMode="numeric" error={message(errors.lessonCount?.message)} {...form.register('lessonCount')} />
              )}
              {kind === 'GroupProgram' && <FormField label={t('offers.fields.seats')} inputMode="numeric" error={message(errors.seats?.message)} {...form.register('seats')} />}
            </CardContent>
          </Card>

          <Card>
            <CardContent className="flex flex-col gap-3 pt-6">
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" className="h-4 w-4" {...form.register('isActive')} />
                {t('offers.fields.isActive')}
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" className="h-4 w-4" {...form.register('showOnPublicSite')} />
                {t('offers.fields.showOnPublicSite')}
              </label>
            </CardContent>
          </Card>

          {save.error && (
            <Alert variant="error">
              {save.error instanceof ApiError && save.error.code === 'CONCURRENCY_CONFLICT' ? t('offers.changedElsewhere') : errorMessage(save.error, t)}
            </Alert>
          )}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? t('common.loading') : t('offers.save')}
            </Button>
            <Button type="button" variant="ghost" onClick={() => navigate(`/app/w/${workspaceId}/offers`)}>
              {t('offers.cancel')}
            </Button>
          </div>
        </fieldset>
      </form>
    </section>
  );
}
