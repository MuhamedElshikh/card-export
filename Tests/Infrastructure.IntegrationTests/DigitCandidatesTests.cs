using System.Text;
using IdCardReader.Infrastructure.Imaging;

namespace IdCardReader.Infrastructure.IntegrationTests;

/// <summary>
/// The candidate rules decide whether a number read off a photo is trusted, so they are tested
/// directly with no OCR engine involved.
/// </summary>
public sealed class DigitCandidatesTests
{
    private const string ValidId = "29001011234567";

    private const string OtherValidId = "30101011234571";

    [Theory]
    [InlineData("\u0662\u0669\u0660\u0660\u0661\u0660\u0661\u0661\u0662\u0663\u0664\u0665\u0666\u0667")]
    [InlineData("\u06F2\u06F9\u06F0\u06F0\u06F1\u06F0\u06F1\u06F1\u06F2\u06F3\u06F4\u06F5\u06F6\u06F7")]
    public void NormalizeDigits_ConvertsArabicIndicAndPersianDigits(string raw)
    {
        Assert.Equal(ValidId, DigitCandidates.NormalizeDigits(raw));
    }

    [Fact]
    public void Extract_ReadsNumberPrintedInGroups()
    {
        // Egyptian cards print the number in groups; the spaces must not break the read.
        var candidates = DigitCandidates.Extract("2 90 01 01 12 3456 7", "pass", direct: true).ToList();

        var candidate = Assert.Single(candidates);
        Assert.Equal(ValidId, candidate.Value);
        Assert.True(candidate.Direct);
    }

    [Fact]
    public void Extract_ReadsArabicIndicNumber()
    {
        var arabic = new string(ValidId.Select(character => (char)('\u0660' + (character - '0'))).ToArray());

        var candidates = DigitCandidates.Extract($"الرقم القومى {arabic}", "pass", direct: true).ToList();

        var candidate = Assert.Single(candidates);
        Assert.Equal(ValidId, candidate.Value);
        Assert.True(candidate.Direct);
    }

    [Fact]
    public void Extract_ProducesNoCandidatesForInvalidNumbers()
    {
        // Month 13 and a birth date in the future are both rejected by the domain parser.
        Assert.Empty(DigitCandidates.Extract("29013011234567", "pass", direct: true));
        Assert.Empty(DigitCandidates.Extract("39901011234567", "pass", direct: true));
    }

    [Fact]
    public void Extract_NeverInventsANumberFromSlidingWindows()
    {
        // A 14-digit window that happens to pass date/governorate checks is still not a reading.
        Assert.Empty(DigitCandidates.Extract("990012900101123456789", "pass", direct: true));
    }

    [Fact]
    public void Extract_NeverJoinsDigitsFromDifferentLines()
    {
        var candidates = DigitCandidates.Extract($"2900101\n1234567\n{OtherValidId}", "pass", direct: true).ToList();

        // Only the complete line is a reading; the two halves are never glued together.
        var candidate = Assert.Single(candidates);
        Assert.Equal(OtherValidId, candidate.Value);
        Assert.True(candidate.Direct);
    }

    [Fact]
    public void Vote_OneDirectReadIsNotConfidentEnough()
    {
        var candidates = new[] { new DigitCandidates.Candidate(ValidId, true, "eng") };

        var vote = DigitCandidates.Vote(candidates);

        Assert.Equal(ValidId, vote.Value);
        Assert.True(vote.LowConfidence);
        Assert.False(DigitCandidates.HasStrongWinner(candidates));
    }

    [Fact]
    public void Vote_TwoAgreeingPassesAreConfident()
    {
        var candidates = new[]
        {
            new DigitCandidates.Candidate(ValidId, true, "eng"),
            new DigitCandidates.Candidate(ValidId, true, "ara")
        };

        var vote = DigitCandidates.Vote(candidates);

        Assert.Equal(ValidId, vote.Value);
        Assert.Equal(1.0, vote.Share);
        Assert.False(vote.LowConfidence);
        Assert.True(DigitCandidates.HasStrongWinner(candidates));
    }

    [Fact]
    public void Vote_MajorityWinsButMinorityShareIsFlagged()
    {
        var candidates = new[]
        {
            new DigitCandidates.Candidate(ValidId, true, "eng"),
            new DigitCandidates.Candidate(OtherValidId, true, "ara"),
            new DigitCandidates.Candidate(OtherValidId, true, "ara-2"),
            new DigitCandidates.Candidate(OtherValidId, true, "eng-2")
        };

        var vote = DigitCandidates.Vote(candidates);

        Assert.Equal(OtherValidId, vote.Value);
        Assert.Equal(0.75, vote.Share, 3);
        Assert.False(vote.LowConfidence);
    }

    [Fact]
    public void Vote_SingleVoteOutOfThreeIsLowConfidence()
    {
        var candidates = new[]
        {
            new DigitCandidates.Candidate(ValidId, true, "eng"),
            new DigitCandidates.Candidate(OtherValidId, true, "ara"),
            new DigitCandidates.Candidate("29001011234568", true, "ara-2")
        };

        var vote = DigitCandidates.Vote(candidates);

        Assert.Equal(ValidId, vote.Value);
        Assert.Equal(1.0 / 3.0, vote.Share, 3);
        Assert.True(vote.LowConfidence);
    }

    [Fact]
    public void Vote_FallbackOnlyWinnerIsLowConfidence()
    {
        var candidates = new[]
        {
            new DigitCandidates.Candidate(ValidId, false, "eng/document"),
            new DigitCandidates.Candidate(ValidId, false, "ara/document")
        };

        var vote = DigitCandidates.Vote(candidates);

        Assert.Equal(ValidId, vote.Value);
        Assert.True(vote.LowConfidence);
        Assert.False(DigitCandidates.HasStrongWinner(candidates));
    }

    [Fact]
    public void Vote_NoCandidatesIsLowConfidence()
    {
        var vote = DigitCandidates.Vote(Array.Empty<DigitCandidates.Candidate>());

        Assert.Null(vote.Value);
        Assert.True(vote.LowConfidence);
    }

    [Fact]
    public void Extract_HandlesEmptyText()
    {
        Assert.Empty(DigitCandidates.Extract(null, "pass", direct: true));
        Assert.Empty(DigitCandidates.Extract("   \n\t ", "pass", direct: true));
    }

    [Fact]
    public void Extract_IgnoresShortDigitRuns()
    {
        var text = new StringBuilder("رقم 12345 و 6789 و 0123456789").ToString();

        Assert.Empty(DigitCandidates.Extract(text, "pass", direct: true));
    }
}
