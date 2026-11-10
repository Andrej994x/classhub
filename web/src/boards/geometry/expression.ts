/**
 * A safe parser for function terms f(x) (R3-P5): its own tokenizer and recursive-descent parser into a small syntax
 * tree, evaluated by walking the tree. Never `eval`, `new Function` or JSXGraph's own expression language: a term can
 * only be numbers, x, the constants pi and e, + - * / ^, parentheses and the functions listed below. The API checks
 * terms with the same grammar.
 *
 * Grammar: sum := product (('+' | '-') product)*; product := unary (('*' | '/' | implicit) unary)*;
 * unary := '-' unary | power; power := primary ('^' unary)?; primary := number | 'x' | constant | fn '(' sum ')' | '(' sum ')'.
 * Implicit multiplication: "2x", "3(x+1)", "x sin(x)" (never before a number). Decimal point or comma ("2.5" and "2,5").
 */
export const MAX_TERM_LENGTH = 200;
export const MAX_DEPTH = 100;
export const MAX_NODES = 200;

export const FUNCTIONS = ['sin', 'cos', 'tan', 'asin', 'acos', 'atan', 'sqrt', 'abs', 'ln', 'log', 'exp'] as const;
type FunctionName = (typeof FUNCTIONS)[number];

export type Node =
  | { kind: 'number'; value: number }
  | { kind: 'x' }
  | { kind: 'negate'; operand: Node }
  | { kind: 'binary'; op: '+' | '-' | '*' | '/' | '^'; left: Node; right: Node }
  | { kind: 'call'; fn: FunctionName; argument: Node };

export type ParseResult = { ok: true; tree: Node } | { ok: false; error: 'empty' | 'tooLong' | 'tooComplex' | 'invalid' };

type Token = { type: 'number'; value: number } | { type: 'name'; value: string } | { type: 'op'; value: string };

const IMPLEMENTATIONS: Record<FunctionName, (v: number) => number> = {
  sin: Math.sin,
  cos: Math.cos,
  tan: Math.tan,
  asin: Math.asin,
  acos: Math.acos,
  atan: Math.atan,
  sqrt: Math.sqrt,
  abs: Math.abs,
  ln: Math.log,
  log: Math.log10,
  exp: Math.exp,
};

function tokenize(term: string): Token[] | null {
  const tokens: Token[] = [];
  let i = 0;
  while (i < term.length) {
    const c = term[i]!;
    if (c === ' ' || c === '\t') {
      i++;
    } else if (/[0-9.,]/.test(c)) {
      const match = /^(\d+([.,]\d+)?|[.,]\d+)/.exec(term.slice(i));
      if (!match) return null;
      i += match[0].length;
      // A second decimal separator ("1.2.3", "1,2,3") is a malformed number, never two numbers.
      if (i < term.length && (term[i] === '.' || term[i] === ',')) return null;
      tokens.push({ type: 'number', value: Number(match[0].replace(',', '.')) });
    } else if (/[a-zA-Z]/.test(c)) {
      const match = /^[a-zA-Z]+/.exec(term.slice(i))!;
      tokens.push({ type: 'name', value: match[0].toLowerCase() });
      i += match[0].length;
    } else if ('+-*/^()'.includes(c)) {
      tokens.push({ type: 'op', value: c });
      i++;
    } else if (c === '·' || c === '×') {
      tokens.push({ type: 'op', value: '*' });
      i++;
    } else if (c === '−') {
      tokens.push({ type: 'op', value: '-' });
      i++;
    } else {
      return null;
    }
  }
  return tokens;
}

class Parser {
  private position = 0;
  private nodes = 0;

  constructor(private readonly tokens: Token[]) {}

  parse(): Node {
    const tree = this.sum(0);
    if (this.position !== this.tokens.length) throw new SyntaxError('trailing');
    return tree;
  }

  private node<T extends Node>(n: T, depth: number): T {
    if (++this.nodes > MAX_NODES || depth > MAX_DEPTH) throw new RangeError('complex');
    return n;
  }

  private peek(): Token | undefined {
    return this.tokens[this.position];
  }

  private isOp(value: string): boolean {
    const token = this.peek();
    return token?.type === 'op' && token.value === value;
  }

  private sum(depth: number): Node {
    let left = this.product(depth + 1);
    while (this.isOp('+') || this.isOp('-')) {
      const op = (this.tokens[this.position++] as { value: '+' | '-' }).value;
      left = this.node({ kind: 'binary', op, left, right: this.product(depth + 1) }, depth);
    }
    return left;
  }

