/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { currencyDigits, formatMoney, THREE_DIGIT_CURRENCIES, ZERO_DIGIT_CURRENCIES } from './format.ts';

/**
 * Prices are integer minor units. The SPA converts typed prices with currencyDigits; the API renders public prices
 * with Currencies.MinorDigits (Iso.cs). Both must use the same fixed table, independent of the browser's CLDR data.
 */
describe('currency minor units', () => {
  // Read from disk (the C# file is outside the Vite root); vitest runs in web/.
  const source = readFileSync(resolve(process.cwd(), '../src/ClassHub.Core/SharedKernel/Globalization/Iso.cs'), 'utf8');
  const list = (name: string) =>
    source
      .match(new RegExp(`${name}\\s*=\\s*((?:"[^"]*"\\s*\\+?\\s*)+);`))![1]!
      .replace(/"\s*\+\s*"/g, ' ')
      .replace(/"/g, '')
      .split(/\s+/)
      .filter(Boolean);

  it('use the same table as the API', () => {
    expect(ZERO_DIGIT_CURRENCIES.toSorted()).toEqual(list('ZeroDigitCodes').toSorted());
    expect(THREE_DIGIT_CURRENCIES.toSorted()).toEqual(list('ThreeDigitCodes').toSorted());
    const active = list('ActiveCodes');
    expect(active.length).toBeGreaterThan(150);
    expect([...ZERO_DIGIT_CURRENCIES, ...THREE_DIGIT_CURRENCIES].every((code) => active.includes(code))).toBe(true);
  });

  it('format amounts with exactly the table digits, whatever the browser data says', () => {
    expect(currencyDigits('MKD')).toBe(2);
    expect(currencyDigits('JPY')).toBe(0);
    expect(currencyDigits('KWD')).toBe(3);
    expect(currencyDigits('COP')).toBe(2);
    expect(formatMoney(123_456, 'COP', 'en')).toMatch(/1,234\.56/);
    expect(formatMoney(5_000, 'JPY', 'en')).toMatch(/5,000(?!\.)/);
  });
});
