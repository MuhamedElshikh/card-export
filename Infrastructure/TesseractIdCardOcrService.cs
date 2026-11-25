using System.Text;
using IdCardReader.Application;
using IdCardReader.Domain;
using Tesseract;

namespace IdCardReader.Infrastructure;

/// <summary>
/// Tesseract based OCR for Egyptian national ID cards (Arabic LSTM model, fully offline).
/// The number is read with three passes (free text + two digit-restricted passes) and the
/// candidates that pass domain validation are combined with a majority vote.
/// The named/address fields are best effort and are always reviewed by the user.
/// </summary>
public sealed class TesseractIdCardOcrService : IIdCardOcrService
{
    private const string DigitWhitelist = "0123456789\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669\u06F0\u06F1\u06F2\u06F3\u06F4\u06F5\u06F6\u06F7\u06F8\u06F9";
    private const double MinimumVoteShare = 0.6;
    private const double MinimumTextConfidence = 0.6;

    private readonly string _tessdataPath;
    private readonly SemaphoreSlim _scanSemaphore;

    /// <summary>Creates the service.</summary>
    /// <param name="tessdataPath">Directory containing <c>ara.traineddata</c>.</param>
    /// <param name="maxConcurrentScans">Maximum concurrent scans; keep at 1 on dual core machines.</param>
    public TesseractIdCardOcrService(string tessdataPath, int maxConcurrentScans = 1)
    {
        if (string.IsNullOrWhiteSpace(tessdataPath))
        {
            throw new ArgumentException("A tessdata directory is required.", nameof(tessdataPath));
        }

        _tessdataPath = tessdataPath;
        _scanSemaphore = new SemaphoreSlim(Math.Max(1, maxConcurrentScans), Math.Max(1, maxConcurrentScans));
    }

    /// <inheritdoc />
    public async Task<IdCardScanResult> ScanAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        await _scanSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Tesseract is synchronous and CPU bound; keep it off the request thread.
            return await Task.Run(() => Scan(imageBytes, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _scanSemaphore.Release();
        }
    }

    private IdCardScanResult Scan(byte[] imageBytes, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"idcard-{Guid.NewGuid():N}");
        File.WriteAllBytes(tempPath, imageBytes);

        try
        {
            // The bundled ara.traineddata is LSTM only, so OEM 1 is required (OEM 3 needs legacy data).
            using var engine = new TesseractEngine(_tessdataPath, "ara", EngineMode.LstmOnly);
            engine.SetVariable("user_defined_dpi", "300");
            engine.SetVariable("tessedit_do_osd", "0");

            using var pix = Pix.LoadFromFile(tempPath);

            cancellationToken.ThrowIfCancellationRequested();
            var textPass = RunPass(engine, pix, PageSegMode.SingleBlock, whitelist: null, out var textConfidence);

            cancellationToken.ThrowIfCancellationRequested();
            var digitPassBlock = RunPass(engine, pix, PageSegMode.SingleBlock, DigitWhitelist, out _);

            cancellationToken.ThrowIfCancellationRequested();
            var digitPassSparse = RunPass(engine, pix, PageSegMode.SparseText, DigitWhitelist, out _);

            var candidates = new List<Candidate>();
            candidates.AddRange(FindCandidates(textPass, direct: false));
            candidates.AddRange(FindCandidates(digitPassBlock, direct: true));
            candidates.AddRange(FindCandidates(digitPassSparse, direct: true));

            var (nationalId, lowConfidenceVote) = Vote(candidates);

            var (name, address) = ExtractNameAndAddress(textPass);

            var result = new IdCardScanResult
            {
                NationalId = nationalId,
                FullName = name,
                Address = address,
                NationalIdValid = false,
                LowConfidence = nationalId is null || lowConfidenceVote || textConfidence < MinimumTextConfidence
            };

            if (nationalId is not null)
            {
                var parsed = NationalIdNumber.Create(nationalId);
                var derived = NationalIdResult.FromParsedNumber(parsed, name, address);

                result.NationalId = derived.NationalIdNumber;
                result.NationalIdValid = true;
                result.BirthDate = derived.BirthDate;
                result.Gender = derived.Gender;
                result.Governorate = derived.Governorate;
                result.GovernorateEnglish = derived.GovernorateEnglish;
                result.GovernorateArabic = derived.GovernorateArabic;
            }

            return result;
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
                // Temp file cleanup is best effort.
            }
        }
    }

    private static string RunPass(
        TesseractEngine engine,
        Pix pix,
        PageSegMode pageSegMode,
        string? whitelist,
        out double meanConfidence)
    {
        engine.SetVariable("tessedit_char_whitelist", whitelist ?? string.Empty);

        using var page = engine.Process(pix, pageSegMode);
        meanConfidence = page.GetMeanConfidence();

        return page.GetText() ?? string.Empty;
    }

    private static IEnumerable<Candidate> FindCandidates(string text, bool direct)
    {
        var digits = NormalizeDigits(text);

        var run = new StringBuilder();
        foreach (var character in digits)
        {
            if (character is >= '0' and <= '9')
            {
                run.Append(character);
                continue;
            }

            foreach (var candidate in Windows(run.ToString(), direct))
            {
                yield return candidate;
            }

            run.Clear();
        }

        foreach (var candidate in Windows(run.ToString(), direct))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<Candidate> Windows(string digits, bool direct)
    {
        if (digits.Length < 14)
        {
            yield break;
        }

        if (digits.Length == 14)
        {
            if (IsValidNationalId(digits))
            {
                yield return new Candidate(digits, direct);
            }

            yield break;
        }

        for (var index = 0; index <= digits.Length - 14; index++)
        {
            var window = digits.Substring(index, 14);
            if (IsValidNationalId(window))
            {
                // Sliding windows inside a longer run are treated as the fallback path.
                yield return new Candidate(window, false);
            }
        }
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

    private static (string? Value, bool LowConfidence) Vote(IReadOnlyList<Candidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return (null, true);
        }

        var winner = candidates
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

        var share = winner.Votes / (double)candidates.Count;
        var lowConfidence = share < MinimumVoteShare || !winner.HasDirectRead;

        return (winner.Value, lowConfidence);
    }

    private static (string? Name, string? Address) ExtractNameAndAddress(string text)
    {
        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(StripLabels)
            .Where(line => line.Length >= 3)
            .Where(line => line.Count(char.IsLetter) >= 2)
            .Where(line => !IsMostlyDigits(line))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (lines.Count == 0)
        {
            return (null, null);
        }

        var name = lines[0];
        var address = lines.Count > 1 ? string.Join(" ", lines.Skip(1)) : null;
        return (name, address);
    }

    private static string StripLabels(string line)
    {
        var trimmed = line.Trim();
        foreach (var label in new[] { "الاسم", "اسم", "العنوان", "عنوان", "الرقم القومى", "الرقم القومي" })
        {
            if (trimmed.StartsWith(label, StringComparison.Ordinal))
            {
                trimmed = trimmed[label.Length..];
            }
        }

        return trimmed.Trim(' ', ':', '-', '.', '\t', '\r');
    }

    private static bool IsMostlyDigits(string line)
    {
        var digits = line.Count(char.IsDigit);
        return line.Length > 0 && digits > line.Length / 2;
    }

    private static string NormalizeDigits(string text)
    {
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

    private readonly record struct Candidate(string Value, bool Direct);
}
