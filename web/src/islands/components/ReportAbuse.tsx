import { useId, useState, type FormEvent } from 'react';
import { REPORT_REASONS, REPORT_TEXT, type ReportLanguage, type ReportReason } from './reportAbuseText.ts';

export interface ReportAbuseProps {
  slug: string;
  language?: string;
  /** Visible sections of the page: `type` for the API, `label` as the visitor sees it. */
  sections?: { type: string; label: string }[];
  /** On a test page: the test can be reported too. */
  testSlug?: string;
}

type Target = 'site' | 'section' | 'test';

const input = 'w-full rounded-lg border border-site-muted/40 bg-site-surface px-3 py-2 text-base text-site-text focus:outline-none focus-visible:ring-2 focus-visible:ring-site-primary';
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * "Report a problem" on public pages (R1-P11): a small form sent to the ClassHub team, never to the teacher. Anonymous
 * (no cookies), rate-limited by the API, with a honeypot for bots.
 */
export function ReportAbuse({ slug, language, sections = [], testSlug }: ReportAbuseProps) {
  const t = REPORT_TEXT[(language === 'en' ? 'en' : 'mk') as ReportLanguage];
  const id = useId();
  const [open, setOpen] = useState(false);
  const [target, setTarget] = useState<Target>(testSlug ? 'test' : 'site');
  const [sectionType, setSectionType] = useState(sections[0]?.type ?? '');
  const [reason, setReason] = useState<ReportReason | ''>('');
  const [details, setDetails] = useState('');
  const [email, setEmail] = useState('');
  const [website, setWebsite] = useState('');
  const [errors, setErrors] = useState<Partial<Record<'reason' | 'details' | 'email', string>>>({});
  const [state, setState] = useState<'idle' | 'sending' | 'sent'>('idle');
  const [notice, setNotice] = useState<string | null>(null);

  if (state === 'sent') {
    return (
      <output className="block text-sm text-site-muted">{t.sent}</output>
    );
  }

  if (!open) {
    return (
      <button type="button" className="text-sm text-site-muted underline-offset-4 hover:underline" onClick={() => setOpen(true)}>
        {t.open}
      </button>
    );
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const found: typeof errors = {};
    if (!reason) found.reason = t.required;
    if (reason === 'other' && !details.trim()) found.details = t.required;
    if (email.trim() && !EMAIL.test(email.trim())) found.email = t.invalidEmail;
    setErrors(found);
    if (Object.keys(found).length > 0) return;

    setState('sending');
    setNotice(null);
    try {
      const response = await fetch(`/api/public/sites/${encodeURIComponent(slug)}/reports`, {
        method: 'POST',
        credentials: 'omit',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          target,
          sectionType: target === 'section' ? sectionType : null,
          testSlug: target === 'test' ? testSlug : null,
          reason,
          details: details.trim() || null,
          email: email.trim() || null,
          website,
        }),
      });
      if (response.status === 202) {
        setState('sent');
        return;
      }

      setState('idle');
      setNotice(response.status === 429 ? t.tooMany : response.status === 404 ? t.unavailable : t.failed);
    } catch {
      setState('idle');
      setNotice(t.failed);
    }
  };

  const fieldError = (name: keyof typeof errors) =>
    errors[name] ? (
      <p id={`${id}-${name}-error`} className="text-sm text-red-700">
        {errors[name]}
      </p>
    ) : null;

  return (
    <form noValidate aria-labelledby={`${id}-title`} onSubmit={(e) => void submit(e)} className="relative flex w-full max-w-lg flex-col gap-3 rounded-xl border border-site-muted/30 bg-site-surface p-4 text-left text-site-text">
      <h2 id={`${id}-title`} className="text-base font-semibold">
        {t.title}
      </h2>
      <p className="text-sm text-site-muted">{t.lead}</p>

      <fieldset className="flex flex-col gap-1">
        <legend className="text-sm font-medium">{t.what}</legend>
        {(['site', ...(sections.length > 0 ? ['section'] : []), ...(testSlug ? ['test'] : [])] as Target[]).map((value) => (
          <label key={value} className="flex items-center gap-2 text-sm">
            <input type="radio" name={`${id}-target`} value={value} checked={target === value} onChange={() => setTarget(value)} />
            {value === 'site' ? t.targetSite : value === 'section' ? t.targetSection : t.targetTest}
          </label>
        ))}
      </fieldset>

      {target === 'section' && (
        <div className="flex flex-col gap-1">
          <label htmlFor={`${id}-section`} className="text-sm font-medium">
            {t.section}
          </label>
          <select id={`${id}-section`} className={input} value={sectionType} onChange={(e) => setSectionType(e.target.value)}>
            {sections.map((s) => (
              <option key={s.type} value={s.type}>
                {s.label}
              </option>
            ))}
          </select>
        </div>
      )}

      <div className="flex flex-col gap-1">
        <label htmlFor={`${id}-reason`} className="text-sm font-medium">
          {t.reason}
        </label>
        <select
          id={`${id}-reason`}
          className={input}
          value={reason}
          aria-invalid={errors.reason ? true : undefined}
          aria-describedby={errors.reason ? `${id}-reason-error` : undefined}
          onChange={(e) => setReason(e.target.value as ReportReason)}
        >
          <option value="">—</option>
          {REPORT_REASONS.map((r) => (
            <option key={r} value={r}>
              {t.reasons[r]}
            </option>
          ))}
        </select>
        {fieldError('reason')}
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor={`${id}-details`} className="text-sm font-medium">
          {reason === 'other' ? t.detailsRequired : t.details}
        </label>
        <textarea
          id={`${id}-details`}
          rows={3}
          maxLength={1000}
          className={input}
          value={details}
          aria-invalid={errors.details ? true : undefined}
          aria-describedby={errors.details ? `${id}-details-error` : undefined}
          onChange={(e) => setDetails(e.target.value)}
        />
        {fieldError('details')}
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor={`${id}-email`} className="text-sm font-medium">
          {t.email}
        </label>
        <input
          id={`${id}-email`}
          type="email"
          autoComplete="email"
          maxLength={254}
          className={input}
          value={email}
          aria-invalid={errors.email ? true : undefined}
          aria-describedby={errors.email ? `${id}-email-error` : undefined}
          onChange={(e) => setEmail(e.target.value)}
        />
        {fieldError('email')}
      </div>

      {/* Honeypot (D-19): hidden from people and assistive technology; reports from bots that fill it are dropped. */}
      <div aria-hidden="true" className="absolute -left-[10000px] top-auto h-px w-px overflow-hidden">
        <label>
          Website
          <input tabIndex={-1} autoComplete="off" name="website" value={website} onChange={(e) => setWebsite(e.target.value)} />
        </label>
      </div>

      {notice && (
        <p role="alert" className="text-sm text-red-700">
          {notice}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <button
          type="submit"
          disabled={state === 'sending'}
          className="rounded-full bg-site-primary px-4 py-2 text-sm font-semibold text-site-on-primary disabled:opacity-60 focus:outline-none focus-visible:ring-2 focus-visible:ring-site-primary focus-visible:ring-offset-2"
        >
          {state === 'sending' ? t.sending : t.submit}
        </button>
        <button type="button" className="rounded-full px-4 py-2 text-sm text-site-muted hover:underline" onClick={() => setOpen(false)}>
          {t.cancel}
        </button>
      </div>
    </form>
  );
}
