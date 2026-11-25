using System.Diagnostics;
using System.Globalization;
using System.Text;
using IdCardReader.Application;
using IdCardReader.Infrastructure;

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

        var service = new TesseractIdCardOcrService(tessdataPath, maxConcurrentScans: 1);

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
            var result = await service.ScanAsync(bytes);
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
