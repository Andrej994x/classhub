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

/**
 * Currencies whose minor unit has 0 or 3 digits (all others: 2). A fixed table, identical to the API's
 * `Currencies.MinorDigits` (Iso.cs; a test compares them), because prices are stored as integer minor units: their
 * meaning must never depend on the CLDR version of the browser (Intl reports e.g. COP differently across versions).
 */
export const ZERO_DIGIT_CURRENCIES: readonly string[] = [
  'AFN', 'ALL', 'BIF', 'CLP', 'DJF', 'GNF', 'IQD', 'IRR', 'ISK', 'JPY', 'KMF', 'KPW', 'KRW', 'LAK', 'LBP',
  'MGA', 'MMK', 'PYG', 'RSD', 'RWF', 'SOS', 'SYP', 'UGX', 'VND', 'VUV', 'XAF', 'XOF', 'XPF', 'YER',
];
export const THREE_DIGIT_CURRENCIES: readonly string[] = ['BHD', 'JOD', 'KWD', 'LYD', 'OMR', 'TND'];

/** Number of minor-unit digits of a currency (2 for EUR/MKD, 0 for JPY, 3 for KWD). */
export function currencyDigits(currency: string): number {
  return ZERO_DIGIT_CURRENCIES.includes(currency) ? 0 : THREE_DIGIT_CURRENCIES.includes(currency) ? 3 : 2;
}

/** Minor units → localized amount with currency, e.g. 150000 MKD → "1.500,00 ден." */
export function formatMoney(amountMinor: number, currency: string, language: string): string {
  const digits = currencyDigits(currency);
  const format = new Intl.NumberFormat(toLocale(language), { style: 'currency', currency, minimumFractionDigits: digits, maximumFractionDigits: digits });
  return format.format(amountMinor / 10 ** digits);
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
