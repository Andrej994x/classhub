namespace ClassHub.Core.Modules.Activities.Domain;

/// <summary>
/// The geometry board's function terms (R3-P5): the same grammar as the client's safe parser
/// (<c>web/src/boards/geometry/expression.ts</c>), used only to check that a saved term is a term of x. Terms are never
/// evaluated on the server. Numbers, x, pi, e, + - * / ^, parentheses, implicit multiplication and the functions
/// sin, cos, tan, asin, acos, atan, sqrt, abs, ln, log, exp; at most 200 characters, 100 levels of recursion (about 20 nested parentheses) and 200 nodes.
/// </summary>
public static class GeometryTerms
{
    public const int MaxLength = 200;
    private const int MaxDepth = 100;
    private const int MaxNodes = 200;

    private static readonly HashSet<string> Functions = new(StringComparer.Ordinal)
    {
        "sin", "cos", "tan", "asin", "acos", "atan", "sqrt", "abs", "ln", "log", "exp",
    };

    private enum Kind
    {
        Number,
        Name,
        Op,
    }

    private readonly record struct Token(Kind Kind, string Value);

    public static bool IsValid(string? term)
    {
        var text = term?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength || Tokenize(text) is not { } tokens)
        {
            return false;
        }

        try
        {
            var parser = new Parser(tokens);
            parser.Sum(0);
            return parser.AtEnd;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static List<Token>? Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c is ' ' or '\t')
            {
                i++;
            }
            else if (char.IsAsciiDigit(c) || c is '.' or ',')
            {
                // digits, optionally one decimal point or comma followed by digits; or a leading separator and digits
                var start = i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                if (i < text.Length && text[i] is '.' or ',')
                {
                    i++;
                    var fraction = i;
                    while (i < text.Length && char.IsAsciiDigit(text[i]))
                    {
                        i++;
                    }

                    if (i == fraction)
                    {
                        return null;
                    }
                }

                if (i == start)
                {
                    return null;
                }

                tokens.Add(new Token(Kind.Number, text[start..i]));
            }
            else if (char.IsAsciiLetter(c))
            {
                var start = i;
                while (i < text.Length && char.IsAsciiLetter(text[i]))
                {
                    i++;
                }

                tokens.Add(new Token(Kind.Name, text[start..i].ToLowerInvariant()));
            }
            else if (c is '+' or '-' or '*' or '/' or '^' or '(' or ')')
            {
                tokens.Add(new Token(Kind.Op, c.ToString()));
                i++;
            }
            else if (c is '·' or '×')
            {
                tokens.Add(new Token(Kind.Op, "*"));
                i++;
            }
            else if (c is '−')
            {
                tokens.Add(new Token(Kind.Op, "-"));
                i++;
            }
            else
            {
                return null;
            }
        }

        return tokens;
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int position;
        private int nodes;

        public bool AtEnd => position == tokens.Count;

        private Token? Peek => position < tokens.Count ? tokens[position] : null;

        private bool IsOp(string op) => Peek is { Kind: Kind.Op } t && t.Value == op;

        private void Node(int depth)
        {
            if (++nodes > MaxNodes || depth > MaxDepth)
            {
                throw new FormatException("Too complex.");
            }
        }

        public void Sum(int depth)
        {
            Product(depth + 1);
            while (IsOp("+") || IsOp("-"))
            {
                position++;
                Product(depth + 1);
                Node(depth);
            }
        }

        private bool StartsImplicitFactor() => Peek is { } t && (t.Kind is Kind.Name or Kind.Number || (t.Kind == Kind.Op && t.Value == "("));

        private void Product(int depth)
        {
            Unary(depth + 1);
            while (true)
            {
                if (IsOp("*") || IsOp("/"))
                {
                    position++;
                }
                else if (!StartsImplicitFactor())
                {
                    return;
                }

                Unary(depth + 1);
                Node(depth);
            }
        }

        private void Unary(int depth)
        {
            if (IsOp("-") || IsOp("+"))
            {
                position++;
                Node(depth);
                Unary(depth + 1);
                return;
            }

            Power(depth + 1);
        }

        private void Power(int depth)
        {
            Primary(depth + 1);
            if (IsOp("^"))
            {
                position++;
                Node(depth);
                Unary(depth + 1);
            }
        }

        private void Primary(int depth)
        {
            if (Peek is not { } token)
            {
                throw new FormatException("Unexpected end.");
            }

            position++;
            Node(depth);
            switch (token.Kind)
            {
                case Kind.Number:
                    return;
                case Kind.Op when token.Value == "(":
                    Sum(depth + 1);
                    Close();
                    return;
                case Kind.Name when token.Value is "x" or "pi" or "e":
                    return;
                case Kind.Name when Functions.Contains(token.Value):
                    if (!IsOp("("))
                    {
                        throw new FormatException("A function needs parentheses.");
                    }

                    position++;
                    Sum(depth + 1);
                    Close();
                    return;
                default:
                    throw new FormatException("Unexpected token.");
            }
        }

        private void Close()
        {
            if (!IsOp(")"))
            {
                throw new FormatException("Missing parenthesis.");
            }

            position++;
        }
    }
}
