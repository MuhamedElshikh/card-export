using System.Text;
using IdCardReader.Domain;

namespace IdCardReader.Infrastructure.Imaging;

/// <summary>
/// Extracts national ID candidates from raw OCR text and votes among them.
/// Deliberately free of any OCR or imaging dependency so the rules can be unit tested.
/// A candidate is only accepted when one OCR line contains exactly the 14 printed digits:
/// sliding windows and joined lines are arithmetic coincidences, not readings.
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
    /// True when the digits formed exactly one 14-digit run on one OCR line. False is retained for
    /// diagnostics of legacy results; the current extractor never emits such candidates.
    /// </param>
    /// <param name="Source">Diagnostic label of the pass that produced the reading.</param>
    public readonly record struct Candidate(string Value, bool Direct, string Source);

    /// <summary>Outcome of the majority vote.</summary>
    /// <param name="Value">Winning value, or null when nothing was read.</param>
    /// <param name="Share">Winner votes divided by all votes.</param>
    /// <param name="HasDirectRead">Whether the winner was read directly at least once.</param>
    /// <param name="DirectVotes">How many independent passes read the winning value directly.</param>
    /// <param name="Total">Total number of votes.</param>
    public readonly record struct VoteResult(
        string? Value,
        double Share,
        bool HasDirectRead,
        int DirectVotes,
        int Total)
    {
        /// <summary>
        /// A single reading is never enough to trust a number that generates three other fields.
        /// </summary>
        public bool LowConfidence =>
            Value is null || Share < MinimumVoteShare || !HasDirectRead || DirectVotes < 2;
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
    /// Pulls valid national ID candidates out of OCR text. Spaces between printed digit groups are
    /// ignored, but nothing is taken from a longer digit run or from another line.
    /// </summary>
    public static IEnumerable<Candidate> Extract(string? text, string source, bool direct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var rawLine in NormalizeDigits(text).Split('\n'))
        {
            var digits = KeepDigits(rawLine);
            if (digits.Length != NationalIdLength || !IsValidNationalId(digits))
            {
                continue;
            }

            yield return new Candidate(digits, direct, source);
        }
    }

    /// <summary>Votes among all candidates and returns the winner with its support.</summary>
    public static VoteResult Vote(IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var list = candidates as IReadOnlyList<Candidate> ?? candidates.ToList();
        if (list.Count == 0)
        {
            return new VoteResult(null, 0, false, 0, 0);
        }

        var winner = list
            .GroupBy(candidate => candidate.Value)
            .Select(group => new
            {
                Value = group.Key,
                Votes = group.Count(),
                DirectVotes = group.Count(candidate => candidate.Direct)
            })
            .OrderByDescending(group => group.DirectVotes)
            .ThenByDescending(group => group.Votes)
            .First();

        return new VoteResult(
            winner.Value,
            winner.Votes / (double)list.Count,
            winner.DirectVotes > 0,
            winner.DirectVotes,
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
            .Any(group => group.Count(candidate => candidate.Direct) >= 2);
    }

    /// <summary>Value of the strong winner, if any.</summary>
    public static string? StrongWinnerValue(IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var winner = candidates
            .GroupBy(candidate => candidate.Value)
            .Where(group => group.Count(candidate => candidate.Direct) >= 2)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();

        return winner?.Key;
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
