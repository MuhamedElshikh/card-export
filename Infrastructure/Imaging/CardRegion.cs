using SkiaSharp;

namespace IdCardReader.Infrastructure.Imaging;

/// <summary>
/// Locates the card inside a phone photo and crops away the background. Photos of an ID card
/// usually include a table, a hand or other clutter, and whole-image OCR then reads background
/// shapes as text. Cropping to the card also gives the OCR passes more pixels per character,
/// because scaling to the OCR long edge happens after the crop.
/// <para>
/// Detection is deliberately conservative: it requires two long horizontal and two long vertical
/// edges that form a card-like aspect ratio with strong gradient support on all four borders.
/// Any doubt returns null and the caller keeps the full image, so a failed detection never
/// makes a scan worse than before.
/// </para>
/// Pure pixel math on a downscaled grayscale image; no model, no network, no OpenCV.
/// </summary>
public static class CardRegion
{
    /// <summary>Long edge of the working image used for detection (speed, noise reduction).</summary>
    public const int DetectionLongEdge = 640;

    /// <summary>Smallest card area, as a fraction of the frame, accepted as a real card.</summary>
    public const double MinimumAreaFraction = 0.15;

    /// <summary>Smallest fraction of strong gradient pixels along the four borders.</summary>
    public const double MinimumBorderSupport = 0.5;

    // ID-1 cards are 85.6 x 53.98 mm (ratio ~1.586). Perspective and tilt widen the range.
    private const double MinAspect = 1.25;
    private const double MaxAspect = 2.10;

    // Sobel sums are up to 4 x 255; 128 keeps smooth noise out while real card borders pass.
    private const int EdgeThreshold = 128;

    // A candidate edge line must show a strong gradient along at least this share of its length.
    private const double LineCountFraction = 0.28;

    // Generous margin: a detected border can sit on the card's printed frame instead of its
    // physical edge, and clipped glyphs at the text margins are worse than a little background.
    private const double CropMarginFraction = 0.05;

    /// <summary>
    /// Returns a crop of <paramref name="source"/> around the detected card, or an unchanged copy
    /// when no confident card region is found.
    /// </summary>
    public static SKBitmap CropToCard(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var small = ImagePreprocessor.ResizeToLongEdge(source, DetectionLongEdge);
        using var gray = small.ColorType == SKColorType.Gray8
            ? small.Copy(SKColorType.Gray8)
            : ImagePreprocessor.ToGrayscale(small);

        if (FindCard(gray) is not { } region)
        {
            return source.Copy(source.ColorType);
        }

        var scale = Math.Max(source.Width / (double)small.Width, source.Height / (double)small.Height);
        var marginX = region.Width * CropMarginFraction * scale;
        var marginY = region.Height * CropMarginFraction * scale;

        var left = Math.Max(0, (int)Math.Floor((region.Left * scale) - marginX));
        var top = Math.Max(0, (int)Math.Floor((region.Top * scale) - marginY));
        var right = Math.Min(source.Width, (int)Math.Ceiling((region.Right * scale) + marginX));
        var bottom = Math.Min(source.Height, (int)Math.Ceiling((region.Bottom * scale) + marginY));

        if (right - left < 50 || bottom - top < 50)
        {
            return source.Copy(source.ColorType);
        }

        var target = new SKBitmap(new SKImageInfo(
            right - left,
            bottom - top,
            source.ColorType,
            SKAlphaType.Opaque));
        using var canvas = new SKCanvas(target);
        canvas.DrawBitmap(
            source,
            new SKRect(left, top, right, bottom),
            new SKRect(0, 0, target.Width, target.Height));
        canvas.Flush();

        return target;
    }

    /// <summary>
    /// Finds the card rectangle in a small grayscale image, or null when the borders are not
    /// convincing enough. Coordinates are in the passed image's pixel space.
    /// </summary>
    public static SKRectI? FindCard(SKBitmap gray)
    {
        ArgumentNullException.ThrowIfNull(gray);
        if (gray.ColorType != SKColorType.Gray8 || gray.Width < 120 || gray.Height < 120)
        {
            return null;
        }

        var width = gray.Width;
        var height = gray.Height;
        var gradientX = new int[width * height];
        var gradientY = new int[width * height];
        ComputeSobel(gray, gradientX, gradientY);

        var horizontal = FindLines(gradientY, width, height, horizontal: true);
        var vertical = FindLines(gradientX, width, height, horizontal: false);
        if (horizontal.Count < 2 || vertical.Count < 2)
        {
            return null;
        }

        SKRectI? best = null;
        var bestSupport = 0.0;

        for (var topIndex = 0; topIndex < horizontal.Count - 1; topIndex++)
        {
            for (var bottomIndex = topIndex + 1; bottomIndex < horizontal.Count; bottomIndex++)
            {
                var top = horizontal[topIndex];
                var bottom = horizontal[bottomIndex];
                if (bottom - top < 60)
                {
                    continue;
                }

                for (var leftIndex = 0; leftIndex < vertical.Count - 1; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < vertical.Count; rightIndex++)
                    {
                        var left = vertical[leftIndex];
                        var right = vertical[rightIndex];
                        if (right - left < 80)
                        {
                            continue;
                        }

                        var aspect = (right - left) / (double)(bottom - top);
                        var landscape = aspect is >= MinAspect and <= MaxAspect;
                        var portrait = aspect > 0 && (1 / aspect) is >= MinAspect and <= MaxAspect;
                        if (!landscape && !portrait)
                        {
                            continue;
                        }

                        var areaFraction = ((right - left) * (double)(bottom - top)) / (width * (double)height);
                        if (areaFraction < MinimumAreaFraction)
                        {
                            continue;
                        }

                        var support = BorderSupport(gradientX, gradientY, width, height, left, top, right, bottom);
                        if (support >= MinimumBorderSupport && support > bestSupport)
                        {
                            bestSupport = support;
                            best = new SKRectI(left, top, right, bottom);
                        }
                    }
                }
            }
        }

