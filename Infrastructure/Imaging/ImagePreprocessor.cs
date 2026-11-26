using SkiaSharp;

namespace IdCardReader.Infrastructure.Imaging;

/// <summary>
/// Pure image preparation helpers used before the OCR engine runs: EXIF orientation, downscaling,
/// grayscale, contrast stretching, local-mean/Otsu binarisation, deskew estimation and rotation.
/// No OCR, no network, no storage.
/// </summary>
public static class ImagePreprocessor
{
    /// <summary>Decodes JPEG/PNG bytes and applies the EXIF orientation so the image is upright.</summary>
    public static SKBitmap? DecodeWithOrientation(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            return null;
        }

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());

        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            decoded.Dispose();
            return null;
        }

        return ApplyOrientation(decoded, codec.EncodedOrigin);
    }

    /// <summary>
    /// Scales the image so its longest edge is <paramref name="longEdge"/>, never shrinking below
    /// <paramref name="minimumLongEdge"/> (Tesseract needs roughly 30 px tall characters).
    /// </summary>
    public static SKBitmap ResizeToLongEdge(SKBitmap source, int longEdge, int minimumLongEdge = 0)
    {
        ArgumentNullException.ThrowIfNull(source);

        var longest = Math.Max(source.Width, source.Height);
        var targetEdge = longest > longEdge ? longEdge : longest;
        if (minimumLongEdge > 0 && longest < minimumLongEdge)
        {
            targetEdge = minimumLongEdge;
        }

        if (Math.Abs(longest - targetEdge) < 2)
        {
            return source.Copy(SKColorType.Bgra8888);
        }

        var ratio = targetEdge / (double)longest;
        var width = Math.Max(1, (int)Math.Round(source.Width * ratio));
        var height = Math.Max(1, (int)Math.Round(source.Height * ratio));

        var resized = source.Resize(
            new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque),
            SKFilterQuality.High);

        return resized ?? source.Copy(SKColorType.Bgra8888);
    }

    /// <summary>Converts to an 8 bit grayscale bitmap (BT.601 luma, alpha dropped).</summary>
    public static SKBitmap ToGrayscale(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var bgra = source.ColorType == SKColorType.Bgra8888
            ? source.Copy()
            : source.Copy(SKColorType.Bgra8888);

        var target = new SKBitmap(new SKImageInfo(bgra.Width, bgra.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        var sourcePixels = Pixels(bgra);
        var targetPixels = Pixels(target);
        var sourceRowBytes = bgra.RowBytes;
        var targetRowBytes = target.RowBytes;

        for (var y = 0; y < bgra.Height; y++)
        {
            var sourceOffset = y * sourceRowBytes;
            var targetOffset = y * targetRowBytes;

            for (var x = 0; x < bgra.Width; x++)
            {
                var pixel = sourceOffset + (x * 4);
                var blue = sourcePixels[pixel];
                var green = sourcePixels[pixel + 1];
                var red = sourcePixels[pixel + 2];

                targetPixels[targetOffset + x] = (byte)(((red * 77) + (green * 150) + (blue * 29)) >> 8);
            }
        }

        return target;
    }

    /// <summary>Stretches the histogram between the 1st and 99th percentile (helps washed out photos).</summary>
    public static SKBitmap StretchContrast(SKBitmap gray)
    {
        ArgumentNullException.ThrowIfNull(gray);

        var source = Pixels(gray);
        var rowBytes = gray.RowBytes;
        var histogram = new int[256];
        long total = 0;

        for (var y = 0; y < gray.Height; y++)
        {
            var offset = y * rowBytes;
            for (var x = 0; x < gray.Width; x++)
            {
                histogram[source[offset + x]]++;
                total++;
            }
        }

        var lowTarget = total * 0.01;
        var highTarget = total * 0.99;
        long running = 0;
        var low = 0;
        var high = 255;

        for (var value = 0; value < 256; value++)
        {
            running += histogram[value];
            if (running >= lowTarget)
            {
                low = value;
                break;
            }
        }

        running = 0;
        for (var value = 0; value < 256; value++)
        {
            running += histogram[value];
            if (running >= highTarget)
            {
                high = value;
                break;
            }
        }

        if (high <= low + 8)
        {
            return gray.Copy(SKColorType.Gray8);
        }

        var target = new SKBitmap(new SKImageInfo(gray.Width, gray.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        var destination = Pixels(target);
        var scale = 255.0 / (high - low);

        for (var y = 0; y < gray.Height; y++)
        {
            var offset = y * rowBytes;
            var targetOffset = y * target.RowBytes;
            for (var x = 0; x < gray.Width; x++)
            {
                var value = (source[offset + x] - low) * scale;
                destination[targetOffset + x] = (byte)Math.Clamp(value, 0, 255);
            }
        }

        return target;
    }

    /// <summary>Applies a light unsharp mask to improve printed character edges.</summary>
    public static SKBitmap Sharpen(SKBitmap source, double amount = 0.8)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (amount < 0 || amount > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Sharpen amount must be between 0 and 2.");
        }

        using var gray = source.ColorType == SKColorType.Gray8
            ? source.Copy(SKColorType.Gray8)
            : ToGrayscale(source);

        var target = new SKBitmap(new SKImageInfo(gray.Width, gray.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        var input = Pixels(gray);
        var output = Pixels(target);

        for (var y = 0; y < gray.Height; y++)
        {
            var destinationOffset = y * target.RowBytes;
            for (var x = 0; x < gray.Width; x++)
            {
                var sum = 0;
                var count = 0;

                for (var neighborY = Math.Max(0, y - 1); neighborY <= Math.Min(gray.Height - 1, y + 1); neighborY++)
                {
                    var sourceOffset = neighborY * gray.RowBytes;
                    for (var neighborX = Math.Max(0, x - 1); neighborX <= Math.Min(gray.Width - 1, x + 1); neighborX++)
                    {
                        if (neighborX == x && neighborY == y)
                        {
                            continue;
                        }

                        sum += input[sourceOffset + neighborX];
                        count++;
                    }
                }

                var center = input[(y * gray.RowBytes) + x];
                var blurred = count == 0 ? center : sum / (double)count;
                output[destinationOffset + x] = (byte)Math.Clamp(
                    Math.Round(center + ((center - blurred) * amount)),
                    0,
                    255);
            }
        }

        return target;
    }

    /// <summary>
    /// Local mean threshold (integral image). Robust to shadows and uneven lighting, which is the
    /// usual failure mode of a phone photo of an ID card.
    /// </summary>
    public static SKBitmap BinarizeLocalMean(SKBitmap gray, double k = 0.85)
    {
        ArgumentNullException.ThrowIfNull(gray);

        var width = gray.Width;
        var height = gray.Height;
        var source = Pixels(gray);
        var rowBytes = gray.RowBytes;
        var integral = new long[(width + 1) * (height + 1)];

        for (var y = 0; y < height; y++)
        {
            long rowSum = 0;
            var sourceOffset = y * rowBytes;
            var current = (y + 1) * (width + 1);
            var previous = y * (width + 1);

            for (var x = 0; x < width; x++)
            {
                rowSum += source[sourceOffset + x];
                integral[current + x + 1] = integral[previous + x + 1] + rowSum;
            }
        }

        var radius = Math.Max(7, Math.Min(width, height) / 32);
        var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        var destination = Pixels(target);

        for (var y = 0; y < height; y++)
        {
            var y0 = Math.Max(0, y - radius);
            var y1 = Math.Min(height - 1, y + radius);
            var sourceOffset = y * rowBytes;
            var targetOffset = y * target.RowBytes;

            for (var x = 0; x < width; x++)
            {
                var x0 = Math.Max(0, x - radius);
                var x1 = Math.Min(width - 1, x + radius);

                var area = (y1 - y0 + 1) * (x1 - x0 + 1);
                var sum = integral[((y1 + 1) * (width + 1)) + (x1 + 1)]
                    - integral[(y0 * (width + 1)) + (x1 + 1)]
                    - integral[((y1 + 1) * (width + 1)) + x0]
                    + integral[(y0 * (width + 1)) + x0];

                var threshold = sum / (double)area * k;
                destination[targetOffset + x] = source[sourceOffset + x] < threshold ? (byte)0 : (byte)255;
            }
        }

        return target;
    }

    /// <summary>Global Otsu threshold. Cheap second opinion when the local mean pass finds nothing.</summary>
    public static SKBitmap BinarizeOtsu(SKBitmap gray)
    {
        ArgumentNullException.ThrowIfNull(gray);

        var source = Pixels(gray);
        var rowBytes = gray.RowBytes;
        var histogram = new long[256];
        var total = gray.Width * gray.Height;

        for (var y = 0; y < gray.Height; y++)
        {
            var offset = y * rowBytes;
            for (var x = 0; x < gray.Width; x++)
            {
                histogram[source[offset + x]]++;
            }
        }

        long sum = 0;
        for (var value = 0; value < 256; value++)
        {
            sum += value * histogram[value];
        }

        long backgroundSum = 0;
        long backgroundWeight = 0;
        var bestVariance = -1.0;
        var threshold = 128;

        for (var value = 0; value < 256; value++)
        {
            backgroundWeight += histogram[value];
            if (backgroundWeight == 0)
            {
                continue;
            }

            var foregroundWeight = total - backgroundWeight;
            if (foregroundWeight == 0)
            {
                break;
            }

            backgroundSum += value * histogram[value];
            var backgroundMean = backgroundSum / (double)backgroundWeight;
            var foregroundMean = (sum - backgroundSum) / foregroundWeight;
            var variance = backgroundWeight * (double)foregroundWeight
                * (backgroundMean - foregroundMean) * (backgroundMean - foregroundMean);

            if (variance > bestVariance)
            {
                bestVariance = variance;
                threshold = value;
            }
        }

        var target = new SKBitmap(new SKImageInfo(gray.Width, gray.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        var destination = Pixels(target);

        for (var y = 0; y < gray.Height; y++)
        {
            var offset = y * rowBytes;
            var targetOffset = y * target.RowBytes;
            for (var x = 0; x < gray.Width; x++)
            {
                destination[targetOffset + x] = source[offset + x] <= threshold ? (byte)0 : (byte)255;
            }
        }

        return target;
    }

    /// <summary>
    /// Estimates the small rotation that makes text lines horizontal, using the variance of the
    /// horizontal ink projection over a set of candidate angles.
    /// </summary>
    public static double EstimateSkewDegrees(SKBitmap gray, double maxDegrees = 8.0)
    {
        ArgumentNullException.ThrowIfNull(gray);

        var baseline = ProjectionScore(gray, 0);
        var best = 0.0;
        var bestScore = baseline;

        for (var angle = -maxDegrees; angle <= maxDegrees + 0.001; angle += 2.0)
        {
            var score = ProjectionScore(gray, angle);
            if (score > bestScore)
            {
                bestScore = score;
                best = angle;
            }
        }

        // Images without a dominant horizontal direction (for example a sideways photo) produce
        // nearly identical scores at every angle. Do not rotate those; it only adds skew.
        if (bestScore < baseline * 1.15)
        {
            return 0;
        }

        for (var angle = best - 2.0; angle <= best + 2.0 + 0.001; angle += 0.5)
        {
            var score = ProjectionScore(gray, angle);
            if (score > bestScore)
            {
                bestScore = score;
                best = angle;
            }
        }

        return Math.Abs(best) < 0.75 ? 0 : best;
    }

    /// <summary>Rotates by an arbitrary angle, filling the background with white.</summary>
    public static SKBitmap Rotate(SKBitmap source, double degrees)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (Math.Abs(degrees) < 0.01)
        {
            return source.Copy(source.ColorType);
        }

        var radians = degrees * Math.PI / 180.0;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));
        var width = (int)Math.Ceiling((source.Width * cos) + (source.Height * sin));
        var height = (int)Math.Ceiling((source.Width * sin) + (source.Height * cos));

        var target = new SKBitmap(new SKImageInfo(width, height, source.ColorType, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.White);
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees((float)degrees);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0, null);
        canvas.Flush();

        return target;
    }

    /// <summary>Rotates by whole quarter turns clockwise (0..3).</summary>
    public static SKBitmap RotateQuarterTurns(SKBitmap source, int quarterTurns)
    {
        ArgumentNullException.ThrowIfNull(source);

        var turns = ((quarterTurns % 4) + 4) % 4;
        if (turns == 0)
        {
            return source.Copy(source.ColorType);
        }

        var swap = turns is 1 or 3;
        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;

        var target = new SKBitmap(new SKImageInfo(width, height, source.ColorType, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.White);
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees(turns * 90f);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0, null);
        canvas.Flush();

        return target;
    }

    /// <summary>Writes a bitmap to a PNG file so the OCR engine can load it.</summary>
    public static void SavePng(SKBitmap bitmap, string path)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static unsafe Span<byte> Pixels(SKBitmap bitmap)
    {
        return new Span<byte>((void*)bitmap.GetPixels(), bitmap.RowBytes * bitmap.Height);
    }

    private static double ProjectionScore(SKBitmap gray, double angle)
    {
        using var rotated = Rotate(gray, angle);
        var span = Pixels(rotated);
        var width = rotated.Width;
        var height = rotated.Height;
        var rowBytes = rotated.RowBytes;

        var sums = new long[height];
        for (var y = 0; y < height; y++)
        {
            var offset = y * rowBytes;
            long sum = 0;
            for (var x = 0; x < width; x++)
            {
                sum += 255 - span[offset + x];
            }

            sums[y] = sum;
        }

        double score = 0;
        for (var y = 1; y < height; y++)
        {
            var delta = sums[y] - sums[y - 1];
            score += delta * delta;
        }

        return score;
    }

    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft)
        {
            return source;
        }

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;

        var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.White);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;

            case SKEncodedOrigin.BottomRight:
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;

            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;

            // Mirrored transpose variants are rare; a plain quarter turn is the closest sane result.
            case SKEncodedOrigin.LeftTop:
            case SKEncodedOrigin.RightTop:
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;

            case SKEncodedOrigin.RightBottom:
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, height);
                canvas.RotateDegrees(-90);
                break;
        }

        canvas.DrawBitmap(source, 0, 0, null);
        canvas.Flush();
        source.Dispose();

        return target;
    }
}
