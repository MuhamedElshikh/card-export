using System.Runtime.InteropServices;
using IdCardReader.Infrastructure.Imaging;
using PdfNetStack.Drawing;
using PdfNetStack.Ocr;
using SkiaSharp;

namespace IdCardReader.Infrastructure;

internal sealed class PaddleOcrNumberReader : IDisposable
{
    private const double MinimumConfidence = 0.9;
    private const double MinimumTextConfidence = 0.75;
    private static readonly int[] OrientationOrder = { 0, 2, 1, 3 };
    private static readonly string[] Languages = { "latin", "arabic" };

    private readonly Lazy<OnnxOcrEngine> _engine = new(
        () => OnnxOcrEngine.Load(new OnnxOcrEngineOptions { Languages = Languages }));

    // The ONNX engine is not thread safe, and the parallel scan lanes can reach both readers.
    private readonly object _engineLock = new();

    public PaddleOcrNumberRead? Read(SKBitmap bitmap, CancellationToken cancellationToken)
    {
        lock (_engineLock)
        {
            return ReadCore(bitmap, cancellationToken);
        }
    }

    private PaddleOcrNumberRead? ReadCore(SKBitmap bitmap, CancellationToken cancellationToken)
    {
        var candidates = new List<PaddleOcrNumberRead>();

        foreach (var language in Languages)
        {
            foreach (var turns in OrientationOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var oriented = ImagePreprocessor.RotateQuarterTurns(bitmap, turns);
                var result = _engine.Value.Recognize(
                    ToOcrBitmap(oriented),
                    new OcrRequest { Language = language },
                    cancellationToken);

                foreach (var line in result.Lines)
                {
                    if (line.Confidence < MinimumConfidence)
                    {
                        continue;
                    }

                    candidates.AddRange(DigitCandidates.Extract(
                            line.Text,
                            $"paddle-{language}-{turns}",
                            direct: true)
                        .Select(candidate => new PaddleOcrNumberRead(
                            candidate.Value,
                            turns,
                            line.Confidence)));
                }
            }
        }

        return SelectUniqueCandidate(candidates);
    }

    /// <summary>
    /// Reads the card front with the PP-OCRv5 Arabic model and returns every detected line with
    /// its vertical position and confidence. Unlike Tesseract's page segmentation, the detector
    /// finds the text lines on the card's patterned background; selection and garbage rejection
    /// happen in <see cref="Imaging.ArabicTextFields"/>.
    /// </summary>
    public IReadOnlyList<PaddleTextLine> ReadArabicLines(SKBitmap bitmap, CancellationToken cancellationToken)
    {
        lock (_engineLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _engine.Value.Recognize(
                ToOcrBitmap(bitmap),
                new OcrRequest { Language = "arabic" },
                cancellationToken);

            return result.Lines
                .Select(line => new PaddleTextLine(
                    line.Text,
                    line.Quad.Min(point => point.Y),
                    line.Confidence))
                .ToArray();
        }
    }

    public string ReadArabicText(SKBitmap bitmap, CancellationToken cancellationToken)
    {
        lock (_engineLock)
        {
            return ReadArabicTextCore(bitmap, cancellationToken);
        }
    }

    private string ReadArabicTextCore(SKBitmap bitmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _engine.Value.Recognize(
            ToOcrBitmap(bitmap),
            new OcrRequest { Language = "arabic" },
            cancellationToken);

        return string.Join(
            '\n',
            result.Lines
                .Where(line => line.Confidence >= MinimumTextConfidence)
                .Select(line => line.Text));
    }

    internal static PaddleOcrNumberRead? SelectUniqueCandidate(
        IEnumerable<PaddleOcrNumberRead> candidates)
    {
        var candidateArray = candidates.ToArray();
        var values = candidateArray
            .Select(candidate => candidate.Value)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        return values.Length == 1
            ? candidateArray
                .Where(candidate => candidate.Value == values[0])
                .MaxBy(candidate => candidate.Confidence)
            : null;
    }

    private static OcrBitmap ToOcrBitmap(SKBitmap bitmap)
    {
        using var grayscale = ImagePreprocessor.ToGrayscale(bitmap);
        var pixelAddress = grayscale.GetPixels();
        if (pixelAddress == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not access the grayscale image pixels for PaddleOCR.");
        }

        var pixels = new byte[grayscale.Width * grayscale.Height];
        for (var y = 0; y < grayscale.Height; y++)
        {
            Marshal.Copy(
                IntPtr.Add(pixelAddress, y * grayscale.RowBytes),
                pixels,
                y * grayscale.Width,
                grayscale.Width);
        }

        return new OcrBitmap(grayscale.Width, grayscale.Height, pixels);
    }

    public void Dispose()
    {
        if (_engine.IsValueCreated)
        {
            _engine.Value.Dispose();
        }
    }
}

internal sealed record PaddleOcrNumberRead(string Value, int QuarterTurns, double Confidence);

/// <summary>One Arabic text line read by PP-OCRv5: the text, its top edge and the read confidence.</summary>
internal sealed record PaddleTextLine(string Text, double Top, double Confidence);