  /**
   * Whether the next token starts a factor that multiplies implicitly ("2x", "3(x+1)", "x sin(x)"). Never a number:
   * "2 3" or "x2" is refused rather than guessed.
   */
  private startsImplicitFactor(): boolean {
    const token = this.peek();
    return token !== undefined && (token.type === 'name' || (token.type === 'op' && token.value === '('));
  }

  private product(depth: number): Node {
    let left = this.unary(depth + 1);
    for (;;) {
      if (this.isOp('*') || this.isOp('/')) {
        const op = (this.tokens[this.position++] as { value: '*' | '/' }).value;
        left = this.node({ kind: 'binary', op, left, right: this.unary(depth + 1) }, depth);
      } else if (this.startsImplicitFactor()) {
        left = this.node({ kind: 'binary', op: '*', left, right: this.unary(depth + 1) }, depth);
      } else {
        return left;
      }
    }
  }

  private unary(depth: number): Node {
    if (this.isOp('-')) {
      this.position++;
      return this.node({ kind: 'negate', operand: this.unary(depth + 1) }, depth);
    }
    if (this.isOp('+')) {
      this.position++;
      return this.unary(depth + 1);
    }
    return this.power(depth + 1);
  }

  private power(depth: number): Node {
    const base = this.primary(depth + 1);
    if (this.isOp('^')) {
      this.position++;
      // Right-associative: 2^3^2 = 2^(3^2); the exponent may be negative (x^-1).
      return this.node({ kind: 'binary', op: '^', left: base, right: this.unary(depth + 1) }, depth);
    }
    return base;
  }

  private primary(depth: number): Node {
    const token = this.tokens[this.position++];
    if (!token) throw new SyntaxError('end');
    if (token.type === 'number') return this.node({ kind: 'number', value: token.value }, depth);
    if (token.type === 'op' && token.value === '(') {
      const inner = this.sum(depth + 1);
      if (!this.isOp(')')) throw new SyntaxError('paren');
      this.position++;
      return inner;
    }
    if (token.type === 'name') {
      if (token.value === 'x') return this.node({ kind: 'x' }, depth);
      if (token.value === 'pi') return this.node({ kind: 'number', value: Math.PI }, depth);
      if (token.value === 'e') return this.node({ kind: 'number', value: Math.E }, depth);
      if ((FUNCTIONS as readonly string[]).includes(token.value)) {
        if (!this.isOp('(')) throw new SyntaxError('call');
        this.position++;
        const argument = this.sum(depth + 1);
        if (!this.isOp(')')) throw new SyntaxError('paren');
        this.position++;
        return this.node({ kind: 'call', fn: token.value as FunctionName, argument }, depth);
      }
    }
    throw new SyntaxError('unexpected');
  }
}

/** Parses a term of x. Unknown names, characters or structure are refused, never guessed. */
export function parseTerm(term: string): ParseResult {
  const text = term.trim();
  if (text.length === 0) return { ok: false, error: 'empty' };
  if (text.length > MAX_TERM_LENGTH) return { ok: false, error: 'tooLong' };
  const tokens = tokenize(text);
  if (!tokens) return { ok: false, error: 'invalid' };
  try {
    return { ok: true, tree: new Parser(tokens).parse() };
  } catch (error) {
    return { ok: false, error: error instanceof RangeError ? 'tooComplex' : 'invalid' };
  }
}

/**
 * The tree's value at x, or NaN where it is not defined (sqrt(-1), 1/0, …). Undefined is undefined all the way up: every
 * subexpression that is not finite makes everything built on it NaN, so 1/(1/x) has a gap at 0 and sqrt(x)^0 is not 1
 * for negative x (in JavaScript NaN ** 0 is 1).
 */
export function evaluate(tree: Node, x: number): number {
  const value = (node: Node): number => {
    const result = raw(node);
    return Number.isFinite(result) ? result : Number.NaN;
  };
  const raw = (node: Node): number => {
    switch (node.kind) {
      case 'number':
        return node.value;
      case 'x':
        return x;
      case 'negate':
        return -value(node.operand);
      case 'call': {
        const argument = value(node.argument);
        return Number.isNaN(argument) ? Number.NaN : IMPLEMENTATIONS[node.fn](argument);
      }
      case 'binary': {
        const a = value(node.left);
        const b = value(node.right);
        if (Number.isNaN(a) || Number.isNaN(b)) return Number.NaN;
        switch (node.op) {
          case '+':
            return a + b;
          case '-':
            return a - b;
          case '*':
            return a * b;
          case '/':
            return a / b;
          case '^':
            return a ** b;
        }
      }
    }
  };
  return value(tree);
}
