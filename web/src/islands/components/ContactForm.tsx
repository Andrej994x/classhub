import { useCallback, useEffect, useId, useState, type FormEvent } from 'react';
import { CONTACT_FORM_TEXT, type ContactFormKey, type ContactFormLanguage } from './contactFormText.ts';

export interface ContactFormProps {
  slug?: string;
  language?: string;
}

interface FormData {
  available: boolean;
  formToken: string | null;
  offers: { id: string; name: string }[];
  subjects: { id: string; name: string }[];
  consentVersion: string;
  consentText: string;
  minorNotice: string;
}

type Values = {
  type: 'Message' | 'LessonRequest';
  name: string;
  email: string;
  phone: string;
  offerId: string;
  subjectId: string;
  message: string;
  consent: boolean;
  ageConfirmed: boolean;
  website: string;
};

const EMPTY: Values = { type: 'Message', name: '', email: '', phone: '', offerId: '', subjectId: '', message: '', consent: false, ageConfirmed: false, website: '' };
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const PHONE = /^[0-9+()\-./ ]{5,40}$/;
const FIELDS = ['name', 'email', 'phone', 'message', 'offerId', 'subjectId', 'consent', 'ageConfirmed'] as const;
type Field = (typeof FIELDS)[number];

async function fetchForm(slug: string): Promise<FormData> {
  const response = await fetch(`/api/public/sites/${encodeURIComponent(slug)}/lead-form`, { credentials: 'omit', cache: 'no-store' });
  if (!response.ok) throw new Error(String(response.status));
  return (await response.json()) as FormData;
}

const input = 'w-full rounded-lg border border-site-muted/40 bg-site-surface px-3 py-2 text-base text-site-text focus:outline-none focus-visible:ring-2 focus-visible:ring-site-primary';

/**
 * The public contact / lesson-request form (R1-P5). Loads its data from the API when mounted (never from the cached
 * page): availability (D-10b), a signed form token for the minimum fill time, offers, subjects and the versioned
 * consent text (D-18). Spam protection (D-19): a honeypot field people never see, the fill time, server rate limits.
 */
