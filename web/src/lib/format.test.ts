import { describe, expect, it } from 'vitest';
import en from '../i18n/locales/en.json';
import mk from '../i18n/locales/mk.json';
import { ApiError, errorMessage, toApiError } from '../api/errors.ts';
import { i18n } from '../i18n/index.ts';
import { formatDate, formatDateTime, formatMoney, formatTime } from './format.ts';

describe('formatting', () => {
  it('shows UTC times in the requested time zone (DST-aware)', () => {
    // 2026-03-29 is the European DST switch: 00:30 UTC is 01:30 in Skopje, 06:30 UTC is 08:30.
    expect(formatTime('2026-03-29T00:30:00Z', 'en', 'Europe/Skopje')).toBe('01:30');
    expect(formatTime('2026-03-29T06:30:00Z', 'en', 'Europe/Skopje')).toBe('08:30');
    expect(formatDateTime('2026-11-04T12:00:00Z', 'en', 'UTC')).toContain('12:00');
  });

  it('formats dates in Macedonian and English', () => {
    expect(formatDate('2026-11-04T12:00:00Z', 'mk', 'Europe/Skopje')).toMatch(/2026/);
    expect(formatDate('2026-11-04T12:00:00Z', 'en', 'Europe/Skopje')).toMatch(/Nov/);
  });

  it('formats minor units with the currency, never without it', () => {
    expect(formatMoney(150_000, 'MKD', 'en')).toMatch(/MKD|ден/);
    expect(formatMoney(150_000, 'MKD', 'en')).toMatch(/1,500/);
    expect(formatMoney(2_550, 'EUR', 'en')).toBe('€25.50');
  });
});

describe('error messages', () => {
  it('translates API error codes and never shows server titles', async () => {
    await i18n.changeLanguage('en');
    const error = await toApiError(new Response(JSON.stringify({ code: 'RATE_LIMITED', title: 'raw server text' }), { status: 429 }));
    expect(errorMessage(error, i18n.t)).toBe(en.errors.codes.RATE_LIMITED);

    await i18n.changeLanguage('mk');
    expect(errorMessage(error, i18n.t)).toBe(mk.errors.codes.RATE_LIMITED);
  });

  it('falls back to a generic message for unknown codes and non-JSON responses', async () => {
    await i18n.changeLanguage('en');
    expect(errorMessage(new ApiError(418, 'SOMETHING_NEW', 'x'), i18n.t)).toBe(en.errors.generic);
    const htmlError = await toApiError(new Response('<html>bad gateway</html>', { status: 502 }));
    expect(htmlError.code).toBe('INTERNAL_ERROR');
    expect(errorMessage(new Error('boom'), i18n.t)).toBe(en.errors.codes.INTERNAL_ERROR);
    await i18n.changeLanguage('mk');
  });
});
