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
        "جمهورية", "العربية", "بطاقة", "قومي", "المحافظة", "العنوان", "الاسم",
        "القاهرة", "الجيزة", "حدائق", "شارع", "حارة", "سكة", "طريق", "ميدان",
        "منطقة", "قسم", "مركز", "قرية", "نجع", "كفر", "عزبة",
        // The card's fixed vocabulary: never a person's name, no matter how cleanly read.
        "وزارة", "الداخلية", "تحقيق", "شخصية", "مصر", "الرقم", "النوع", "الديانة",
        "الجنسية", "تاريخ", "الميلاد", "الوظيفة", "الحالة", "الاجتماعية", "الانتهاء"
    };

    private static readonly string[] AddressLabels =
    {
        "محل الاقامة", "محل الإقامة", "العنوان", "عنوان"
    };

    /// <summary>One OCR text line with its vertical position on the card and the read confidence.</summary>
    public readonly record struct PositionedLine(string Text, double Top, double Confidence);

    /// <summary>
    /// Selects the name and address from positioned OCR lines (PP-OCRv5): lines under the
    /// confidence floor are dropped and the rest is ordered top to bottom by its bounding box
    /// before the usual label and layout rules run, so a noisy low-confidence line can no longer
    /// displace the real fields.
    /// </summary>
    public static (string? Name, string? Address) SelectPositioned(
        IEnumerable<PositionedLine> lines,
        double minimumConfidence = 0.5)
    {
        var ordered = lines
            .Where(line => line.Confidence >= minimumConfidence && !string.IsNullOrWhiteSpace(line.Text))
            .OrderBy(line => line.Top)
            .Select(line => line.Text);

        return Select(string.Join('\n', ordered));
    }

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

        name ??= ReadUnlabelledName(lines);

        // PP-OCR's detector sometimes boxes the name and the first address line together (they
        // sit close on the card). The house number then fails every name check, so split such a
        // line at the first token that holds a digit: the prefix is the name candidate and the
        // digit token starts the address candidates.
        string? splitAddress = null;
        var splitIndex = -1;
        if (name is null)
        {
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                if (line.NameLabel || line.AddressLabel)
                {
                    continue;
                }

                var (prefix, suffix) = SplitAtFirstDigitToken(line.Text);
                if (prefix is not null && IsNameLike(prefix))
                {
                    name = prefix;
                    splitAddress = suffix;
                    splitIndex = index;
                    break;
                }
            }
        }

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

        // The current Egyptian card prints no labels: the address is the one or two lines that
        // follow the name. Only lines that look like addresses are taken, and only after a name.
        address ??= ReadAddressAfterName(lines, name);

        if (address is null && splitIndex >= 0)
        {
            var parts = new List<string>();
            if (IsAddressLike(splitAddress))
            {
                parts.Add(splitAddress!);
            }

            foreach (var next in lines.Skip(splitIndex + 1))
            {
                if (next.NameLabel || next.AddressLabel || !IsAddressLike(next.Text))
                {
                    continue;
                }

                parts.Add(next.Text);
                if (parts.Count == 2)
                {
                    break;
                }
            }

            address = parts.Count == 0 ? null : string.Join(" ", parts);
        }

        // The name's decorated font can defeat every reader while the address reads cleanly;
        // a digit-anchored address (the house number) must not be held hostage by a missing name.
        address ??= ReadUnlabelledAddress(lines);

        if (SameLine(address, name))
        {
            address = null;
        }

        return (name, address);
    }

    private static string? ReadAddressAfterName(IReadOnlyList<FieldLine> lines, string? name)
    {
        if (name is null)
        {
            return null;
        }

        var nameIndex = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            var text = lines[index].Text;
            if (text.Length > 0 && name.EndsWith(text, StringComparison.Ordinal))
            {
                nameIndex = index;
            }
        }

        if (nameIndex < 0)
        {
            return null;
        }

        var following = lines
            .Skip(nameIndex + 1)
            .Where(line => !line.NameLabel && !line.AddressLabel && IsAddressLike(line.Text))
            .Select(line => line.Text)
            .Take(2)
            .ToList();

        return following.Count == 0 ? null : string.Join(" ", following);
    }

    /// <summary>
    /// Finds the address without a name: the anchor is a line that carries a digit (the house
    /// number) and still looks like an address; the following address-like line completes it.
    /// </summary>
    private static string? ReadUnlabelledAddress(IReadOnlyList<FieldLine> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.NameLabel || line.AddressLabel || !line.Text.Any(char.IsDigit) || !IsAddressLike(line.Text))
            {
                continue;
            }

            var following = lines
                .Skip(index + 1)
                .Where(next => !next.NameLabel && !next.AddressLabel && IsAddressLike(next.Text))
                .Take(1)
                .ToList();

            return following.Count == 0 ? line.Text : $"{line.Text} {following[0].Text}";
        }

        return null;
    }

    private static string? ReadUnlabelledName(IReadOnlyList<FieldLine> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.NameLabel || line.AddressLabel || !IsNameLike(line.Text))
            {
                continue;
            }

            if (index > 0)
            {
                var previous = lines[index - 1];
                var combined = $"{previous.Text} {line.Text}";
                if (!previous.NameLabel
                    && !previous.AddressLabel
                    && IsSingleNamePart(previous.Text)
                    && IsNameLike(combined))
                {
                    return combined;
                }
            }

            return line.Text;
        }

        return null;
    }

    // The card's fixed phrases, tokenized and spelling-normalized, so even a slightly misread
    // heading ("جمهوريه مصر العربيه") is never mistaken for a person.
    private static readonly string[] CardPhrases =
    {
        "جمهورية مصر العربية", "بطاقة تحقيق شخصية", "وزارة الداخلية", "الرقم القومي"
    };

    private static readonly HashSet<string> CardVocabulary = CardPhrases
        .SelectMany(phrase => phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Select(NormalizeToken)
        .ToHashSet(StringComparer.Ordinal);

    private static bool IsCardVocabulary(string[] tokens)
    {
        return tokens.Length > 0 && tokens.All(token => CardVocabulary.Contains(NormalizeToken(token)));
    }

    private static string NormalizeToken(string token)
    {
        return token
            .Replace('\u0623', '\u0627') // أ -> ا
            .Replace('\u0625', '\u0627') // إ -> ا
            .Replace('\u0622', '\u0627') // آ -> ا
            .Replace('\u0649', '\u064A') // ى -> ي
            .Replace('\u0629', '\u0647'); // ة -> ه
    }

    private static bool IsSingleNamePart(string value)
    {
        var token = value.Trim();
        return token.Length is >= 2 and <= 15
            && token.All(char.IsLetter)
            && !NotAName.Contains(token, StringComparer.Ordinal);
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

        // OCR noise tends to fragment into one and two letter shards ("بم عاله"); real Egyptian
        // name parts are longer. Requiring 3+ letters per token kills that garbage without
        // rejecting real names.
        if (tokens.Any(token => token.Length < 3))
        {
            return false;
        }

        if (trimmed.Any(char.IsDigit) || tokens.Any(token => NotAName.Contains(token, StringComparer.Ordinal)))
        {
            return false;
        }

        if (IsCardVocabulary(tokens))
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

    /// <summary>
    /// Splits a line at the first token holding a digit (a house number starts the address on
    /// the card). The digit token itself belongs to the address side.
    /// </summary>
    private static (string? Prefix, string? Suffix) SplitAtFirstDigitToken(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            if (tokens[index].Any(char.IsDigit))
            {
                var prefix = string.Join(' ', tokens[..index]);
                var suffix = string.Join(' ', tokens[index..]);
                return (prefix.Length == 0 ? null : prefix, suffix.Length == 0 ? null : suffix);
            }
        }

        return (null, null);
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

        // Diacritics add no value for matching and confuse the letter ratio. Neural readers
        // also emit the superscript alef and the Quranic annotation marks for decorated fonts.
        var withoutDiacritics = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is >= '\u064B' and <= '\u065F' or '\u0670' or >= '\u06D6' and <= '\u06ED')
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
