using System.Text.Json;
using IdCardReader.Domain;
using SkiaSharp;

namespace IdCardReader.Infrastructure.Imaging;

/// <summary>
/// Reads the printed national ID number glyph by glyph. The Egyptian card prints the number in a
/// decorative Arabic-Indic font (dot-shaped zeros) that Tesseract's stock models cannot read, so the
/// number row is located in the binarised image (a row of 14 similar components), each component is
/// normalised and matched against a small bank of templates with a nearest-neighbour search.
/// The font is the same on every card, so the template bank stays tiny. Pure CPU, no model download.
/// </summary>
public sealed class GlyphDigitReader
{
    /// <summary>Side of the square feature bitmap.</summary>
    public const int GlyphSize = 28;

    /// <summary>Largest accepted mean distance between the 14 glyphs and their templates.</summary>
    public const double MaximumMeanDistance = 4.5;

    /// <summary>Largest accepted distance for any single glyph.</summary>
    public const double MaximumGlyphDistance = 8.5;

    private const int TargetHeight = 24;

    // Position priors: first digit is the century (2 or 3), month tens is 0 or 1, day tens is 0-3.
    private static readonly string[] AllowedByPosition =
    {
        "23", "0123456789", "0123456789", "01", "0123456789", "0123", "0123456789",
        "0123456789", "0123456789", "0123456789", "0123456789", "0123456789", "0123456789", "0123456789"
    };

    private readonly IReadOnlyList<(char Digit, float[] Features)> _templates;

    /// <summary>Creates a reader from an explicit template bank.</summary>
    public GlyphDigitReader(IReadOnlyList<(char Digit, float[] Features)> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        _templates = templates;
    }

    /// <summary>A number read from one image.</summary>
    /// <param name="Value">The 14 ASCII digits, already accepted by the domain parser.</param>
    /// <param name="MeanDistance">Average glyph-to-template distance (lower is better).</param>
    public readonly record struct GlyphNumber(string Value, double MeanDistance);

    /// <summary>One component found in the number row, with its pixels as a mask.</summary>
    public sealed record GlyphMask(int Width, int Height, bool[] Pixels);

    /// <summary>True when the bank has at least one template.</summary>
    public bool HasTemplates => _templates.Count > 0;

    /// <summary>Loads the template bank embedded in this assembly.</summary>
    public static GlyphDigitReader LoadDefault()
    {
        using var stream = typeof(GlyphDigitReader).Assembly.GetManifestResourceStream("glyph-templates.json");
        if (stream is null)
        {
            return new GlyphDigitReader(Array.Empty<(char, float[])>());
        }

        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    /// <summary>Parses a template bank ({"size":28,"templates":[{"d":"2","v":"base64"}]}).</summary>
    public static GlyphDigitReader FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);
        var templates = new List<(char, float[])>();
        foreach (var item in document.RootElement.GetProperty("templates").EnumerateArray())
        {
            var digit = item.GetProperty("d").GetString();
            var bytes = Convert.FromBase64String(item.GetProperty("v").GetString() ?? string.Empty);
            if (string.IsNullOrEmpty(digit) || digit.Length != 1 || bytes.Length != GlyphSize * GlyphSize)
            {
                continue;
            }

            var features = new float[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                features[i] = bytes[i] / 255f;
            }

            templates.Add((digit[0], features));
        }

