import { describe, expect, it } from 'vitest';
import { evaluate, MAX_TERM_LENGTH, parseTerm } from './expression.ts';

const at = (term: string, x: number) => {
  const parsed = parseTerm(term);
  if (!parsed.ok) throw new Error(`${term}: ${parsed.error}`);
  return evaluate(parsed.tree, x);
};

describe('safe expression parser', () => {
  it('evaluates the usual school functions', () => {
    expect(at('x^2 - 2x + 1', 3)).toBe(4);
    expect(at('2x', 5)).toBe(10);
    expect(at('3(x+1)', 1)).toBe(6);
    expect(at('x sin(x)', 0)).toBe(0);
    expect(at('-x^2', 3)).toBe(-9); // the power binds tighter than the minus
    expect(at('2^3^2', 0)).toBe(512); // right-associative
    expect(at('x^-1', 4)).toBe(0.25);
    expect(at('2,5x', 2)).toBe(5); // decimal comma
    expect(at('sqrt(abs(x)) + ln(e) + log(100) + exp(0)', -16)).toBe(8);
    expect(at('sin(pi/2)', 0)).toBeCloseTo(1);
    expect(at('2 × x − 1', 2)).toBe(3);
    expect(at('1/x', 0)).toBeNaN(); // undefined points are gaps, not infinity
    expect(at('sqrt(x)', -1)).toBeNaN();
  });

  it('refuses everything that is not a term of x', () => {
    for (const term of [
      'alert(1)',
      'eval(x)',
      'Function("return 1")()',
      'x.constructor',
      'constructor',
      '__proto__',
      'this',
      'window',
      'x;1',
      'x => x',
      'x = 1',
      '${x}',
      '`x`',
      '<script>',
      'y + 1',
      'sin x',
      'sin()',
      '2^^3',
      '(x',
      'x)',
      '1..2',
      'x[0]',
      'x, 1',
    ]) {
      expect({ term, ok: parseTerm(term).ok }).toEqual({ term, ok: false });
    }
  });

  it('limits length, nesting and size', () => {
    expect(parseTerm('')).toEqual({ ok: false, error: 'empty' });
    expect(parseTerm('x+'.repeat(MAX_TERM_LENGTH))).toEqual({ ok: false, error: 'tooLong' });
    expect(parseTerm(`${'('.repeat(60)}x${')'.repeat(60)}`)).toEqual({ ok: false, error: 'tooComplex' });
    expect(parseTerm('-'.repeat(120) + 'x')).toEqual({ ok: false, error: 'tooComplex' });
    expect(parseTerm(`${'('.repeat(10)}x${')'.repeat(10)}`)).toMatchObject({ ok: true });
  });
});
