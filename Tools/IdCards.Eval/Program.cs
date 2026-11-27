using System.Diagnostics;
using System.Globalization;
using System.Text;
using IdCardReader.Application;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdCardReader.Infrastructure;
using IdCardReader.Infrastructure.Imaging;
using SkiaSharp;
using Microsoft.Extensions.Options;

return await EvalProgram.RunAsync(args);

/// <summary>
/// Offline evaluation harness.
/// It never invents sample data: point it at a folder of your own ID photos and (optionally) a
/// ground truth CSV, and it prints real per field accuracy, low confidence share, latency and memory.
/// </summary>
internal static class EvalProgram
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png" };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 1;
        }

        if (args[0] == "--export-glyphs")
        {
            return ExportGlyphs(args);
        }

        if (args[0] == "--debug-text")
        {
            return DebugText(args);
        }

        var folder = args[0];
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine($"Images folder not found: {folder}");
            return 1;
        }

        Dictionary<string, Truth> truths = new(StringComparer.OrdinalIgnoreCase);
        if (args.Length > 1)
        {
            if (!File.Exists(args[1]))
            {
                Console.Error.WriteLine($"Ground truth file not found: {args[1]}");
                return 1;
            }

            foreach (var row in ReadGroundTruth(args[1]))
            {
                truths[row.FileName] = row;
            }
        }

        var images = Directory
            .EnumerateFiles(folder)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (images.Count == 0)
        {
            Console.Error.WriteLine($"No JPG/PNG files in {folder}. Supply your own ID photos; this tool never fabricates them.");
            return 1;
        }

        var tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
        if (!Directory.Exists(tessdataPath))
        {
            Console.Error.WriteLine($"tessdata not found next to the executable: {tessdataPath}");
            return 1;
        }

        Console.WriteLine($"Images      : {images.Count} ({folder})");
        Console.WriteLine($"Ground truth: {(truths.Count == 0 ? "not supplied (accuracy not reported)" : $"{truths.Count} rows")}");
        Console.WriteLine($"Concurrency : 1 (low end profile)");
        Console.WriteLine();

        var service = new TesseractIdCardOcrService(
            Options.Create(new TesseractOptions
            {
                TessdataPath = tessdataPath,
                MaxConcurrentScans = 1
            }));

        var baseline = Process.GetCurrentProcess().WorkingSet64;
        long peakWorkingSet = baseline;

        var latencies = new List<double>(images.Count);
        var idMatches = 0;
        var nameMatches = 0;
        var addressMatches = 0;
        var comparedNames = 0;
        var comparedAddresses = 0;
        var lowConfidence = 0;
        var detected = 0;

        foreach (var image in images)
        {
            var bytes = await File.ReadAllBytesAsync(image);
            var stopwatch = Stopwatch.StartNew();
            var result = await service.ScanAsync(bytes, bytes);
            stopwatch.Stop();

            var current = Process.GetCurrentProcess();
            peakWorkingSet = Math.Max(peakWorkingSet, current.PeakWorkingSet64);

            latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
            if (result.LowConfidence)
            {
                lowConfidence++;
            }

            if (result.NationalId is not null)
            {
                detected++;
            }

            var name = Path.GetFileName(image);
            var notes = new List<string>();

            if (truths.TryGetValue(name, out var truth))
            {
                var idOk = string.Equals(result.NationalId, truth.NationalId, StringComparison.Ordinal);
                if (idOk)
                {
                    idMatches++;
                }
                else
                {
                    notes.Add($"id expected {truth.NationalId}, got {result.NationalId ?? "none"}");
                }

                if (truth.FullName is not null)
                {
                    comparedNames++;
                    if (TextMatches(result.FullName, truth.FullName))
                    {
                        nameMatches++;
                    }
                    else
                    {
                        notes.Add($"name expected '{truth.FullName}', got '{result.FullName ?? "none"}'");
                    }
                }

                if (truth.Address is not null)
                {
                    comparedAddresses++;
                    if (TextMatches(result.Address, truth.Address))
                    {
                        addressMatches++;
                    }
                    else
                    {
                        notes.Add($"address expected '{truth.Address}', got '{result.Address ?? "none"}'");
                    }
                }
            }

            var status = notes.Count == 0 ? "ok" : string.Join("; ", notes);
            Console.WriteLine(
                $"{name,-36} {stopwatch.Elapsed.TotalMilliseconds,8:F0} ms  " +
                $"lowConfidence={result.LowConfidence,-5}  {status}");
        }

        Console.WriteLine();
        Console.WriteLine("=== Results ===");
        Console.WriteLine($"National ID detected      : {detected}/{images.Count} ({Percent(detected, images.Count)})");

        if (truths.Count > 0)
        {
            Console.WriteLine($"National ID exact match   : {idMatches}/{images.Count} ({Percent(idMatches, images.Count)})");

            if (comparedNames > 0)
            {
                Console.WriteLine($"Name exact match          : {nameMatches}/{comparedNames} ({Percent(nameMatches, comparedNames)})");
            }

            if (comparedAddresses > 0)
            {
                Console.WriteLine($"Address exact match       : {addressMatches}/{comparedAddresses} ({Percent(addressMatches, comparedAddresses)})");
            }
        }

        Console.WriteLine($"Low confidence share      : {Percent(lowConfidence, images.Count)}");
        Console.WriteLine(
            $"Latency                   : avg {latencies.Average():F0} ms | p95 {Percentile(latencies, 0.95):F0} ms | " +
            $"max {latencies.Max():F0} ms");

        var baselineMb = baseline / 1024d / 1024d;
        var peakMb = peakWorkingSet / 1024d / 1024d;
        Console.WriteLine(
            $"Memory                    : peak working set {peakMb:F0} MB " +
            $"(baseline {baselineMb:F0} MB, delta {peakMb - baselineMb:F0} MB)");

        return 0;
    }

    /// <summary>
    /// Adds digit templates from one card photo whose 14 digit number you type in.
    /// Only 28x28 glyph bitmaps are written (no name, address or photo).
    /// Usage: IdCards.Eval --export-glyphs photo.jpg 29001011234567 glyph-templates.json
    /// </summary>
    private static int ExportGlyphs(string[] args)
    {
        if (args.Length != 4 || args[2].Length != 14 || !args[2].All(char.IsAsciiDigit) || !File.Exists(args[1]))
        {
            Console.Error.WriteLine("Usage: IdCards.Eval --export-glyphs <photo> <14 digit number> <templates.json>");
            return 1;
        }

        using var decoded = ImagePreprocessor.DecodeWithOrientation(File.ReadAllBytes(args[1]));
        if (decoded is null)
        {
            Console.Error.WriteLine("The image could not be decoded.");
            return 1;
        }

        using var small = ImagePreprocessor.ResizeToLongEdge(decoded, 1100, minimumLongEdge: 900);
        using var gray = ImagePreprocessor.StretchContrast(ImagePreprocessor.ToGrayscale(small));
        using var sharp = ImagePreprocessor.Sharpen(gray);
        using var binary = ImagePreprocessor.BinarizeLocalMean(sharp);

        var ink = new bool[binary.Width * binary.Height];
        var pixels = binary.GetPixelSpan();
        for (var y = 0; y < binary.Height; y++)
        {
            for (var x = 0; x < binary.Width; x++)
            {
                ink[(y * binary.Width) + x] = pixels[(y * binary.RowBytes) + x] < 128;
            }
        }

        var components = GlyphDigitReader.FindComponents(ink, binary.Width, binary.Height);
        var rows = GlyphDigitReader.FindNumberRows(components, binary.Width, binary.Height);
        if (rows.Count == 0)
        {
            Console.Error.WriteLine("No row of exactly 14 glyphs was found. Use a sharper, upright photo.");
            return 1;
        }

        Console.WriteLine($"Found {rows.Count} candidate row(s). Using the first; check the output with a test scan.");
        var row = rows[0];
        var lineHeight = row.Max(component => component.Height);

        var root = File.Exists(args[3])
            ? JsonNode.Parse(File.ReadAllText(args[3]))!.AsObject()
            : new JsonObject { ["size"] = GlyphDigitReader.GlyphSize, ["templates"] = new JsonArray() };
        var templates = root["templates"]!.AsArray();

        for (var i = 0; i < row.Count; i++)
        {
            var features = GlyphDigitReader.Features(GlyphDigitReader.ToMask(row[i], ink, binary.Width), lineHeight);
            var bytes = features.Select(value => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255)).ToArray();
            templates.Add(new JsonObject { ["d"] = args[2][i].ToString(), ["v"] = Convert.ToBase64String(bytes) });
        }

        File.WriteAllText(args[3], root.ToJsonString());
        Console.WriteLine($"Wrote {row.Count} templates to {args[3]} ({templates.Count} in total).");
        return 0;
    }

    /// <summary>
    /// Compares the name/address extraction across the pipeline settings on ONE of the user's own
    /// photos and prints a numbered row per setting. The values never leave the machine: the user
    /// reports back only which row number looks right, which pinpoints the failing stage. It also
    /// writes the cropped image next to the photo so a bad crop is visible at a glance.
    /// </summary>
    private static int DebugText(string[] args)
    {
        if (args.Length != 2 || !File.Exists(args[1]))
        {
            Console.Error.WriteLine("Usage: IdCards.Eval --debug-text <front-photo.jpg>");
            return 1;
        }

        Console.OutputEncoding = Encoding.UTF8;

        using var decoded = ImagePreprocessor.DecodeWithOrientation(File.ReadAllBytes(args[1]));
        if (decoded is null)
        {
            Console.Error.WriteLine("Could not decode the image (JPEG/PNG only).");
            return 1;
        }

        using var cropped = CardRegion.CropToCard(decoded);
        var cropPreviewPath = args[1] + ".cropped.png";
        ImagePreprocessor.SavePng(cropped, cropPreviewPath);
        Console.WriteLine($"Loaded      : {decoded.Width}x{decoded.Height}");
        Console.WriteLine($"Crop preview: {cropPreviewPath} ({cropped.Width}x{cropped.Height} of {decoded.Width}x{decoded.Height})");
        Console.WriteLine("Open it: it should show the FRONT of the card, upright, with the name and address not cut.");
        Console.WriteLine();

        var baseDir = AppContext.BaseDirectory;
        var fastTessdata = Path.Combine(baseDir, "tessdata");
        var bestTessdata = Path.Combine(baseDir, "tessdata-best");
        if (!File.Exists(Path.Combine(fastTessdata, "ara.traineddata")))
        {
            Console.Error.WriteLine($"Fast Arabic model not found: {fastTessdata}");
            return 1;
        }

        // The app learns the photo's orientation from the number search; this tool has to probe it.
        // Raw letter counts tie-break on noise (a sideways read produces stray Arabic letters too),
        // so the probe looks for words that must be on the card front and prefers 0° on a tie.
        // Pass a rotation explicitly (--debug-text photo.jpg 0|90|180|270) to skip the probe.
        using var fastEngine = CreateEngine(fastTessdata);
        var bestTurns = 0;
        if (args.Length >= 3 && int.TryParse(args[2], out var forcedDegrees))
        {
            bestTurns = ((forcedDegrees / 90) % 4 + 4) % 4;
            Console.WriteLine($"Orientation : forced to {bestTurns * 90}° by argument");
        }
        else
        {
            var bestKeywords = 0;
            var bestLetters = 0;
            var probeScores = new List<string>();
            foreach (var turns in new[] { 0, 2, 1, 3 })
            {
                var (letters, keywords) = ProbeOrientation(cropped, fastEngine, turns);
                probeScores.Add($"{turns * 90}°={letters} letters/{keywords} card-words");
                if (keywords > bestKeywords || (keywords == bestKeywords && bestKeywords == 0 && letters > bestLetters))
                {
                    bestKeywords = keywords;
                    bestLetters = letters;
                    bestTurns = turns;
                }
            }

            Console.WriteLine($"Orientation : {string.Join("  ", probeScores)} -> using {bestTurns * 90}°");
            if (bestKeywords == 0 && bestLetters < 40)
            {
                Console.WriteLine("WARNING: almost no Arabic text read at ANY rotation. Check the crop preview:");
                Console.WriteLine("is this really the FRONT of the card, and is the photo sharp?");
            }
        }

        Console.WriteLine();

        Tesseract.TesseractEngine? bestEngine = File.Exists(Path.Combine(bestTessdata, "ara.traineddata"))
            ? CreateEngine(bestTessdata)
            : null;

        var configs = new (string Label, SKBitmap Source, int LongEdge, bool Binarize, Tesseract.TesseractEngine Engine)[]
        {
            ("crop + best model + 1400 (current)", cropped, 1400, false, bestEngine ?? fastEngine),
            ("crop + fast model + 1400", cropped, 1400, false, fastEngine),
            ("crop + best model + 1800", cropped, 1800, false, bestEngine ?? fastEngine),
            ("crop + fast model + 1800", cropped, 1800, false, fastEngine),
            ("crop + best model + 1400 + binarized", cropped, 1400, true, bestEngine ?? fastEngine),
            ("no crop + fast model + 1800 (old behavior)", decoded, 1800, false, fastEngine),
        };

        var row = 0;
        foreach (var (label, source, longEdge, binarize, engine) in configs)
        {
            row++;

            using var medium = ImagePreprocessor.ResizeToLongEdge(source, longEdge, minimumLongEdge: 1200);
            using var gray = ImagePreprocessor.StretchContrast(ImagePreprocessor.ToGrayscale(medium));
            using var oriented = ImagePreprocessor.RotateQuarterTurns(gray, bestTurns);
            using var probe = ImagePreprocessor.ResizeToLongEdge(oriented, 420);
            var skew = ImagePreprocessor.EstimateSkewDegrees(probe);
            using var deskewed = skew == 0 ? oriented.Copy(SKColorType.Gray8) : ImagePreprocessor.Rotate(oriented, skew);
            using var sharpened = ImagePreprocessor.Sharpen(deskewed);
            using var binarized = binarize ? ImagePreprocessor.BinarizeLocalMean(sharpened) : null;

            // The exact image the OCR reads is saved next to the photo: open it to see what the
            // engine saw (upright? sharp? cut?).
            var rowImagePath = $"{args[1]}.row{row}.png";
            ImagePreprocessor.SavePng(binarized ?? sharpened, rowImagePath);

            var stopwatch = Stopwatch.StartNew();
            string text;
            using (var pix = Tesseract.Pix.LoadFromFile(rowImagePath))
            using (var page = engine.Process(pix, Tesseract.PageSegMode.Auto))
            {
                text = page.GetText() ?? string.Empty;
            }

            stopwatch.Stop();

            var lines = text.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line));
            var (name, address) = ArabicTextFields.Select(text);
            Console.WriteLine($"[{row}] {label} — {stopwatch.Elapsed.TotalSeconds:F1}s, {lines} lines, {CountArabicLetters(text)} letters");
            Console.WriteLine($"    image  : {rowImagePath}");
            Console.WriteLine($"    name   : {name ?? "(none)"}");
            Console.WriteLine($"    address: {address ?? "(none)"}");
            Console.WriteLine();
        }

        bestEngine?.Dispose();
        Console.WriteLine("Report back only the ROW NUMBER whose name/address look right; the values stay on your machine.");
        return 0;
    }

    private static Tesseract.TesseractEngine CreateEngine(string tessdata)
    {
        var engine = new Tesseract.TesseractEngine(tessdata, "ara", Tesseract.EngineMode.LstmOnly);
        engine.SetVariable("user_defined_dpi", "300");
        engine.SetVariable("preserve_interword_spaces", "1");
        return engine;
    }

    // Words that must appear on the front of an Egyptian national ID card. A rotation that finds
    // them is the true orientation; letter counts alone tie-break on noise.
    private static readonly string[] CardFrontWords =
        { "جمهورية", "مصر", "قومي", "الشخصية", "الرقم", "الاسم", "العنوان" };

    /// <summary>Counts Arabic letters and card-front words the fast model reads at one rotation.</summary>
    private static (int Letters, int CardWords) ProbeOrientation(
        SKBitmap source,
        Tesseract.TesseractEngine engine,
        int quarterTurns)
    {
        using var medium = ImagePreprocessor.ResizeToLongEdge(source, 1100, minimumLongEdge: 900);
        using var gray = ImagePreprocessor.StretchContrast(ImagePreprocessor.ToGrayscale(medium));
        using var oriented = ImagePreprocessor.RotateQuarterTurns(gray, quarterTurns);

        var temp = Path.Combine(Path.GetTempPath(), $"debug-probe-{Guid.NewGuid():N}.png");
        try
        {
            ImagePreprocessor.SavePng(oriented, temp);
            using var pix = Tesseract.Pix.LoadFromFile(temp);
            using var page = engine.Process(pix, Tesseract.PageSegMode.Auto);
            var text = page.GetText() ?? string.Empty;
            return (CountArabicLetters(text), CardFrontWords.Count(text.Contains));
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static int CountArabicLetters(string text)
    {
        var count = 0;
        foreach (var character in text)
        {
            if (character is >= '\u0621' and <= '\u064A')
            {
                count++;
            }
        }

        return count;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: IdCards.Eval <images-folder> [ground-truth.csv]");
        Console.WriteLine();
        Console.WriteLine("  images-folder     folder containing your own JPG/PNG ID photos");
        Console.WriteLine("  ground-truth.csv  optional, with header: fileName,nationalId,fullName,address");
        Console.WriteLine("                    quote fields that contain commas");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  dotnet run --project Tools/IdCards.Eval -- samples/id-cards samples/ground-truth.csv");
        Console.WriteLine();
        Console.WriteLine("  --export-glyphs <photo> <14 digits> <templates.json>  add number glyphs from your own card");
        Console.WriteLine("  --debug-text <front-photo> [rotation]  compare name/address extraction across pipeline");
        Console.WriteLine("                              settings (crop x fast/best x 1400/1800, saves every OCR input).");
        Console.WriteLine("                              rotation: 0, 90, 180 or 270 to skip the orientation probe.");
        Console.WriteLine();
        Console.WriteLine("This tool never generates or downloads sample ID images.");
    }

    private static string Percent(int value, int total)
    {
        return total == 0 ? "n/a" : (value * 100d / total).ToString("F1", CultureInfo.InvariantCulture) + " %";
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToList();
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static bool TextMatches(string? actual, string expected)
    {
        if (actual is null)
        {
            return false;
        }

        return NormalizeText(actual) == NormalizeText(expected);
    }

    private static string NormalizeText(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\u0640' or '\u200F' or '\u200E')
            {
                continue;
            }

            if (char.IsPunctuation(character) || char.IsWhiteSpace(character))
            {
                builder.Append(' ');
                continue;
            }

            builder.Append(character);
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }

    private static IEnumerable<Truth> ReadGroundTruth(string path)
    {
        var lines = File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        for (var index = 0; index < lines.Count; index++)
        {
            var fields = SplitCsv(lines[index]);
            if (fields.Count < 2)
            {
                continue;
            }

            // Skip the header row when present.
            if (index == 0 && fields[0].Contains("fileName", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new Truth(
                fields[0],
                fields[1],
                fields.Count > 2 && fields[2].Length > 0 ? fields[2] : null,
                fields.Count > 3 && fields[3].Length > 0 ? fields[3] : null);
        }
    }

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                    continue;
                }

                quoted = !quoted;
                continue;
            }

            if (character == ',' && !quoted)
            {
                fields.Add(builder.ToString().Trim());
                builder.Clear();
                continue;
            }

            builder.Append(character);
        }

        fields.Add(builder.ToString().Trim());
        return fields;
    }

    private sealed record Truth(string FileName, string NationalId, string? FullName, string? Address);
}