        return best;
    }

    private static unsafe void ComputeSobel(SKBitmap gray, int[] gradientX, int[] gradientY)
    {
        var width = gray.Width;
        var height = gray.Height;
        var rowBytes = gray.RowBytes;
        var pixels = new ReadOnlySpan<byte>((void*)gray.GetPixels(), rowBytes * height);

        for (var y = 1; y < height - 1; y++)
        {
            var up = (y - 1) * rowBytes;
            var mid = y * rowBytes;
            var down = (y + 1) * rowBytes;
            var index = y * width;

            for (var x = 1; x < width - 1; x++)
            {
                var gx =
                    pixels[up + x + 1] + (2 * pixels[mid + x + 1]) + pixels[down + x + 1]
                    - pixels[up + x - 1] - (2 * pixels[mid + x - 1]) - pixels[down + x - 1];
                var gy =
                    pixels[down + x - 1] + (2 * pixels[down + x]) + pixels[down + x + 1]
                    - pixels[up + x - 1] - (2 * pixels[up + x]) - pixels[up + x + 1];

                gradientX[index + x] = Math.Abs(gx);
                gradientY[index + x] = Math.Abs(gy);
            }
        }
    }

    /// <summary>
    /// Finds rows (or columns) whose strong-gradient pixel count marks them as long straight edges.
    /// Neighbouring rows collapse into one band whose centre is the candidate line.
    /// </summary>
    private static List<int> FindLines(int[] gradient, int width, int height, bool horizontal)
    {
        var outerLength = horizontal ? height : width;
        var innerLength = horizontal ? width : height;
        var minimumCount = Math.Max(40, (int)(innerLength * LineCountFraction));
        var lines = new List<int>();

        var bandStart = -1;
        var bandEnd = -1;

        for (var outer = 0; outer < outerLength; outer++)
        {
            var count = 0;
            for (var inner = 0; inner < innerLength; inner++)
            {
                var index = horizontal ? (outer * width) + inner : (inner * width) + outer;
                if (gradient[index] >= EdgeThreshold)
                {
                    count++;
                }
            }

            if (count >= minimumCount)
            {
                if (bandStart < 0)
                {
                    bandStart = outer;
                }

                bandEnd = outer;
            }
            else if (bandStart >= 0 && outer - bandEnd > 2)
            {
                lines.Add((bandStart + bandEnd) / 2);
                bandStart = -1;
            }
        }

        if (bandStart >= 0)
        {
            lines.Add((bandStart + bandEnd) / 2);
        }

        return lines;
    }

    /// <summary>
    /// Average share of strong gradient pixels along the four borders: top/bottom measured with
    /// the vertical gradient, left/right with the horizontal one.
    /// </summary>
    private static double BorderSupport(
        int[] gradientX,
        int[] gradientY,
        int width,
        int height,
        int left,
        int top,
        int right,
        int bottom)
    {
        var horizontalLength = right - left;
        var verticalLength = bottom - top;

        var topCount = 0;
        var bottomCount = 0;
        for (var x = left; x < right; x++)
        {
            if (gradientY[(top * width) + x] >= EdgeThreshold)
            {
                topCount++;
            }

            if (gradientY[(bottom * width) + x] >= EdgeThreshold)
            {
                bottomCount++;
            }
        }

        var leftCount = 0;
        var rightCount = 0;
        for (var y = top; y < bottom; y++)
        {
            if (gradientX[(y * width) + left] >= EdgeThreshold)
            {
                leftCount++;
            }

            if (gradientX[(y * width) + right] >= EdgeThreshold)
            {
                rightCount++;
            }
        }

        var horizontalSupport = (topCount + bottomCount) / (2.0 * horizontalLength);
        var verticalSupport = (leftCount + rightCount) / (2.0 * verticalLength);

        return (horizontalSupport + verticalSupport) / 2;
    }
}