        return new GlyphDigitReader(templates);
    }

    /// <summary>
    /// Finds the number row in a binarised image (Gray8, ink = 0) and reads it.
    /// Returns null when no row of exactly 14 glyphs matches the template bank closely enough
    /// or the digits are not a valid national ID.
    /// </summary>
    public GlyphNumber? Read(SKBitmap binarized)
    {
        ArgumentNullException.ThrowIfNull(binarized);
        if (!HasTemplates || binarized.ColorType != SKColorType.Gray8)
        {
            return null;
        }

        var width = binarized.Width;
        var height = binarized.Height;
        var ink = new bool[width * height];
        var pixels = binarized.GetPixelSpan();
        for (var y = 0; y < height; y++)
        {
            var offset = y * binarized.RowBytes;
            for (var x = 0; x < width; x++)
            {
                ink[(y * width) + x] = pixels[offset + x] < 128;
            }
        }

        var components = FindComponents(ink, width, height);
        GlyphNumber? best = null;

        foreach (var row in FindNumberRows(components, width, height))
        {
            var masks = row.Select(component => ToMask(component, ink, width)).ToArray();
            var lineHeight = row.Max(component => component.Height);
            var candidate = Classify(masks.Select(mask => Features(mask, lineHeight)).ToArray());

            if (candidate is { } read && (best is null || read.MeanDistance < best.Value.MeanDistance))
            {
                best = read;
            }
        }

        return best;
    }

    /// <summary>Matches 14 feature vectors against the bank. Exposed for tests.</summary>
    public GlyphNumber? Classify(IReadOnlyList<float[]> glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        if (glyphs.Count != DigitCandidates.NationalIdLength)
        {
            return null;
        }

        var chars = new char[glyphs.Count];
        double total = 0;

        for (var position = 0; position < glyphs.Count; position++)
        {
            var allowed = AllowedByPosition[position];
            var bestDistance = double.MaxValue;
            var bestDigit = '\0';

            foreach (var (digit, features) in _templates)
            {
                if (allowed.IndexOf(digit) < 0)
                {
                    continue;
                }

                var distance = Distance(glyphs[position], features);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestDigit = digit;
                }
            }

            if (bestDigit == '\0' || bestDistance > MaximumGlyphDistance)
            {
                return null;
            }

            chars[position] = bestDigit;
            total += bestDistance;
        }

        var mean = total / glyphs.Count;
        var value = new string(chars);

        if (mean > MaximumMeanDistance || NationalIdNumber.TryCreate(value) is null)
        {
            return null;
        }

        return new GlyphNumber(value, mean);
    }

    /// <summary>Normalises one glyph to a <see cref="GlyphSize"/> square feature vector (0..1).</summary>
    public static float[] Features(GlyphMask mask, int lineHeight)
    {
        ArgumentNullException.ThrowIfNull(mask);

        // Scale by the height of the whole row so a dot-shaped zero stays small.
        var scale = TargetHeight / (double)Math.Max(1, lineHeight);
        var newWidth = Math.Clamp((int)Math.Round(mask.Width * scale), 1, GlyphSize);
        var newHeight = Math.Clamp((int)Math.Round(mask.Height * scale), 1, GlyphSize);

        var resized = BoxResize(mask, newWidth, newHeight);
        var canvas = new float[GlyphSize * GlyphSize];
        var offsetY = (GlyphSize - newHeight) / 2;
        var offsetX = (GlyphSize - newWidth) / 2;

        for (var y = 0; y < newHeight; y++)
        {
            for (var x = 0; x < newWidth; x++)
            {
                canvas[((y + offsetY) * GlyphSize) + x + offsetX] = resized[(y * newWidth) + x];
            }
        }

        return Blur(canvas);
    }

    /// <summary>Cuts the mask of one component out of the full ink map.</summary>
    public static GlyphMask ToMask(Component component, bool[] ink, int imageWidth)
    {
        var pixels = new bool[component.Width * component.Height];
        for (var y = 0; y < component.Height; y++)
        {
            for (var x = 0; x < component.Width; x++)
            {
                pixels[(y * component.Width) + x] =
                    ink[((component.Y + y) * imageWidth) + component.X + x];
            }
        }

        return new GlyphMask(component.Width, component.Height, pixels);
    }

    /// <summary>A connected component of ink pixels (bounding box and pixel count).</summary>
    public readonly record struct Component(int X, int Y, int Width, int Height, int Area)
    {
        /// <summary>Vertical centre.</summary>
        public double CenterY => Y + (Height / 2.0);
    }

    /// <summary>Labels 8-connected ink components.</summary>
    public static List<Component> FindComponents(bool[] ink, int width, int height)
    {
        var visited = new bool[ink.Length];
        var result = new List<Component>();
        var stack = new Stack<int>();

        for (var start = 0; start < ink.Length; start++)
        {
            if (!ink[start] || visited[start])
            {
                continue;
            }

            var minX = width;
            var minY = height;
            var maxX = 0;
            var maxY = 0;
            var area = 0;
            visited[start] = true;
            stack.Push(start);

            while (stack.Count > 0)
            {
                var index = stack.Pop();
                var x = index % width;
                var y = index / width;
                area++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);

                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        var next = (ny * width) + nx;
                        if (ink[next] && !visited[next])
                        {
                            visited[next] = true;
                            stack.Push(next);
                        }
                    }
                }
            }

            result.Add(new Component(minX, minY, maxX - minX + 1, maxY - minY + 1, area));
        }

        return result;
    }

    /// <summary>
    /// Finds horizontal runs of exactly 14 components that sit on one text row. Tall components
    /// establish the row; small ones inside the same band (dot-shaped zeros) are added afterwards.
    /// </summary>
    public static List<List<Component>> FindNumberRows(IReadOnlyList<Component> components, int width, int height)
    {
        var shortEdge = Math.Min(width, height);
        var tall = components
            .Where(c => c.Height >= 0.025 * shortEdge
                && c.Height <= 0.15 * shortEdge
                && c.Width <= 0.2 * shortEdge
                && c.Area >= 15)
            .ToList();

        var used = new HashSet<int>();
        var rows = new List<List<Component>>();

        foreach (var seedIndex in Enumerable.Range(0, tall.Count).OrderByDescending(i => tall[i].Height))
        {
            if (used.Contains(seedIndex))
            {
                continue;
            }

            var seed = tall[seedIndex];
            var members = Enumerable.Range(0, tall.Count)
                .Where(j => Math.Abs(tall[j].CenterY - seed.CenterY) <= 0.35 * seed.Height
                    && tall[j].Height / (double)seed.Height is >= 0.7 and <= 1.4)
                .ToList();

            if (members.Count < 6)
            {
                continue;
            }

            foreach (var member in members)
            {
                used.Add(member);
            }

            var top = members.Min(j => tall[j].Y);
            var bottom = members.Max(j => tall[j].Y + tall[j].Height);
            var heights = members.Select(j => tall[j].Height).OrderBy(h => h).ToList();
            var rowHeight = heights[heights.Count / 2];
            var middle = (top + bottom) / 2.0;
            var bandHeight = bottom - top;

            var band = components
                .Where(c => Math.Abs(c.CenterY - middle) <= 0.5 * bandHeight
                    && c.Height >= 0.25 * rowHeight
                    && c.Width >= 3
                    && c.Height <= 1.4 * bandHeight
                    && c.Area >= 8)
                .OrderBy(c => c.X)
                .ToList();

            var segment = new List<Component>();
            var segments = new List<List<Component>> { segment };
            foreach (var component in band)
            {
                if (segment.Count > 0)
                {
                    var previous = segment[^1];
                    if (component.X - (previous.X + previous.Width) > 1.6 * rowHeight)
                    {
                        segment = new List<Component>();
                        segments.Add(segment);
                    }
                }

                segment.Add(component);
            }

            rows.AddRange(segments.Where(s => s.Count == DigitCandidates.NationalIdLength));
        }

        return rows;
    }

    private static double Distance(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }

        return Math.Sqrt(sum);
    }

    private static float[] BoxResize(GlyphMask mask, int newWidth, int newHeight)
    {
        var result = new float[newWidth * newHeight];
        for (var j = 0; j < newHeight; j++)
        {
            var y0 = j * mask.Height / (double)newHeight;
            var y1 = (j + 1) * mask.Height / (double)newHeight;
            for (var i = 0; i < newWidth; i++)
            {
                var x0 = i * mask.Width / (double)newWidth;
                var x1 = (i + 1) * mask.Width / (double)newWidth;
                double accumulated = 0;
                double weight = 0;

                for (var yy = (int)Math.Floor(y0); yy < (int)Math.Ceiling(y1) && yy < mask.Height; yy++)
                {
                    var fy = Math.Min(yy + 1, y1) - Math.Max(yy, y0);
                    for (var xx = (int)Math.Floor(x0); xx < (int)Math.Ceiling(x1) && xx < mask.Width; xx++)
                    {
                        var fx = Math.Min(xx + 1, x1) - Math.Max(xx, x0);
                        accumulated += (mask.Pixels[(yy * mask.Width) + xx] ? 1.0 : 0.0) * fx * fy;
                        weight += fx * fy;
                    }
                }

                result[(j * newWidth) + i] = weight > 0 ? (float)(accumulated / weight) : 0f;
            }
        }

        return result;
    }

    private static float[] Blur(float[] canvas)
    {
        var horizontal = new float[canvas.Length];
        for (var y = 0; y < GlyphSize; y++)
        {
            for (var x = 0; x < GlyphSize; x++)
            {
                var left = x > 0 ? canvas[(y * GlyphSize) + x - 1] : 0f;
                var right = x < GlyphSize - 1 ? canvas[(y * GlyphSize) + x + 1] : 0f;
                horizontal[(y * GlyphSize) + x] = (left + (2 * canvas[(y * GlyphSize) + x]) + right) / 4f;
            }
        }

        var result = new float[canvas.Length];
        for (var y = 0; y < GlyphSize; y++)
        {
            for (var x = 0; x < GlyphSize; x++)
            {
                var up = y > 0 ? horizontal[((y - 1) * GlyphSize) + x] : 0f;
                var down = y < GlyphSize - 1 ? horizontal[((y + 1) * GlyphSize) + x] : 0f;
                result[(y * GlyphSize) + x] = (up + (2 * horizontal[(y * GlyphSize) + x]) + down) / 4f;
            }
        }

        return result;
    }
}
