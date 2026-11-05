import { zodResolver } from '@hookform/resolvers/zod';
import { Camera, Trash2 } from 'lucide-react';
import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import { useForm, useWatch, type UseFormRegisterReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { z } from 'zod';
import { ApiError, errorMessage } from '../../../api/errors.ts';
import { Alert } from '../../../components/ui/alert.tsx';
import { Button } from '../../../components/ui/button.tsx';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../../components/ui/card.tsx';
import { FormField } from '../../../components/ui/form-field.tsx';
import { Label } from '../../../components/ui/label.tsx';
import {
  PHOTO_MAX_BYTES,
  PHOTO_TYPES,
  photoUrl,
  useCatalog,
  usePhoto,
  useProfile,
  useSaveProfile,
  useWorkspace,
  type Catalog,
  type TeacherProfile,
  type Workspace,
} from '../../../teacher/queries.ts';
import { NotFoundContent } from '../Pages.tsx';

const MODES = ['Online', 'InPerson', 'Hybrid'] as const;
/** Currencies offered in the form (the API accepts every active ISO 4217 code). */
const CURRENCIES = ['MKD', 'EUR', 'USD', 'CHF', 'GBP', 'RSD', 'ALL', 'BGN', 'TRY'];

const schema = z.object({
  firstName: z.string().trim().max(100, 'profile.validation.tooLong'),
  lastName: z.string().trim().max(100, 'profile.validation.tooLong'),
  bio: z.string().trim().max(1000, 'profile.validation.bioTooLong'),
  teachingMode: z.enum(['', ...MODES]),
  countryCode: z.string(),
  subjectIds: z.array(z.string()).max(20, 'profile.validation.tooManySubjects'),
  levelIds: z.array(z.string()).max(10, 'profile.validation.tooManyLevels'),
  languages: z.array(z.string()).max(10, 'profile.validation.tooManyLanguages'),
  timeZone: z.string().min(1),
  currency: z.string().length(3),
  /** The profile version this form was loaded from (optimistic concurrency). */
  version: z.number().nullable(),
});
type ProfileForm = z.infer<typeof schema>;

function toForm(profile: TeacherProfile, workspace: Workspace): ProfileForm {
  return {
    firstName: profile.firstName ?? '',
    lastName: profile.lastName ?? '',
    bio: profile.bio ?? '',
    teachingMode: (profile.teachingMode as ProfileForm['teachingMode'] | null) ?? '',
    countryCode: profile.countryCode ?? '',
    subjectIds: [...profile.subjectIds],
    levelIds: [...profile.levelIds],
    languages: [...profile.languages],
    timeZone: workspace.timeZone,
    currency: workspace.currency,
    version: profile.version === null || profile.version === undefined ? null : Number(profile.version),
  };
}

const empty = (value: string) => (value.trim() === '' ? null : value.trim());

export function ProfilePage() {
  const { t } = useTranslation();
  const { workspaceId = '' } = useParams();
  const profile = useProfile(workspaceId);
  const workspace = useWorkspace(workspaceId);
  const catalog = useCatalog();

  if (profile.isPending || workspace.isPending || catalog.isPending) return <output className="block">{t('common.loading')}</output>;
  const failed = profile.error ?? workspace.error ?? catalog.error;
  if (failed) return failed instanceof ApiError && failed.status === 404 ? <NotFoundContent /> : <Alert variant="error">{errorMessage(failed, t)}</Alert>;

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t('profile.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('profile.lead')}</p>
      </div>
      <PhotoCard workspaceId={workspaceId} profile={profile.data!} />
      <ProfileFormCard workspaceId={workspaceId} profile={profile.data!} workspace={workspace.data!} catalog={catalog.data!} />
    </section>
  );
}

function PhotoCard({ workspaceId, profile }: { workspaceId: string; profile: TeacherProfile }) {
  const { t } = useTranslation();
  const input = useRef<HTMLInputElement>(null);
  const inputId = useId();
  const [clientError, setClientError] = useState<string | null>(null);
  const { upload, remove } = usePhoto(workspaceId);
  const error = clientError ?? (upload.error ? errorMessage(upload.error, t) : remove.error ? errorMessage(remove.error, t) : null);

  const onFile = (file: File | undefined) => {
    setClientError(null);
    upload.reset();
    if (!file) return;
    if (!PHOTO_TYPES.includes(file.type)) return setClientError(t('profile.photo.wrongType'));
    if (file.size > PHOTO_MAX_BYTES) return setClientError(t('profile.photo.tooLarge'));
    upload.mutate(file);
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">{t('profile.photo.title')}</CardTitle>
        <CardDescription>{t('profile.photo.hint')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4 sm:flex-row sm:items-center">
        <div className="flex h-32 w-32 shrink-0 items-center justify-center overflow-hidden rounded-full bg-muted">
          {profile.hasPhoto ? (
            <img src={photoUrl(workspaceId, profile.photoVersion)} alt={t('profile.photo.alt')} className="h-full w-full object-cover" />
          ) : (
            <Camera className="h-10 w-10 text-muted-foreground" aria-hidden />
          )}
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor={inputId} className="sr-only">
            {t('profile.photo.choose')}
          </Label>
          <input
            ref={input}
            id={inputId}
            type="file"
            accept={PHOTO_TYPES.join(',')}
            className="sr-only"
            onChange={(event) => {
              onFile(event.target.files?.[0]);
              event.target.value = '';
            }}
          />
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" disabled={upload.isPending} onClick={() => input.current?.click()}>
              {upload.isPending ? t('common.loading') : profile.hasPhoto ? t('profile.photo.change') : t('profile.photo.choose')}
            </Button>
            {profile.hasPhoto && (
              <Button type="button" variant="ghost" disabled={remove.isPending} onClick={() => remove.mutate()}>
                <Trash2 className="h-4 w-4" aria-hidden />
                {t('profile.photo.remove')}
              </Button>
            )}
          </div>
          {error && <Alert variant="error">{error}</Alert>}
          {upload.isSuccess && <Alert variant="success">{t('profile.photo.saved')}</Alert>}
        </div>
      </CardContent>
    </Card>
  );
}

function Choices({ legend, children, id }: { legend: string; children: ReactNode; id?: string }) {
  return (
    <fieldset id={id} className="flex flex-col gap-2">
      <legend className="mb-1 text-sm font-medium">{legend}</legend>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">{children}</div>
    </fieldset>
  );
}

function Choice({ label, type = 'checkbox', registration, value }: { label: string; type?: 'checkbox' | 'radio'; registration: UseFormRegisterReturn; value: string }) {
  return (
    <label className="flex min-h-11 items-center gap-2 rounded-md border border-input px-3 py-2 text-sm has-[:checked]:border-primary has-[:checked]:bg-muted">
      <input type={type} value={value} className="h-4 w-4" {...registration} />
      {label}
    </label>
  );
}

function ProfileFormCard({ workspaceId, profile, workspace, catalog }: { workspaceId: string; profile: TeacherProfile; workspace: Workspace; catalog: Catalog }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage === 'en' ? 'en' : 'mk';
  const name = (item: { nameMk: string; nameEn: string }) => (language === 'en' ? item.nameEn : item.nameMk);
  const save = useSaveProfile(workspaceId);
  const form = useForm<ProfileForm>({ resolver: zodResolver(schema), defaultValues: toForm(profile, workspace) });
  const { errors, isSubmitting, isDirty } = form.formState;
  const bioLength = useWatch({ control: form.control, name: 'bio' }).length;

  // Re-sync after the photo (or anything else) refreshed the server state, without losing unsaved edits.
  useEffect(() => {
    if (!form.formState.isDirty) form.reset(toForm(profile, workspace));
  }, [profile, workspace, form]);

  const countries = useMemo(() => {
    const names = new Intl.DisplayNames([language], { type: 'region' });
    return catalog.countries.map((code) => ({ code, name: names.of(code) ?? code })).toSorted((a, b) => a.name.localeCompare(b.name, language));
  }, [catalog.countries, language]);
  const timeZones = useMemo(() => {
    const zones = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : [];
    return zones.includes(workspace.timeZone) ? zones : [workspace.timeZone, ...zones];
  }, [workspace.timeZone]);
  const currencies = CURRENCIES.includes(workspace.currency) ? CURRENCIES : [workspace.currency, ...CURRENCIES];

  const onSubmit = form.handleSubmit(async (values) => {
    // Each workspace setting is sent only when the teacher changed it in this form (compared with the form's own
    // loaded values, not with a refreshed workspace), so an untouched, possibly stale value can never undo a change
    // made elsewhere. The profile version does not cover workspace settings.
    const timeZoneChanged = form.getFieldState('timeZone').isDirty;
    const currencyChanged = form.getFieldState('currency').isDirty;
    await save
      .mutateAsync({
        firstName: empty(values.firstName),
        lastName: empty(values.lastName),
        bio: empty(values.bio),
        teachingMode: values.teachingMode === '' ? null : values.teachingMode,
        countryCode: empty(values.countryCode),
        // Entries the ClassHub team deactivated are not offered any more (R1-P11): they are dropped on the next save.
        subjectIds: values.subjectIds.filter((id) => catalog.subjects.some((subject) => subject.id === id)),
        levelIds: values.levelIds.filter((id) => catalog.levels.some((level) => level.id === id)),
        languages: values.languages,
        timeZone: timeZoneChanged ? values.timeZone : null,
        currency: currencyChanged ? values.currency : null,
        version: values.version,
      })
      .then((saved) => form.reset(toForm(saved, { ...workspace, timeZone: values.timeZone, currency: values.currency })))
      .catch(() => undefined);
  });

  const selectClass = 'h-11 w-full rounded-md border border-input bg-background px-3 text-base sm:h-10 sm:text-sm';
  return (
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-6">
      {/* While saving, nothing can be edited: the success reset must not overwrite edits made in between. */}
      <fieldset disabled={isSubmitting} aria-busy={isSubmitting} className="contents">
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('profile.personal')}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField label={t('profile.fields.firstName')} autoComplete="given-name" error={errors.firstName?.message && t(errors.firstName.message)} {...form.register('firstName')} />
            <FormField label={t('profile.fields.lastName')} autoComplete="family-name" error={errors.lastName?.message && t(errors.lastName.message)} {...form.register('lastName')} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="profile-bio">{t('profile.fields.bio')}</Label>
            <textarea
              id="profile-bio"
              rows={5}
              maxLength={1000}
              aria-describedby="profile-bio-count"
              aria-invalid={errors.bio ? true : undefined}
              className="w-full rounded-md border border-input bg-background px-3 py-2 text-base sm:text-sm"
              {...form.register('bio')}
            />
            <p id="profile-bio-count" className="text-xs text-muted-foreground">
              {t('profile.fields.bioCount', { count: bioLength, max: 1000 })}
            </p>
          </div>
        </CardContent>
      </Card>

      <Card id="teaching">
        <CardHeader>
          <CardTitle className="text-lg">{t('profile.teaching')}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-6">
          <Choices legend={t('profile.fields.mode')}>
            {MODES.map((mode) => (
              <Choice key={mode} type="radio" value={mode} label={t(`profile.modes.${mode}`)} registration={form.register('teachingMode')} />
            ))}
          </Choices>
          {catalog.categories.map((category) => {
            const subjects = catalog.subjects.filter((s) => s.categoryId === category.id);
            return subjects.length === 0 ? null : (
              <Choices key={category.id} legend={`${t('profile.fields.subjects')}: ${name(category)}`}>
                {subjects.map((subject) => (
                  <Choice key={subject.id} value={subject.id} label={name(subject)} registration={form.register('subjectIds')} />
                ))}
              </Choices>
            );
          })}
          {errors.subjectIds?.message && <Alert variant="error">{t(errors.subjectIds.message)}</Alert>}
          <Choices legend={t('profile.fields.levels')}>
            {catalog.levels.map((level) => (
              <Choice key={level.id} value={level.id} label={name(level)} registration={form.register('levelIds')} />
            ))}
          </Choices>
          {errors.levelIds?.message && <Alert variant="error">{t(errors.levelIds.message)}</Alert>}
          <Choices legend={t('profile.fields.languages')}>
            {catalog.languages.map((lang) => (
              <Choice key={lang.code} value={lang.code} label={name(lang)} registration={form.register('languages')} />
            ))}
          </Choices>
          {errors.languages?.message && <Alert variant="error">{t(errors.languages.message)}</Alert>}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('profile.location')}</CardTitle>
          <CardDescription>{t('profile.locationHint')}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-3">
          <div className="flex flex-col gap-2">
            <Label htmlFor="profile-country">{t('profile.fields.country')}</Label>
            <select id="profile-country" className={selectClass} {...form.register('countryCode')}>
              <option value="">{t('profile.fields.choose')}</option>
              {countries.map((c) => (
                <option key={c.code} value={c.code}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="profile-timezone">{t('profile.fields.timeZone')}</Label>
            <select id="profile-timezone" className={selectClass} {...form.register('timeZone')}>
              {timeZones.map((zone) => (
                <option key={zone} value={zone}>
                  {zone}
                </option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="profile-currency">{t('profile.fields.currency')}</Label>
            <select id="profile-currency" className={selectClass} {...form.register('currency')}>
              {currencies.map((code) => (
                <option key={code} value={code}>
                  {code}
                </option>
              ))}
            </select>
          </div>
        </CardContent>
      </Card>

      {save.error && (
        <Alert variant="error">
          {save.error instanceof ApiError && save.error.code === 'CONCURRENCY_CONFLICT' ? t('profile.changedElsewhere') : errorMessage(save.error, t)}
        </Alert>
      )}
      {save.isSuccess && !isDirty && <Alert variant="success">{t('profile.saved')}</Alert>}
      <div className="sticky bottom-0 -mx-4 border-t border-border bg-background/95 px-4 py-3 backdrop-blur md:static md:mx-0 md:border-0 md:bg-transparent md:p-0">
        <Button type="submit" className="w-full sm:w-auto" disabled={isSubmitting}>
          {isSubmitting ? t('common.loading') : t('profile.save')}
        </Button>
      </div>
      </fieldset>
    </form>
  );
}
