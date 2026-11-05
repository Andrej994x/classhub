namespace ClassHub.Core.SharedKernel.Globalization;

/// <summary>URL slugs from free text: lowercase ASCII letters, digits and single hyphens; Macedonian Cyrillic is transliterated.</summary>
public static class Slugs
{
    /// <summary>"Ана Петровска" → "ana-petrovska"; empty when nothing usable remains. At most <paramref name="maxLength"/> characters.</summary>
    public static string FromText(string? text, int maxLength)
    {
        var lowered = (text ?? string.Empty).Trim().ToLowerInvariant();
        var slug = string.Concat(lowered.Select(Transliterate));
        slug = string.Join('-', slug.Split(['-', ' '], StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > maxLength)
        {
            slug = slug[..maxLength].TrimEnd('-');
        }

        return slug;
    }

    private static string Transliterate(char c) => c switch
    {
        >= 'a' and <= 'z' or >= '0' and <= '9' => c.ToString(),
        'а' => "a", 'б' => "b", 'в' => "v", 'г' => "g", 'д' => "d", 'ѓ' => "gj", 'е' => "e", 'ж' => "zh", 'з' => "z", 'ѕ' => "dz",
        'и' => "i", 'ј' => "j", 'к' => "k", 'л' => "l", 'љ' => "lj", 'м' => "m", 'н' => "n", 'њ' => "nj", 'о' => "o", 'п' => "p",
        'р' => "r", 'с' => "s", 'т' => "t", 'ќ' => "kj", 'у' => "u", 'ф' => "f", 'х' => "h", 'ц' => "c", 'ч' => "ch", 'џ' => "dj", 'ш' => "sh",
        _ => "-",
    };
}
