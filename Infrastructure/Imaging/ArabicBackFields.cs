using System.Globalization;
using System.Text;

namespace IdCardReader.Infrastructure.Imaging;

/// <summary>Best-effort fields read from the back of an Egyptian national ID card.</summary>
public sealed record ArabicBackCardFields(
    string? Occupation,
    string? MaritalStatus,
    DateOnly? ExpiryDate);

/// <summary>Extracts labeled or layout-positioned values from the back-side Arabic OCR text.</summary>
public static class ArabicBackFields
{
    private static readonly string[] OccupationLabels = { "المهنة", "المهنه" };
    private static readonly string[] MaritalStatusLabels = { "الحالة الاجتماعية", "الحاله الاجتماعيه" };
    private static readonly string[] ExpiryDateLabels =
    {
        "تاريخ الانتهاء", "البطاقة سارية حتى", "البطاقه ساريه حتى",
        "البطاقة سارية حتي", "البطاقه ساريه حتي", "صالحة حتى", "صالحة الي", "تنتهي في", "ينتهي في"
    };

    private static readonly IReadOnlyDictionary<string, string> MaritalStatuses =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["اعزب"] = "أعزب",
            ["عزباء"] = "عزباء",
            ["متزوج"] = "متزوج",
            ["متزوجة"] = "متزوجة",
            ["متزوجه"] = "متزوجة",
            ["مطلق"] = "مطلق",
            ["مطلقة"] = "مطلقة",
            ["مطلقه"] = "مطلقة",
            ["ارمل"] = "أرمل",
            ["ارملة"] = "أرملة",
            ["ارمله"] = "أرملة",
            ["انسة"] = "آنسة",
            ["انسه"] = "آنسة"
        };

    private static readonly string[] AllLabels = OccupationLabels
        .Concat(MaritalStatusLabels)
        .Concat(ExpiryDateLabels)
        .ToArray();

    /// <summary>Returns recognized labeled values, leaving unrecognized fields null.</summary>
    public static ArabicBackCardFields Select(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ArabicBackCardFields(null, null, null);
        }

        var lines = text.Split('\n').Select(Clean).Where(line => line.Length > 0).ToArray();

        var occupation = ReadLabeledValue(lines, OccupationLabels);
        var (maritalStatus, maritalStatusLine) = ReadMaritalStatus(lines);
        var expiryText = ReadLabeledValue(lines, ExpiryDateLabels);

        if (!IsTextValue(occupation, maxLength: 200))
        {
            occupation = ReadUnlabelledOccupation(lines, maritalStatusLine);
        }

        return new ArabicBackCardFields(
            IsTextValue(occupation, maxLength: 200) ? occupation : null,
            maritalStatus,
            ParseDate(expiryText) ?? ReadUniqueDate(lines));
    }

    private static (string? Value, int LineIndex) ReadMaritalStatus(string[] lines)
    {
        var matches = lines
            .SelectMany((line, index) => FindMaritalStatuses(line).Select(value => (Value: value, Index: index)))
            .ToArray();
        var values = matches.Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();

        return values.Length == 1
            ? (values[0], matches[0].Index)
            : (null, -1);
    }

    private static IEnumerable<string> FindMaritalStatuses(string line)
    {
        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = NormalizeArabicToken(token);
            if (MaritalStatuses.TryGetValue(normalized, out var value))
            {
                yield return value;
            }
        }
    }

    private static string? ReadUnlabelledOccupation(string[] lines, int maritalStatusLine)
    {
        if (maritalStatusLine <= 0)
        {
            return null;
        }

        var precedingLines = lines
            .Take(maritalStatusLine)
            .TakeLast(3)
            .ToArray();
        var occupationLines = precedingLines
            .Reverse()
            .TakeWhile(IsUnlabelledOccupationLine)
            .Reverse()
            .ToArray();

        return occupationLines.Length == 0
            ? null
            : string.Join(' ', occupationLines);
    }

    private static bool IsUnlabelledOccupationLine(string line)
    {
        return IsTextValue(line, maxLength: 200)
            && line.Count(char.IsDigit) <= line.Length * 0.1
            && !AllLabels.Any(label => line.Contains(label, StringComparison.Ordinal));
    }

    private static string NormalizeArabicToken(string token)
    {
        var result = new StringBuilder(token.Length);
        foreach (var character in token)
        {
            var normalized = character switch
            {
                'أ' or 'إ' or 'آ' => 'ا',
                'ى' => 'ي',
                _ => character
            };

            if (char.IsLetter(normalized))
            {
                result.Append(normalized);
            }
        }

        return result.ToString();
    }

    private static string? ReadLabeledValue(string[] lines, string[] labels)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            foreach (var label in labels.OrderByDescending(value => value.Length))
            {
                var match = lines[index].IndexOf(label, StringComparison.Ordinal);
                if (match < 0)
                {
                    continue;
                }

                var afterLabel = Clean(lines[index][(match + label.Length)..].Trim(' ', ':', '-', '.', '،', '|'));
                if (afterLabel.Length > 0)
                {
                    return afterLabel;
                }

                for (var next = index + 1; next < lines.Length; next++)
                {
                    if (AllLabels.Any(candidate => lines[next].Contains(candidate, StringComparison.Ordinal)))
                    {
                        return null;
                    }

                    if (lines[next].Length > 0)
                    {
                        return lines[next];
                    }
                }

                return null;
            }
        }

        return null;
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var asciiDigits = NormalizeDigits(value);
        var candidates = asciiDigits.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var candidate in candidates)
        {
            foreach (var format in new[]
            {
                "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy",
                "yyyy/M/d", "yyyy/MM/dd", "yyyy-M-d", "yyyy-MM-dd"
            })
            {
                if (DateOnly.TryParseExact(
                    candidate,
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
                {
                    return date;
                }
            }
        }

        return null;
    }

    private static DateOnly? ReadUniqueDate(string[] lines)
    {
        var dates = lines
            .Select(ParseDate)
            .Where(date => date.HasValue)
            .Select(date => date.GetValueOrDefault())
            .Distinct()
            .Take(2)
            .ToArray();

        return dates.Length == 1 ? dates[0] : null;
    }

    private static string NormalizeDigits(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is >= '\u0660' and <= '\u0669')
            {
                result.Append((char)('0' + character - '\u0660'));
            }
            else if (character is >= '\u06F0' and <= '\u06F9')
            {
                result.Append((char)('0' + character - '\u06F0'));
            }
            else
            {
                result.Append(character);
            }
        }

        return result.ToString();
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

            if (character is >= '\u064B' and <= '\u0652')
            {
                continue;
            }

            if (char.IsControl(character) && character != '\t')
            {
                continue;
            }

            builder.Append(char.IsWhiteSpace(character) ? ' ' : character);
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Trim(' ', ':', '-', '.', '،', '|');
    }

    private static bool IsTextValue(string? value, int maxLength)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= maxLength
            && value.Any(char.IsLetter);
    }
}
