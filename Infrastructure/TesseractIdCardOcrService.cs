using IdCardReader.Application;
using IdCardReader.Domain;
using IdCardReader.Infrastructure.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Tesseract;

namespace IdCardReader.Infrastructure;

/// <summary>
/// Offline Tesseract OCR for Egyptian national ID cards.
/// <para>
/// The pipeline is deliberately cascaded so a normal, upright photo is cheap:
/// </para>
/// <list type="number">
/// <item>decode + EXIF orientation, downscale, grayscale, contrast stretch;</item>
/// <item>estimate and remove the small rotation of the text lines (deskew);</item>
/// <item>binarise with a local mean threshold (tolerates shadows and uneven light);</item>
/// <item>
/// read digits from the upright orientation with the English model and the Arabic model;
/// stop as soon as both agree, otherwise try 180°, 90° and 270°, then a global Otsu threshold;
/// </item>
/// <item>read the name and address once, at higher resolution, on the winning orientation.</item>
/// </list>
/// Name and address remain best effort and are always reviewed by the user.
/// </summary>
public sealed class TesseractIdCardOcrService : IIdCardOcrService, IDisposable
{
    private const string WesternDigits = "0123456789";

    private const string AllDigits =
        "0123456789\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669\u06F0\u06F1\u06F2\u06F3\u06F4\u06F5\u06F6\u06F7\u06F8\u06F9";

    // Upright first, then upside down (very common with phone cameras), then the sideways cases.
    private static readonly int[] OrientationOrder = { 0, 2, 1, 3 };

    private readonly TesseractOptions _options;
    private readonly ILogger<TesseractIdCardOcrService> _logger;
    private readonly SemaphoreSlim _scanSemaphore;
    private readonly TesseractEnginePool _arabicPool;
    private readonly TesseractEnginePool _englishPool;

