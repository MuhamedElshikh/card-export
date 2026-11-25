using System.Text;

namespace IdCardReader.Infrastructure.Imaging;

/// <summary>
/// Chooses the name and address lines out of raw Arabic OCR text.
/// Garbage is rejected rather than shown: an empty field is better than a wrong one,
/// because the user reviews both fields before saving.
/// </summary>
public static class ArabicTextFields
{
    private const double MinimumNameLetterRatio = 0.8;
    private const double MinimumAddressLetterRatio = 0.65;
    private const int MinimumFieldLength = 6;

    // The full label must be tried first, otherwise "بالكامل" is mistaken for the person's name.
    private static readonly string[] NameLabels = { "الاسم بالكامل", "اسم بالكامل", "الاسم", "اسم" };

    private static readonly string[] NotAName =
    {
        "جمهورية", "العربية", "بطاقة", "قومي", "المحافظة", "العنوان", "الاسم"
    };

    private static readonly string[] AddressLabels =
    {
        "محل الاقامة", "محل الإقامة", "العنوان", "عنوان"
    };

    /// <summary>Returns the best name and address found in the text, or nulls.</summary>
    public static (string? Name, string? Address) Select(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        var lines = ParseLines(text);

        string? name = null;
        foreach (var line in lines)
        {
            if (line.NameLabel && IsNameLike(line.Text))
            {
                name = line.Text;
                break;
            }
        }

        // A bare "الاسم" label usually sits on its own line, with the value on the next one.
        if (name is null && lines.Any(line => line.NameLabel && line.Text.Length == 0))
        {
            name = lines
                .SkipWhile(line => !(line.NameLabel && line.Text.Length == 0))
                .Skip(1)
                .Select(line => line.Text)
                .FirstOrDefault(line => IsNameLike(line));
        }

        // No unlabelled line is ever promoted to a name. A missing name is edited by the user;
        // a guessed name (a card heading, a governorate, the label remainder) is not.

        string? address = null;
        foreach (var line in lines)
        {
            if (line.AddressLabel && IsAddressLike(line.Text))
            {
                address = line.Text;
                break;
            }
        }

        if (address is null && lines.Any(line => line.AddressLabel && line.Text.Length == 0))
        {
            var afterLabel = lines
                .SkipWhile(line => !(line.AddressLabel && line.Text.Length == 0))
                .Skip(1)
                .Select(line => line.Text)
                .Where(IsAddressLike)
                .Take(2)
                .ToList();

            address = afterLabel.Count == 0 ? null : string.Join(" ", afterLabel);
        }

        // Unlabelled Arabic text is not promoted to an address either.

        if (SameLine(address, name))
        {
            address = null;
        }

        return (name, address);
    }

    /// <summary>True when the line looks like a person name (no digits, mostly letters).</summary>
    public static bool IsNameLike(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.Length < MinimumFieldLength)
        {
            return false;
        }

        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 2 or > 8)
        {
            return false;
        }

        if (trimmed.Any(char.IsDigit) || tokens.Any(token => NotAName.Contains(token, StringComparer.Ordinal)))
        {
            return false;
        }

        return LetterRatio(trimmed) >= MinimumNameLetterRatio;
    }

    /// <summary>True when the line looks like an address (longer, may contain a house number).</summary>
    public static bool IsAddressLike(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.Length < MinimumFieldLength)
        {
            return false;
        }

        if (CountTokens(trimmed) < 2)
        {
            return false;
        }

        var digits = trimmed.Count(char.IsDigit);
        if (digits > trimmed.Length * 0.25)
        {
            return false;
        }

        return LetterRatio(trimmed) >= MinimumAddressLetterRatio;
    }

    private static bool SameLine(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(Collapse(left), Collapse(right), StringComparison.Ordinal);
    }

    private static string Collapse(string value)
    {
        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static List<FieldLine> ParseLines(string text)
    {
        var result = new List<FieldLine>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = Clean(rawLine);
            if (line.Length < 2)
            {
                continue;
            }

            var (withoutName, hadName) = StripLabel(line, NameLabels);
            var (withoutAddress, hadAddress) = StripLabel(withoutName, AddressLabels);
            var body = Clean(withoutAddress);

            if (!hadName && !hadAddress && body.Length < 3)
            {
                continue;
            }

            if (!hadName && !hadAddress && (IsMostlyDigits(body) || LetterRatio(body) < 0.5))
            {
                // Rows of numbers or symbol soup are never labels for a person.
                continue;
            }

            result.Add(new FieldLine(body, hadName, hadAddress));
        }

        return result;
    }

    private static (string Text, bool Found) StripLabel(string line, string[] labels)
    {
        foreach (var label in labels.OrderByDescending(value => value.Length))
        {
            var index = line.IndexOf(label, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var beforeIsBoundary = index == 0 || !char.IsLetter(line[index - 1]);
            var end = index + label.Length;
            var afterIsBoundary = end >= line.Length || !char.IsLetter(line[end]);

            if (!beforeIsBoundary || !afterIsBoundary)
            {
                continue;
            }

            return (line[end..].Trim(' ', ':', '-', '.', '،', '|'), true);
        }

        return (line, false);
    }

    private static string Clean(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (character is '\u0640' or '\u200F' or '\u200E' or '\u202A' or '\u202B' or '\u202C')
            {
                continue;
            }

            if (char.IsControl(character) && character != '\t')
            {
                continue;
            }

            builder.Append(char.IsWhiteSpace(character) ? ' ' : character);
        }

        var text = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // Diacritics add no value for matching and confuse the letter ratio.
        var withoutDiacritics = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is >= '\u064B' and <= '\u0652')
            {
                continue;
            }

            withoutDiacritics.Append(character);
        }

        return withoutDiacritics.ToString().Trim(' ', '.', ',', ':', ';', '-', '_', '|', '/', '\\', '"', '\'');
    }

    private static bool IsMostlyDigits(string line)
    {
        var digits = line.Count(char.IsDigit);
        return line.Length > 0 && digits > line.Length / 2;
    }

    private static int CountTokens(string line)
    {
        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static double LetterRatio(string line)
    {
        var letters = 0;
        var considered = 0;

        foreach (var character in line)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            considered++;
            if (char.IsLetter(character))
            {
                letters++;
            }
        }

        return considered == 0 ? 0 : letters / (double)considered;
    }

    private readonly record struct FieldLine(string Text, bool NameLabel, bool AddressLabel);
}
