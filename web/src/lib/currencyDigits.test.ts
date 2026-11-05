/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { currencyDigits } from './format.ts';

/**
 * The SPA turns prices into minor units with Intl (currencyDigits); the API renders public prices with its own table
 * (Currencies.MinorDigits in Iso.cs). Both must agree for every currency the API accepts.
 */
describe('currency minor units', () => {
  it('match the API table for every active ISO 4217 code', () => {
    // Read from disk (the C# file is outside the Vite root); vitest runs in web/.
    const source = readFileSync(resolve(process.cwd(), '../src/ClassHub.Core/SharedKernel/Globalization/Iso.cs'), 'utf8');
    const list = (name: string) => source.match(new RegExp(`${name}\\s*=\\s*((?:"[^"]*"\\s*\\+?\\s*)+);`))![1]!.replace(/"\s*\+\s*"/g, ' ').replace(/"/g, '').split(/\s+/).filter(Boolean);
    const active = list('ActiveCodes');
    const zero = new Set(list('ZeroDigitCodes'));
    const three = new Set(list('ThreeDigitCodes'));

    expect(active.length).toBeGreaterThan(150);
    for (const code of active) {
      expect([code, currencyDigits(code)]).toEqual([code, zero.has(code) ? 0 : three.has(code) ? 3 : 2]);
    }
  });
});
