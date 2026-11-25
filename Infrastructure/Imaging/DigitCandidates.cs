using System.Text;
using IdCardReader.Domain;

namespace IdCardReader.Infrastructure.Imaging;

/// <summary>
/// Extracts national ID candidates from raw OCR text and votes among them.
/// Deliberately free of any OCR or imaging dependency so the rules can be unit tested.
/// </summary>
public static class DigitCandidates
{
    /// <summary>Length of an Egyptian national ID number.</summary>
    public const int NationalIdLength = 14;

    /// <summary>A candidate must reach this share of the votes to be trusted.</summary>
    public const double MinimumVoteShare = 0.6;

    /// <summary>One reading of a 14 digit number produced by one OCR pass.</summary>
    /// <param name="Value">The normalized 14 digit value.</param>
    /// <param name="Direct">
    /// True when the digits formed a clean single run on one line (the expected layout of a card
    /// number). False when the value came from a sliding window or from joining separate lines.
    /// </param>
    /// <param name="Source">Diagnostic label of the pass that produced the reading.</param>
    public readonly record struct Candidate(string Value, bool Direct, string Source);

    /// <summary>Outcome of the majority vote.</summary>
    /// <param name="Value">Winning value, or null when nothing was read.</param>
    /// <param name="Share">Winner votes divided by all votes.</param>
    /// <param name="HasDirectRead">Whether the winner was read directly at least once.</param>
    /// <param name="Total">Total number of votes.</param>
    public readonly record struct VoteResult(string? Value, double Share, bool HasDirectRead, int Total)
    {
        /// <summary>Spec rule: low confidence when the winner is weak or only found by fallback.</summary>
        public bool LowConfidence => Value is null || Share < MinimumVoteShare || !HasDirectRead;
    }

    /// <summary>
    /// Converts Arabic-Indic (٠-٩) and Persian (۰-۹) digits to ASCII and leaves the rest untouched.
    /// </summary>
    public static string NormalizeDigits(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(character switch
            {
                >= '\u0660' and <= '\u0669' => (char)('0' + (character - '\u0660')),
                >= '\u06F0' and <= '\u06F9' => (char)('0' + (character - '\u06F0')),
                _ => character
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// Pulls valid national ID candidates out of OCR text.
    /// Spaces and punctuation between digits are ignored (cards print the number in groups),
    /// and a document wide fallback joins lines when the number was split by the OCR layout.
    /// </summary>
    public static IEnumerable<Candidate> Extract(string? text, string source, bool direct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var normalized = NormalizeDigits(text);
        var lineCandidates = new List<Candidate>();
        var documentDigits = new StringBuilder();

        foreach (var rawLine in normalized.Split('\n'))
        {
            var digits = KeepDigits(rawLine);
            if (digits.Length == 0)
            {
                continue;
            }

            documentDigits.Append(digits);

            if (digits.Length >= NationalIdLength)
            {
                lineCandidates.AddRange(Windows(digits, source + "/line", direct));
            }
        }

        foreach (var candidate in lineCandidates)
        {
            yield return candidate;
        }

        // Fallback: the number was broken across lines, so only the joined stream makes sense.
        if (lineCandidates.Count == 0 && documentDigits.Length >= NationalIdLength)
        {
            foreach (var candidate in Windows(documentDigits.ToString(), source + "/document", direct: false))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>Votes among all candidates and returns the winner with its support.</summary>
    public static VoteResult Vote(IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var list = candidates as IReadOnlyList<Candidate> ?? candidates.ToList();
        if (list.Count == 0)
        {
            return new VoteResult(null, 0, false, 0);
        }

        var winner = list
            .GroupBy(candidate => candidate.Value)
            .Select(group => new
            {
                Value = group.Key,
                Votes = group.Count(),
                HasDirectRead = group.Any(candidate => candidate.Direct)
            })
            .OrderByDescending(group => group.Votes)
            .ThenByDescending(group => group.HasDirectRead)
            .First();

        return new VoteResult(
            winner.Value,
            winner.Votes / (double)list.Count,
            winner.HasDirectRead,
            list.Count);
    }

    /// <summary>
    /// True when a value was read directly and independently by at least two passes, which is a
    /// good enough reason to stop searching other rotations.
    /// </summary>
    public static bool HasStrongWinner(IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .GroupBy(candidate => candidate.Value)
            .Any(group => group.Count() >= 2 && group.Any(candidate => candidate.Direct));
    }

    /// <summary>Value of the strong winner, if any.</summary>
    public static string? StrongWinnerValue(IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var winner = candidates
            .GroupBy(candidate => candidate.Value)
            .Where(group => group.Count() >= 2 && group.Any(candidate => candidate.Direct))
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();

        return winner?.Key;
    }

    private static IEnumerable<Candidate> Windows(string digits, string source, bool direct)
    {
        if (digits.Length == NationalIdLength)
        {
            if (IsValidNationalId(digits))
            {
                yield return new Candidate(digits, direct, source);
            }

            yield break;
        }

        for (var index = 0; index <= digits.Length - NationalIdLength; index++)
        {
            var window = digits.Substring(index, NationalIdLength);
            if (IsValidNationalId(window))
            {
                // A window inside a longer noise run is never a confident reading.
                yield return new Candidate(window, false, source);
            }
        }
    }

    private static string KeepDigits(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is >= '0' and <= '9')
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool IsValidNationalId(string digits)
    {
        try
        {
            _ = NationalIdNumber.Create(digits);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
