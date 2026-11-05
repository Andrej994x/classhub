using Serilog.Core;
using Serilog.Events;

namespace ClassHub.Api.Platform;

/// <summary>
/// Redacts sensitive log event properties before any sink sees them: secrets (passwords, tokens, keys,
/// cookies, authorization headers, security stamps, 2FA and recovery codes) become <c>***</c>, email
/// addresses are masked (<c>a***@example.com</c>). Works on nested objects, dictionaries and lists.
/// Capability URLs are redacted by value, whatever the property is called: the token segment of
/// <c>/preview/{token}</c> (request paths, URLs) becomes <c>***</c>.
/// Message text itself is not scanned: never interpolate secrets into log messages.
/// </summary>
public sealed partial class SensitiveDataRedactionEnricher : ILogEventEnricher
{
    /// <summary>The token segment of a site-preview URL (a bearer capability for the draft).</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"(/preview/)[^/?#\s""']+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex PreviewToken();

    internal static string RedactCapabilities(string text) =>
        text.Contains("/preview/", StringComparison.OrdinalIgnoreCase) ? PreviewToken().Replace(text, "$1" + Redacted) : text;

    public const string Redacted = "***";

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "cookie", "set-cookie", "securitystamp", "concurrencystamp", "apikey", "api-key",
        "twofactorcode", "recoverycode", "authenticatorkey", "authenticatorcode", "connectionstring", "passwordhash",
    };

    private static readonly string[] SensitiveFragments = ["password", "secret", "token"];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        foreach (var property in logEvent.Properties.ToList())
        {
            var redacted = Redact(property.Key, property.Value);
            if (!ReferenceEquals(redacted, property.Value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, redacted));
            }
        }
    }

    internal static bool IsSensitive(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (SensitiveNames.Contains(name))
        {
            return true;
        }

        // CancellationToken is plumbing, not a secret.
        if (name.Contains("cancellation", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SensitiveFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    internal static string MaskEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            return Redacted;
        }

        return string.Concat(value.AsSpan(0, 1), Redacted, value.AsSpan(at));
    }

    private static LogEventPropertyValue Redact(string name, LogEventPropertyValue value)
    {
        if (IsSensitive(name))
        {
            return new ScalarValue(Redacted);
        }

        switch (value)
        {
            case ScalarValue { Value: string path } when path.Contains("/preview/", StringComparison.OrdinalIgnoreCase):
                return new ScalarValue(RedactCapabilities(path));

            case ScalarValue { Value: string text } when IsEmailName(name):
                return new ScalarValue(MaskEmail(text));

            case StructureValue structure:
            {
                var changed = false;
                var properties = structure.Properties.Select(p =>
                {
                    var r = Redact(p.Name, p.Value);
                    changed |= !ReferenceEquals(r, p.Value);
                    return new LogEventProperty(p.Name, r);
                }).ToList();
                return changed ? new StructureValue(properties, structure.TypeTag) : structure;
            }

            case DictionaryValue dictionary:
            {
                var changed = false;
                var elements = dictionary.Elements.Select(kv =>
                {
                    var key = kv.Key.Value?.ToString() ?? string.Empty;
                    var r = Redact(key, kv.Value);
                    changed |= !ReferenceEquals(r, kv.Value);
                    return new KeyValuePair<ScalarValue, LogEventPropertyValue>(kv.Key, r);
                }).ToList();
                return changed ? new DictionaryValue(elements) : dictionary;
            }

            case SequenceValue sequence:
            {
                var changed = false;
                var elements = sequence.Elements.Select(e =>
                {
                    var r = Redact(name, e);
                    changed |= !ReferenceEquals(r, e);
                    return r;
                }).ToList();
                return changed ? new SequenceValue(elements) : sequence;
            }

            default:
                return value;
        }
    }

    private static bool IsEmailName(string name) => name.Contains("email", StringComparison.OrdinalIgnoreCase);
}
