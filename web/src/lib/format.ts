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

/** Number of minor-unit digits of a currency (2 for EUR/MKD, 0 for JPY). */
export function currencyDigits(currency: string): number {
  return new Intl.NumberFormat('en', { style: 'currency', currency }).resolvedOptions().maximumFractionDigits ?? 2;
}

/**
 * Parses a price typed by the teacher ("1500", "25,50", "25.50") into integer minor units, or null when it is not a
 * valid non-negative amount with at most the currency's decimals. Thousands separators are not accepted.
 */
export function parseMoney(input: string, currency: string): number | null {
  const digits = currencyDigits(currency);
  const text = input.trim().replace(',', '.');
  const pattern = digits > 0 ? new RegExp(String.raw`^\d{1,9}(\.\d{1,${digits}})?$`) : /^\d{1,9}$/;
  if (!pattern.test(text)) return null;
  const [whole, fraction = ''] = text.split('.');
  return Number(whole) * 10 ** digits + Number(fraction.padEnd(digits, '0') || '0');
}

/** Minor units → the plain amount for an input field (no currency symbol, "." as decimal separator). */
export function moneyInput(amountMinor: number, currency: string): string {
  const digits = currencyDigits(currency);
  return digits === 0 ? String(amountMinor) : (amountMinor / 10 ** digits).toFixed(digits);
}