    /// <summary>Creates the service.</summary>
    public TesseractIdCardOcrService(
        IOptions<TesseractOptions> options,
        ILogger<TesseractIdCardOcrService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger ?? NullLogger<TesseractIdCardOcrService>.Instance;

        var tessdataPath = string.IsNullOrWhiteSpace(_options.TessdataPath)
            ? Path.Combine(AppContext.BaseDirectory, "tessdata")
            : _options.TessdataPath;

        _arabicPool = new TesseractEnginePool(tessdataPath, "ara");
        if (!_arabicPool.IsAvailable)
        {
            throw new InvalidOperationException(
                $"The Arabic OCR model was not found in '{tessdataPath}'. The deployment is incomplete.");
        }

        _englishPool = new TesseractEnginePool(tessdataPath, "eng");

        var concurrency = Math.Max(1, _options.MaxConcurrentScans);
        _scanSemaphore = new SemaphoreSlim(concurrency, concurrency);
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
        SKBitmap? decoded;
        try
        {
            decoded = ImagePreprocessor.DecodeWithOrientation(imageBytes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ImageDecodeException("The uploaded image could not be decoded.", exception);
        }

        if (decoded is null)
        {
            throw new ImageDecodeException("The uploaded image could not be decoded.");
        }

        using (decoded)
        {
            var workDirectory = Path.Combine(Path.GetTempPath(), $"idcard-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workDirectory);

            try
            {
                var number = ReadNationalId(decoded, workDirectory, cancellationToken);
                var (name, address) = ReadText(decoded, workDirectory, number.QuarterTurns, number.Skew, cancellationToken);

                return BuildResult(number, name, address);
            }
            finally
            {
                TryDeleteDirectory(workDirectory);
            }
        }
    }

    private NumberRead ReadNationalId(SKBitmap decoded, string workDirectory, CancellationToken cancellationToken)
    {
        using var small = ImagePreprocessor.ResizeToLongEdge(
            decoded,
            _options.NumberLongEdge,
            minimumLongEdge: 900);
        using var grayscale = ImagePreprocessor.StretchContrast(ImagePreprocessor.ToGrayscale(small));

        var skew = 0.0;
        if (_options.EnablePreprocessing && _options.EnableDeskew)
        {
            using var probe = ImagePreprocessor.ResizeToLongEdge(grayscale, 420);
            skew = ImagePreprocessor.EstimateSkewDegrees(probe);
        }

        using var deskewed = skew == 0
            ? grayscale.Copy(SKColorType.Gray8)
            : ImagePreprocessor.Rotate(grayscale, skew);

        var votes = new List<DigitCandidates.Candidate>();
        var byOrientation = new Dictionary<int, List<DigitCandidates.Candidate>>();
        var passes = 0;

        if (!_options.EnablePreprocessing)
        {
            var plain = CollectDigits(deskewed, 0, workDirectory, ref passes, cancellationToken);
            votes.AddRange(plain);
            byOrientation[0] = plain;
        }
        else
        {
            using (var binarized = ImagePreprocessor.BinarizeLocalMean(deskewed))
            {
                foreach (var turns in OrientationOrder)
                {
                    var read = CollectDigits(binarized, turns, workDirectory, ref passes, cancellationToken);
                    votes.AddRange(read);
                    byOrientation[turns] = read;

                    if (DigitCandidates.HasStrongWinner(votes))
                    {
                        break;
                    }
                }
            }

            if (!DigitCandidates.HasStrongWinner(votes))
            {
                // Second opinion with a global threshold. Upright orientations only, to stay in budget.
                using var otsu = ImagePreprocessor.BinarizeOtsu(deskewed);
                foreach (var turns in new[] { 0, 2 })
                {
                    var read = CollectDigits(otsu, turns, workDirectory, ref passes, cancellationToken);
                    votes.AddRange(read);

                    if (byOrientation.TryGetValue(turns, out var existing))
                    {
                        existing.AddRange(read);
                    }
                    else
                    {
                        byOrientation[turns] = read;
                    }

                    if (DigitCandidates.HasStrongWinner(votes))
                    {
                        break;
                    }
                }
            }
        }

        var vote = DigitCandidates.Vote(votes);
        var rotation = ResolveRotation(vote.Value, byOrientation);

        _logger.LogInformation(
            "OCR number pass finished. Votes={Votes} WinnerShare={Share:F2} DirectRead={DirectRead} " +
            "Rotation={Rotation} Skew={Skew:F1} Passes={Passes}",
            vote.Total,
            vote.Share,
            vote.HasDirectRead,
            rotation,
            skew,
            passes);

        return new NumberRead(vote.Value, vote.LowConfidence, rotation, skew);
    }

    private List<DigitCandidates.Candidate> CollectDigits(
        SKBitmap bitmap,
        int quarterTurns,
        string workDirectory,
        ref int passes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = new List<DigitCandidates.Candidate>();

        using var rotated = ImagePreprocessor.RotateQuarterTurns(bitmap, quarterTurns);
        var path = Path.Combine(workDirectory, $"number-{quarterTurns}-{Guid.NewGuid():N}.png");
        ImagePreprocessor.SavePng(rotated, path);

        // The English model is markedly better at plain digits; the Arabic model also catches
        // Arabic-Indic digits, which some printers use for the number.
        if (_englishPool.Rent() is { } english)
        {
            using (english)
            {
                var text = RunPass(english.Engine, path, PageSegMode.SparseText, WesternDigits);
                candidates.AddRange(DigitCandidates.Extract(text, $"eng-{quarterTurns}", direct: true));
                passes++;
            }
        }

        using (var arabic = _arabicPool.Rent())
        {
            if (arabic is null)
            {
                return candidates;
            }

            var text = RunPass(arabic.Engine, path, PageSegMode.SparseText, AllDigits);
            candidates.AddRange(DigitCandidates.Extract(text, $"ara-{quarterTurns}", direct: true));
            passes++;
        }

        return candidates;
    }

    private (string? Name, string? Address) ReadText(
        SKBitmap decoded,
        string workDirectory,
        int quarterTurns,
        double skew,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var medium = ImagePreprocessor.ResizeToLongEdge(
            decoded,
            _options.TextLongEdge,
            minimumLongEdge: 1200);
        using var grayscale = ImagePreprocessor.StretchContrast(ImagePreprocessor.ToGrayscale(medium));
        using var oriented = quarterTurns == 0
            ? grayscale.Copy(SKColorType.Gray8)
            : ImagePreprocessor.RotateQuarterTurns(grayscale, quarterTurns);
        using var deskewed = skew == 0
            ? oriented.Copy(SKColorType.Gray8)
            : ImagePreprocessor.Rotate(oriented, skew);

        var path = Path.Combine(workDirectory, "text.png");
        ImagePreprocessor.SavePng(deskewed, path);

        string text;
        using (var lease = _arabicPool.Rent())
        {
            if (lease is null)
            {
                return (null, null);
            }

            text = RunPass(lease.Engine, path, PageSegMode.Auto, whitelist: null);
        }

        var (name, address) = ArabicTextFields.Select(text);

        if (name is null && address is null)
        {
            // Layout analysis failed to produce usable lines; try a sparse scan before giving up.
            using var lease = _arabicPool.Rent();
            if (lease is not null)
            {
                var sparse = RunPass(lease.Engine, path, PageSegMode.SparseText, whitelist: null);
                (name, address) = ArabicTextFields.Select(sparse);
            }
        }

        return (name, address);
    }

    private static string RunPass(
        TesseractEngine engine,
        string imagePath,
        PageSegMode pageSegMode,
        string? whitelist)
    {
        engine.SetVariable("tessedit_char_whitelist", whitelist ?? string.Empty);

        using var pix = Pix.LoadFromFile(imagePath);
        using var page = engine.Process(pix, pageSegMode);

        return page.GetText() ?? string.Empty;
    }

    private static int ResolveRotation(
        string? value,
        IReadOnlyDictionary<int, List<DigitCandidates.Candidate>> byOrientation)
    {
        if (value is null)
        {
            return 0;
        }

        foreach (var turns in OrientationOrder)
        {
            if (byOrientation.TryGetValue(turns, out var candidates)
                && candidates.Any(candidate => candidate.Value == value && candidate.Direct))
            {
                return turns;
            }
        }

        foreach (var turns in OrientationOrder)
        {
            if (byOrientation.TryGetValue(turns, out var candidates)
                && candidates.Any(candidate => candidate.Value == value))
            {
                return turns;
            }
        }

        return 0;
    }

    private static IdCardScanResult BuildResult(NumberRead number, string? name, string? address)
    {
        var result = new IdCardScanResult
        {
            NationalId = number.Value,
            FullName = name,
            Address = address,
            NationalIdValid = false,
            LowConfidence = number.LowConfidence
        };

        if (number.Value is null || NationalIdNumber.TryCreate(number.Value) is not { } parsed)
        {
            return result;
        }

        var derived = NationalIdResult.FromParsedNumber(parsed, name, address);

        result.NationalId = derived.NationalIdNumber;
        result.NationalIdValid = true;
        result.BirthDate = derived.BirthDate;
        result.Gender = derived.Gender;
        result.Governorate = derived.Governorate;
        result.GovernorateEnglish = derived.GovernorateEnglish;
        result.GovernorateArabic = derived.GovernorateArabic;

        return result;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Temp cleanup is best effort.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _scanSemaphore.Dispose();
        _arabicPool.Dispose();
        _englishPool.Dispose();
    }

    private sealed record NumberRead(string? Value, bool LowConfidence, int QuarterTurns, double Skew);
}