export function ContactForm({ slug = '', language = 'mk' }: ContactFormProps) {
  const lang: ContactFormLanguage = language === 'en' ? 'en' : 'mk';
  const t = (key: ContactFormKey) => CONTACT_FORM_TEXT[lang][key];
  const id = useId();
  const [form, setForm] = useState<FormData | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [values, setValues] = useState<Values>(EMPTY);
  const [errors, setErrors] = useState<Partial<Record<Field, string>>>({});
  const [state, setState] = useState<'idle' | 'sending' | 'sent'>('idle');
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(
    () =>
      fetchForm(slug).then(
        (data) => setForm(data),
        () => setLoadFailed(true),
      ),
    [slug],
  );

  useEffect(() => {
    let active = true;
    fetchForm(slug).then(
      (data) => active && setForm(data),
      () => active && setLoadFailed(true),
    );
    return () => {
      active = false;
    };
  }, [slug]);

  const set = <K extends keyof Values>(key: K, value: Values[K]) => setValues((current) => ({ ...current, [key]: value }));

  function validate(): Partial<Record<Field, string>> {
    const found: Partial<Record<Field, string>> = {};
    if (!values.name.trim()) found.name = t('required');
    if (!values.email.trim()) found.email = t('required');
    else if (!EMAIL.test(values.email.trim())) found.email = t('invalidEmail');
    if (values.phone.trim() && !PHONE.test(values.phone.trim())) found.phone = t('invalidPhone');
    if (values.type === 'Message' && !values.message.trim()) found.message = t('required');
    if (!values.consent) found.consent = t('consentRequired');
    if (!values.ageConfirmed) found.ageConfirmed = t('ageRequired');
    return found;
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!form?.formToken || state === 'sending') return;
    const found = validate();
    setErrors(found);
    setNotice(Object.keys(found).length > 0 ? t('checkFields') : null);
    if (Object.keys(found).length > 0) return;

    setState('sending');
    try {
      const response = await fetch(`/api/public/sites/${encodeURIComponent(slug)}/leads`, {
        method: 'POST',
        credentials: 'omit',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          type: values.type,
          name: values.name.trim(),
          email: values.email.trim(),
          phone: values.phone.trim() || null,
          message: values.message.trim() || null,
          offerId: values.type === 'LessonRequest' && values.offerId ? values.offerId : null,
          subjectId: values.subjectId || null,
          consent: values.consent,
          ageConfirmed: values.ageConfirmed,
          consentVersion: form.consentVersion,
          formToken: form.formToken,
          website: values.website,
        }),
      });
      if (response.status === 202) {
        setState('sent');
        setValues(EMPTY);
        return;
      }

      const problem = (await response.json().catch(() => ({}))) as { code?: string; errors?: Record<string, string[]> };
      setState('idle');
      if (problem.code === 'FORM_EXPIRED') {
        await load();
        setNotice(t('expired'));
      } else if (response.status === 429) {
        setNotice(t('tooMany'));
      } else if (problem.errors) {
        const serverErrors: Partial<Record<Field, string>> = {};
        for (const [key, messages] of Object.entries(problem.errors)) {
          const field = FIELDS.find((f) => f.toLowerCase() === key.toLowerCase());
          if (field) serverErrors[field] = messages[0];
        }
        setErrors(serverErrors);
        setNotice(t('checkFields'));
      } else {
        setNotice(t('failed'));
      }
    } catch {
      setState('idle');
      setNotice(t('failed'));
    }
  }

  if (loadFailed) return <p className="text-sm text-site-muted">{t('unavailable')}</p>;
  if (!form) return <p className="text-sm text-site-muted">{t('loading')}</p>;
  if (!form.available) return null; // D-10b: the form is hidden

  if (state === 'sent') {
    return (
      <div className="flex flex-col items-start gap-3">
        <output className="font-semibold">{t('sent')}</output>
        <button type="button" className="text-sm text-site-primary underline underline-offset-4" onClick={() => void load().then(() => setState('idle'))}>
          {t('sendAnother')}
        </button>
      </div>
    );
  }

  const field = (name: Field) => ({
    id: `${id}-${name}`,
    'aria-invalid': errors[name] ? true : undefined,
    'aria-describedby': errors[name] ? `${id}-${name}-error` : undefined,
  });
  const error = (name: Field) =>
    errors[name] ? (
      <p id={`${id}-${name}-error`} className="text-sm font-medium text-red-700">
        {errors[name]}
      </p>
    ) : null;
  const label = 'flex flex-col gap-1 text-sm font-medium';

  return (
    <form noValidate onSubmit={(event) => void submit(event)} className="mt-4 flex flex-col gap-4" aria-label={t('title')}>
      <fieldset className="flex flex-wrap gap-4">
        <legend className="mb-2 text-sm font-medium">{t('typeLabel')}</legend>
        {(['Message', 'LessonRequest'] as const).map((type) => (
          <label key={type} className="flex items-center gap-2 text-sm">
            <input type="radio" name={`${id}-type`} value={type} checked={values.type === type} onChange={() => set('type', type)} className="size-4 accent-site-primary" />
            {type === 'Message' ? t('typeMessage') : t('typeLesson')}
          </label>
        ))}
      </fieldset>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className={label}>
          <label htmlFor={`${id}-name`}>{t('name')}</label>
          <input {...field('name')} className={input} autoComplete="name" maxLength={100} value={values.name} onChange={(e) => set('name', e.target.value)} />
          {error('name')}
        </div>
        <div className={label}>
          <label htmlFor={`${id}-email`}>{t('email')}</label>
          <input {...field('email')} className={input} type="email" autoComplete="email" maxLength={254} value={values.email} onChange={(e) => set('email', e.target.value)} />
          {error('email')}
        </div>
        <div className={label}>
          <label htmlFor={`${id}-phone`}>{t('phone')}</label>
          <input {...field('phone')} className={input} type="tel" autoComplete="tel" maxLength={40} value={values.phone} onChange={(e) => set('phone', e.target.value)} />
          {error('phone')}
        </div>
        {form.subjects.length > 0 && (
          <div className={label}>
            <label htmlFor={`${id}-subjectId`}>{t('subject')}</label>
            <select {...field('subjectId')} className={input} value={values.subjectId} onChange={(e) => set('subjectId', e.target.value)}>
              <option value="">{t('none')}</option>
              {form.subjects.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
            {error('subjectId')}
          </div>
        )}
        {values.type === 'LessonRequest' && form.offers.length > 0 && (
          <div className={label}>
            <label htmlFor={`${id}-offerId`}>{t('offer')}</label>
            <select {...field('offerId')} className={input} value={values.offerId} onChange={(e) => set('offerId', e.target.value)}>
              <option value="">{t('none')}</option>
              {form.offers.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.name}
                </option>
              ))}
            </select>
            {error('offerId')}
          </div>
        )}
      </div>

      <div className={label}>
        <label htmlFor={`${id}-message`}>{values.type === 'Message' ? t('message') : t('messageOptional')}</label>
        <textarea {...field('message')} className={input} rows={4} maxLength={2000} value={values.message} onChange={(e) => set('message', e.target.value)} />
        {error('message')}
      </div>

      {/* Honeypot (D-19): hidden from people and assistive technology; bots that fill it are dropped. */}
      <div aria-hidden="true" className="absolute -left-[10000px] top-auto h-px w-px overflow-hidden">
        <label>
          Website
          <input tabIndex={-1} autoComplete="off" name="website" value={values.website} onChange={(e) => set('website', e.target.value)} />
        </label>
      </div>

      <fieldset className="flex flex-col gap-3">
        <legend className="mb-1 text-sm font-medium">{t('consentTitle')}</legend>
        <label className="flex items-start gap-2 text-sm" htmlFor={`${id}-consent`}>
          <input {...field('consent')} type="checkbox" className="mt-0.5 size-4 shrink-0 accent-site-primary" checked={values.consent} onChange={(e) => set('consent', e.target.checked)} />
          <span>{form.consentText}</span>
        </label>
        {error('consent')}
        <label className="flex items-start gap-2 text-sm" htmlFor={`${id}-ageConfirmed`}>
          <input {...field('ageConfirmed')} type="checkbox" className="mt-0.5 size-4 shrink-0 accent-site-primary" checked={values.ageConfirmed} onChange={(e) => set('ageConfirmed', e.target.checked)} />
          <span>{form.minorNotice}</span>
        </label>
        {error('ageConfirmed')}
      </fieldset>

      {notice && (
        <p role="alert" className="text-sm font-medium text-red-700">
          {notice}
        </p>
      )}
      <p>
        <button
          type="submit"
          disabled={state === 'sending'}
          className="rounded-full bg-site-primary px-6 py-2 font-semibold text-site-on-primary disabled:opacity-60 focus:outline-none focus-visible:ring-2 focus-visible:ring-site-text"
        >
          {state === 'sending' ? t('sending') : t('submit')}
        </button>
      </p>
    </form>
  );
}
