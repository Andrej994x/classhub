import { useCallback, useEffect, useId, useState, type FormEvent } from 'react';
import { BOOKING_TEXT, type BookingKey, type BookingLanguage } from './bookingText.ts';

export interface BookingWidgetProps {
  slug?: string;
  language?: string;
}

interface Offer {
  id: string;
  title: string;
  durationMinutes: number;
  mode: string;
}

interface BookingPage {
  available: boolean;
  timeZone: string;
  horizonDays: number;
  offers: Offer[];
}

interface FormData {
  available: boolean;
  formToken: string | null;
  consentVersion: string;
  consentText: string;
  minorNotice: string;
  minimumFillSeconds?: number;
}

interface Slot {
  startUtc: string;
  time: string;
}

interface SlotDay {
  date: string;
  slots: Slot[];
}

const DAYS_PER_PAGE = 14;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const PHONE = /^[0-9+()\-./ ]{5,40}$/;
const input = 'w-full rounded-lg border border-site-muted/40 bg-site-surface px-3 py-2 text-base text-site-text focus:outline-none focus-visible:ring-2 focus-visible:ring-site-primary';
const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

/** `YYYY-MM-DD` of an instant in a time zone. */
function dayIn(timeZone: string, at = new Date()): string {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(at);
  const get = (type: string) => parts.find((p) => p.type === type)!.value;
  return `${get('year')}-${get('month')}-${get('day')}`;
}

