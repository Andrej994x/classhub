/**
 * Locale-aware formatting. Times are stored in UTC by the API and shown in the viewer's (or the
 * workspace's) IANA time zone; amounts are integer minor units with an ISO 4217 currency and are always
 * shown with their currency (never mixed without a label).
 */
const LOCALES: Record<string, string> = { mk: 'mk-MK', en: 'en-GB' };

const toLocale = (language: string) => LOCALES[language] ?? 'mk-MK';

export function formatDate(isoUtc: string, language: string, timeZone: string): string {
  return new Intl.DateTimeFormat(toLocale(language), { dateStyle: 'medium', timeZone }).format(new Date(isoUtc));
}

export function formatDateTime(isoUtc: string, language: string, timeZone: string): string {
  return new Intl.DateTimeFormat(toLocale(language), { dateStyle: 'medium', timeStyle: 'short', timeZone }).format(new Date(isoUtc));
}

export function formatTime(isoUtc: string, language: string, timeZone: string): string {
  return new Intl.DateTimeFormat(toLocale(language), { timeStyle: 'short', timeZone }).format(new Date(isoUtc));
}

/** Minor units → localized amount with currency, e.g. 150000 MKD → "1.500,00 ден." */
export function formatMoney(amountMinor: number, currency: string, language: string): string {
  const format = new Intl.NumberFormat(toLocale(language), { style: 'currency', currency });
  const digits = format.resolvedOptions().maximumFractionDigits ?? 2;
  return format.format(amountMinor / 10 ** digits);
}
