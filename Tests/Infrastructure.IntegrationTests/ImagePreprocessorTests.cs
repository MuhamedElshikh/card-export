using IdCardReader.Infrastructure.Imaging;
using SkiaSharp;

namespace IdCardReader.Infrastructure.IntegrationTests;

/// <summary>
/// Verifies the image preparation steps that make phone photos readable at all:
/// decoding, scaling, grayscale conversion, binarisation, rotation and deskew probing.
/// </summary>
public sealed class ImagePreprocessorTests
{
    [Fact]
    public void DecodeWithOrientation_DecodesPngAndJpeg()
    {
        var bytes = CreatePng();

        using var decoded = ImagePreprocessor.DecodeWithOrientation(bytes);

        Assert.NotNull(decoded);
        Assert.Equal(320, decoded!.Width);
        Assert.Equal(200, decoded.Height);
    }

    [Fact]
    public void DecodeWithOrientation_ReturnsNullForGarbage()
    {
        Assert.Null(ImagePreprocessor.DecodeWithOrientation(new byte[] { 0x00, 0x01, 0x02, 0x03 }));
    }

    [Fact]
    public void ResizeToLongEdge_DownscalesKeepingAspectRatio()
    {
        using var source = CreateBitmap(1200, 800);

        using var resized = ImagePreprocessor.ResizeToLongEdge(source, 600);

        Assert.Equal(600, resized.Width);
        Assert.Equal(400, resized.Height);
    }

    [Fact]
    public void ResizeToLongEdge_DoesNotChangeSmallImages()
    {
        using var source = CreateBitmap(200, 100);

        using var resized = ImagePreprocessor.ResizeToLongEdge(source, 1600);

        Assert.Equal(200, resized.Width);
        Assert.Equal(100, resized.Height);
    }

    [Fact]
    public void ResizeToLongEdge_UpscalesBelowTheMinimum()
    {
        using var source = CreateBitmap(200, 100);

        using var resized = ImagePreprocessor.ResizeToLongEdge(source, 1600, minimumLongEdge: 1000);

        Assert.Equal(1000, resized.Width);
        Assert.Equal(500, resized.Height);
    }

    [Fact]
    public void ToGrayscale_ProducesGray8WithExpectedLuma()
    {
        using var source = CreateBitmap(10, 10, SKColors.White);

        using var gray = ImagePreprocessor.ToGrayscale(source);

        Assert.Equal(SKColorType.Gray8, gray.ColorType);
        Assert.Equal(255, gray.GetPixel(5, 5).Red);
    }

    [Fact]
    public void ToGrayscale_BlackStaysBlack()
    {
        using var source = CreateBitmap(10, 10, SKColors.Black);

        using var gray = ImagePreprocessor.ToGrayscale(source);

        Assert.Equal(0, gray.GetPixel(5, 5).Red);
    }

    [Fact]
    public void BinarizeLocalMean_SeparatesInkFromPaper()
    {
        using var source = CreateInkLines(200, 120);

        using var gray = ImagePreprocessor.ToGrayscale(source);
        using var binary = ImagePreprocessor.BinarizeLocalMean(gray);

        // Text strokes are dark; the paper around them stays white.
        Assert.Equal(0, binary.GetPixel(100, 21).Red);
        Assert.Equal(255, binary.GetPixel(100, 10).Red);
    }

    [Fact]
    public void BinarizeOtsu_ProducesOnlyBlackOrWhite()
    {
        using var source = CreateBitmapWithBlackSquare(200, 120);

        using var gray = ImagePreprocessor.ToGrayscale(source);
        using var binary = ImagePreprocessor.BinarizeOtsu(gray);

        var values = new HashSet<byte>();
        for (var y = 0; y < binary.Height; y++)
        {
            for (var x = 0; x < binary.Width; x++)
            {
                values.Add((byte)binary.GetPixel(x, y).Red);
            }
        }

        Assert.Subset(new HashSet<byte> { 0, 255 }, values);
    }

    [Fact]
    public void StretchContrast_LeavesFlatImagesUntouched()
    {
        using var source = CreateBitmap(20, 20, new SKColor(128, 128, 128));

        using var gray = ImagePreprocessor.ToGrayscale(source);
        using var stretched = ImagePreprocessor.StretchContrast(gray);

        Assert.Equal(128, stretched.GetPixel(10, 10).Red);
    }