function addDays(date: string, days: number): string {
  const d = new Date(`${date}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

async function getJson<T>(url: string): Promise<T> {
  const response = await fetch(url, { credentials: 'omit', cache: 'no-store' });
  if (!response.ok) throw new Error(String(response.status));
  return (await response.json()) as T;
}

/**
 * The public booking island (R2-P6) on `/{slug}/book`: choose an offer and a free time (all times in the teacher's time
 * zone, stated explicitly), then send the request through the same protected public form as the contact form (signed
 * token, minimum fill time, honeypot, consent, rate limits). The teacher confirms; pending requests do not hold a time.
 */
export function BookingWidget({ slug = '', language = 'mk' }: BookingWidgetProps) {
  const lang: BookingLanguage = language === 'en' ? 'en' : 'mk';
  const t = (key: BookingKey, values: Record<string, string> = {}) => Object.entries(values).reduce<string>((text, [k, v]) => text.replace(`{${k}}`, v), BOOKING_TEXT[lang][key]);
  const id = useId();
  const base = `/api/public/sites/${encodeURIComponent(slug)}`;
  const [page, setPage] = useState<BookingPage | null>(null);
  const [form, setForm] = useState<(FormData & { loadedAt: number }) | null>(null);
  const [failed, setFailed] = useState(false);
  const [offerId, setOfferId] = useState('');
  const [from, setFrom] = useState('');
  // Slots of one offer and period; a reload (a time was just taken) asks again.
  const [slots, setSlots] = useState<{ key: string; days: SlotDay[] } | null>(null);
  const [reload, setReload] = useState(0);
  const [slot, setSlot] = useState<(Slot & { date: string }) | null>(null);
  const [values, setValues] = useState({ name: '', email: '', phone: '', message: '', consent: false, ageConfirmed: false, website: '' });
  const [errors, setErrors] = useState<Partial<Record<'name' | 'email' | 'phone' | 'consent' | 'ageConfirmed', string>>>({});
  const [notice, setNotice] = useState<string | null>(null);
  const [state, setState] = useState<'idle' | 'sending' | 'sent'>('idle');

  const loadForm = useCallback(async () => {
    const data = await getJson<FormData>(`${base}/lead-form`);
    setForm({ ...data, loadedAt: Date.now() });
  }, [base]);

  useEffect(() => {
    let active = true;
    Promise.all([getJson<BookingPage>(`${base}/booking`), getJson<FormData>(`${base}/lead-form`)])
      .then(([booking, data]) => {
        if (!active) return;
        setPage(booking);
        setForm({ ...data, loadedAt: Date.now() });
        setFrom(dayIn(booking.timeZone));
        if (booking.offers.length === 1) setOfferId(booking.offers[0]!.id);
      })
      .catch(() => active && setFailed(true));
    return () => {
      active = false;
    };
  }, [base]);

  const slotsKey = `${offerId}|${from}|${reload}`;
  useEffect(() => {
    if (!offerId || !from) return;
    let active = true;
    getJson<{ days: SlotDay[] }>(`${base}/booking/slots?offerId=${encodeURIComponent(offerId)}&from=${from}`)
      .catch(() => ({ days: [] as SlotDay[] }))
      .then((result) => active && setSlots({ key: slotsKey, days: result.days }));
    return () => {
      active = false;
    };
  }, [base, offerId, from, slotsKey]);
  const days = slots?.key === slotsKey ? slots.days : null;

  if (failed) return <p className="text-sm text-site-muted">{t('loadFailed')}</p>;
  if (!page || !form) return <p className="text-sm text-site-muted">{t('loading')}</p>;
  if (!page.available || !form.available || page.offers.length === 0) {
    return (
      <div className="flex flex-col gap-2 text-sm">
        <p>{t('unavailable')}</p>
        <a className="text-site-primary underline" href={`/${slug}#contact`}>
          {t('toContact')}
        </a>
      </div>
    );
  }
  if (state === 'sent') return <output className="block rounded-lg bg-site-primary/10 p-4 text-site-text">{t('sent')}</output>;

  const browserZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
  const today = dayIn(page.timeZone);
  const lastDay = addDays(today, page.horizonDays - 1);
  const locale = lang === 'en' ? 'en-GB' : 'mk-MK';
  const dayLabel = (date: string) => new Date(`${date}T12:00:00Z`).toLocaleDateString(locale, { timeZone: 'UTC', weekday: 'long', day: 'numeric', month: 'long' });

  const validate = () => {
    const found: typeof errors = {};
    if (!values.name.trim()) found.name = t('required');
    if (!EMAIL.test(values.email.trim())) found.email = t('invalidEmail');
    if (values.phone.trim() && !PHONE.test(values.phone.trim())) found.phone = t('invalidPhone');
    if (!values.consent) found.consent = t('consentRequired');
    if (!values.ageConfirmed) found.ageConfirmed = t('ageRequired');
    return found;
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!slot || !form.formToken || state === 'sending') return;
    const found = validate();
    setErrors(found);
    setNotice(Object.keys(found).length > 0 ? t('checkFields') : null);
    if (Object.keys(found).length > 0) return;
    setState('sending');
    try {
      const remaining = form.loadedAt + ((form.minimumFillSeconds ?? 3) * 1000 + 250) - Date.now();
      if (remaining > 0) await wait(remaining);
      const response = await fetch(`${base}/leads`, {
        method: 'POST',
        credentials: 'omit',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          type: 'LessonRequest',
          name: values.name.trim(),
          email: values.email.trim(),
          phone: values.phone.trim() || null,
          message: values.message.trim() || null,
          offerId,
          subjectId: null,
          consent: values.consent,
          ageConfirmed: values.ageConfirmed,
          consentVersion: form.consentVersion,
          formToken: form.formToken,
          website: values.website,
          slotStartUtc: slot.startUtc,
        }),
      });
      if (response.status === 202) {
        setState('sent');
        return;
      }

      const problem = (await response.json().catch(() => ({}))) as { code?: string; errors?: Record<string, string[]> };
      setState('idle');
      if (problem.code === 'FORM_EXPIRED') {
        await loadForm();
        setNotice(t('expired'));
      } else if (response.status === 429) {
        setNotice(t('tooMany'));
      } else if (problem.errors && Object.keys(problem.errors).some((k) => k.toLowerCase() === 'slotstartutc')) {
        setSlot(null);
        setNotice(t('taken'));
        setReload((n) => n + 1);
      } else {
        setNotice(t('failed'));
      }
    } catch {
      setState('idle');
      setNotice(t('failed'));
    }
  };

  const field = (name: 'name' | 'email' | 'phone', label: string, type: string, autoComplete: string) => (
    <div className="flex flex-col gap-1">
      <label htmlFor={`${id}-${name}`} className="text-sm font-medium">
        {label}
      </label>
      <input
        id={`${id}-${name}`}
        type={type}
        autoComplete={autoComplete}
        className={input}
        value={values[name]}
        aria-invalid={errors[name] ? true : undefined}
        aria-describedby={errors[name] ? `${id}-${name}-error` : undefined}
        onChange={(e) => setValues({ ...values, [name]: e.target.value })}
      />
      {errors[name] && (
        <p id={`${id}-${name}-error`} className="text-sm text-red-700">
          {errors[name]}
        </p>
      )}
    </div>
  );

  return (
    <div className="flex flex-col gap-5">
      <p className="text-sm text-site-muted">
        {t('zone', { zone: page.timeZone })} {browserZone && browserZone !== page.timeZone && t('zoneDiffers', { zone: browserZone })}
      </p>
      {page.offers.length > 1 && (
        <div className="flex flex-col gap-1">
          <label htmlFor={`${id}-offer`} className="text-sm font-medium">
            {t('offer')}
          </label>
          <select
            id={`${id}-offer`}
            className={input}
            value={offerId}
            onChange={(e) => {
              setOfferId(e.target.value);
              setSlot(null);
            }}
          >
            <option value="">—</option>
            {page.offers.map((o) => (
              <option key={o.id} value={o.id}>
                {o.title} · {o.durationMinutes} {t('minutes')}
              </option>
            ))}
          </select>
        </div>
      )}

      {offerId && !slot && (
        <section aria-label={t('days')} className="flex flex-col gap-3">
          {days === null ? (
            <p className="text-sm text-site-muted">{t('loading')}</p>
          ) : days.length === 0 ? (
            <p className="text-sm text-site-muted">{t('noSlots')}</p>
          ) : (
            <ul className="flex flex-col gap-3">
              {days.map((day) => (
                <li key={day.date}>
                  <h3 className="mb-1 text-sm font-semibold">{dayLabel(day.date)}</h3>
                  <ul className="flex flex-wrap gap-2" aria-label={dayLabel(day.date)}>
                    {day.slots.map((s) => (
                      <li key={s.startUtc}>
                        <button
                          type="button"
                          className="rounded-full border border-site-primary px-3 py-1.5 text-sm text-site-primary hover:bg-site-primary hover:text-site-on-primary focus:outline-none focus-visible:ring-2 focus-visible:ring-site-primary"
                          onClick={() => {
                            setSlot({ ...s, date: day.date });
                            setNotice(null);
                          }}
                        >
                          {s.time}
                        </button>
                      </li>
                    ))}
                  </ul>
                </li>
              ))}
            </ul>
          )}
          <div className="flex gap-2">
            <button type="button" className="text-sm text-site-primary underline disabled:opacity-40" disabled={from <= today} onClick={() => setFrom(addDays(from, -DAYS_PER_PAGE))}>
              {t('earlier')}
            </button>
            <button type="button" className="text-sm text-site-primary underline disabled:opacity-40" disabled={addDays(from, DAYS_PER_PAGE) > lastDay} onClick={() => setFrom(addDays(from, DAYS_PER_PAGE))}>
              {t('later')}
            </button>
          </div>
        </section>
      )}

      {notice && (
        <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-800">
          {notice}
        </p>
      )}

      {slot && (
        <form noValidate className="flex flex-col gap-3" onSubmit={(e) => void submit(e)}>
          <p className="font-medium">
            {t('chosen', { when: `${dayLabel(slot.date)} ${slot.time} (${page.timeZone})` })}{' '}
            <button type="button" className="text-sm font-normal text-site-primary underline" onClick={() => setSlot(null)}>
              {t('change')}
            </button>
          </p>
          {field('name', t('name'), 'text', 'name')}
          {field('email', t('email'), 'email', 'email')}
          {field('phone', t('phone'), 'tel', 'tel')}
          <div className="flex flex-col gap-1">
            <label htmlFor={`${id}-message`} className="text-sm font-medium">
              {t('message')}
            </label>
            <textarea id={`${id}-message`} rows={3} maxLength={2000} className={input} value={values.message} onChange={(e) => setValues({ ...values, message: e.target.value })} />
          </div>
          {/* People never see this field; automated senders fill it in (D-19). */}
          <div aria-hidden="true" className="absolute -left-[9999px] h-px w-px overflow-hidden">
            <label htmlFor={`${id}-website`}>Website</label>
            <input id={`${id}-website`} tabIndex={-1} autoComplete="off" value={values.website} onChange={(e) => setValues({ ...values, website: e.target.value })} />
          </div>
          <label className="flex items-start gap-2 text-sm">
            <input type="checkbox" className="mt-1 h-4 w-4" checked={values.consent} aria-invalid={errors.consent ? true : undefined} onChange={(e) => setValues({ ...values, consent: e.target.checked })} />
            <span>{form.consentText}</span>
          </label>
          {errors.consent && <p className="text-sm text-red-700">{errors.consent}</p>}
          <label className="flex items-start gap-2 text-sm">
            <input type="checkbox" className="mt-1 h-4 w-4" checked={values.ageConfirmed} aria-invalid={errors.ageConfirmed ? true : undefined} onChange={(e) => setValues({ ...values, ageConfirmed: e.target.checked })} />
            <span>{form.minorNotice}</span>
          </label>
          {errors.ageConfirmed && <p className="text-sm text-red-700">{errors.ageConfirmed}</p>}
          <button
            type="submit"
            disabled={state === 'sending'}
            className="self-start rounded-full bg-site-primary px-5 py-2 font-semibold text-site-on-primary focus:outline-none focus-visible:ring-2 focus-visible:ring-site-text disabled:opacity-60"
          >
            {state === 'sending' ? t('sending') : t('submit')}
          </button>
        </form>
      )}
    </div>
  );
}
