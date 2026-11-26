using IdCardReader.Infrastructure;

namespace IdCardReader.Infrastructure.IntegrationTests;

public sealed class PaddleOcrNumberReaderTests
{
    [Fact]
    public void SelectUniqueCandidate_RejectsConflictingLanguageCandidates()
    {
        var candidates = new[]
        {
            new PaddleOcrNumberRead("29001011234567", 0, 0.96),
            new PaddleOcrNumberRead("29001021234567", 0, 0.97)
        };

        Assert.Null(PaddleOcrNumberReader.SelectUniqueCandidate(candidates));
    }
}