    [Theory]
    [InlineData(1, 120, 200)]
    [InlineData(2, 200, 120)]
    [InlineData(3, 120, 200)]
    [InlineData(0, 200, 120)]
    public void RotateQuarterTurns_SwapsDimensionsForOddTurns(int turns, int expectedWidth, int expectedHeight)
    {
        using var source = CreateBitmap(200, 120);

        using var rotated = ImagePreprocessor.RotateQuarterTurns(source, turns);

        Assert.Equal(expectedWidth, rotated.Width);
        Assert.Equal(expectedHeight, rotated.Height);
    }

    [Fact]
    public void RotateQuarterTurns_MovesTheInkCorner()
    {
        // A black square in the top left must end up in the top right after a 90° clockwise turn.
        using var source = CreateBitmap(200, 120);
        using (var canvas = new SKCanvas(source))
        using (var paint = new SKPaint { Color = SKColors.Black })
        {
            canvas.DrawRect(SKRect.Create(0, 0, 40, 40), paint);
        }

        using var rotated = ImagePreprocessor.RotateQuarterTurns(source, 1);

        Assert.Equal(0, rotated.GetPixel(rotated.Width - 10, 10).Red);
        Assert.Equal(255, rotated.GetPixel(10, 10).Red);
    }

    [Fact]
    public void Rotate_KeepsTheImageContent()
    {
        using var source = CreateBitmapWithBlackSquare(200, 120);

        using var rotated = ImagePreprocessor.Rotate(source, 5);

        Assert.True(rotated.Width >= 200);
        Assert.True(rotated.Height >= 120);
    }

    [Fact]
    public void EstimateSkewDegrees_ReturnsZeroForUprightText()
    {
        using var source = CreateSkewedStripes(0);

        var angle = ImagePreprocessor.EstimateSkewDegrees(source);

        Assert.InRange(angle, -0.75, 0.75);
    }

    [Fact]
    public void EstimateSkewDegrees_CorrectsRotatedText()
    {
        using var source = CreateSkewedStripes(6);

        var angle = ImagePreprocessor.EstimateSkewDegrees(source);

        // The estimate must be large enough to matter and must, when applied, remove the skew.
        Assert.InRange(Math.Abs(angle), 4.0, 8.0);

        using var corrected = ImagePreprocessor.Rotate(source, angle);
        var residual = ImagePreprocessor.EstimateSkewDegrees(corrected);

        Assert.InRange(Math.Abs(residual), 0.0, 2.0);
    }

    [Fact]
    public void SavePng_WritesADecodableFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"preproc-{Guid.NewGuid():N}.png");
        try
        {
            using (var source = CreateBitmapWithBlackSquare(120, 80))
            {
                ImagePreprocessor.SavePng(source, path);
            }

            using var decoded = ImagePreprocessor.DecodeWithOrientation(File.ReadAllBytes(path));

            Assert.NotNull(decoded);
            Assert.Equal(120, decoded!.Width);
        }
        finally
        {
            File.Delete(path);
        }
    }

    internal static SKBitmap CreateBitmap(int width, int height, SKColor? color = null)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color ?? SKColors.White);
        return bitmap;
    }

    internal static SKBitmap CreateBitmapWithBlackSquare(int width, int height)
    {
        var bitmap = CreateBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = SKColors.Black };
        canvas.DrawRect(SKRect.Create(width / 4f, height / 4f, width / 2f, height / 2f), paint);
        return bitmap;
    }

    /// <summary>Thin black bars on white, standing in for printed text strokes.</summary>
    internal static SKBitmap CreateInkLines(int width, int height)
    {
        var bitmap = CreateBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = SKColors.Black };
        for (var y = 20; y < height - 4; y += 20)
        {
            canvas.DrawRect(SKRect.Create(10, y, width - 20, 4), paint);
        }

        return bitmap;
    }

    /// <summary>Dark horizontal bands, rotated by <paramref name="angle"/> degrees.</summary>
    private static SKBitmap CreateSkewedStripes(double angle)
    {
        using var bitmap = CreateBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.Translate(200, 150);
            canvas.RotateDegrees((float)angle);
            canvas.Translate(-200, -150);

            using var paint = new SKPaint { Color = SKColors.Black };
            for (var y = 20; y < 300; y += 24)
            {
                canvas.DrawRect(SKRect.Create(20, y, 360, 8), paint);
            }
        }

        return ImagePreprocessor.ToGrayscale(bitmap);
    }

    internal static byte[] CreatePng(int width = 320, int height = 200)
    {
        using var bitmap = CreateBitmapWithBlackSquare(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
