using IdCardReader.Infrastructure.Imaging;
using SkiaSharp;

namespace IdCardReader.Infrastructure.IntegrationTests;

/// <summary>
/// Verifies the conservative card detector: a clear card on a busy background is found and
/// cropped, and anything ambiguous (no card, tiny card, clutter only) keeps the full image.
/// </summary>
public sealed class CardRegionTests
{
    [Fact]
    public void FindCard_LocatesACardOnNoisyBackground()
    {
        using var photo = CreateCardPhoto(1200, 800, cardLeft: 150, cardTop: 200, cardWidth: 900, cardHeight: 570);
        using var gray = ImagePreprocessor.ToGrayscale(photo);

        var region = CardRegion.FindCard(gray);

        Assert.NotNull(region);
        // Tolerance: a few percent of the frame around the true borders.
        Assert.InRange(region!.Value.Left, 120, 180);
        Assert.InRange(region.Value.Top, 170, 230);
        Assert.InRange(region.Value.Right, 1020, 1080);
        Assert.InRange(region.Value.Bottom, 740, 800);
    }

    [Fact]
    public void FindCard_ReturnsNullForUniformBackground()
    {
        using var photo = CreateBitmap(640, 480, SKColors.LightGray);
        using var gray = ImagePreprocessor.ToGrayscale(photo);

        Assert.Null(CardRegion.FindCard(gray));
    }

    [Fact]
    public void FindCard_ReturnsNullWhenTheCardIsTooSmall()
    {
        // A card covering well under 15% of the frame is not a usable photo of a card.
        using var photo = CreateCardPhoto(1200, 900, cardLeft: 100, cardTop: 100, cardWidth: 300, cardHeight: 190);
        using var gray = ImagePreprocessor.ToGrayscale(photo);

        Assert.Null(CardRegion.FindCard(gray));
    }

    [Fact]
    public void CropToCard_CropsToTheCardWithAMargin()
    {
        using var photo = CreateCardPhoto(1600, 1000, cardLeft: 200, cardTop: 120, cardWidth: 1200, cardHeight: 760);

        using var cropped = CardRegion.CropToCard(photo);

        // Cropped size must be close to the card size (plus margin), never the full frame.
        Assert.InRange(cropped.Width, 1200, 1600);
        Assert.InRange(cropped.Height, 760, 1000);
        Assert.True(cropped.Width < photo.Width);
        Assert.True(cropped.Height < photo.Height);
    }

    [Fact]
    public void CropToCard_KeepsTheFullImageWhenNoCardIsFound()
    {
        using var photo = CreateBitmap(900, 600, SKColors.Beige);

        using var cropped = CardRegion.CropToCard(photo);

        Assert.Equal(photo.Width, cropped.Width);
        Assert.Equal(photo.Height, cropped.Height);
    }

    /// <summary>
    /// A phone photo stand-in: noisy, darker background with a light card that has a dark rim,
    /// some text-like bars and a photo block inside.
    /// </summary>
    private static SKBitmap CreateCardPhoto(
        int width,
        int height,
        int cardLeft,
        int cardTop,
        int cardWidth,
        int cardHeight)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(120, 120, 120));

        var random = new Random(42);
        using var noise = new SKPaint();
        for (var i = 0; i < 4000; i++)
        {
            var shade = (byte)(80 + random.Next(80));
            noise.Color = new SKColor(shade, shade, shade);
            canvas.DrawRect(
                SKRect.Create(
                    random.Next(width),
                    random.Next(height),
                    4 + random.Next(14),
                    4 + random.Next(14)),
                noise);
        }
        using var fill = new SKPaint { Color = new SKColor(235, 230, 220) };
        canvas.DrawRect(SKRect.Create(cardLeft, cardTop, cardWidth, cardHeight), fill);
        using var border = new SKPaint { Color = new SKColor(40, 40, 40), Style = SKPaintStyle.Stroke, StrokeWidth = 6 };
        canvas.DrawRect(SKRect.Create(cardLeft + 3, cardTop + 3, cardWidth - 6, cardHeight - 6), border);
        using var ink = new SKPaint { Color = new SKColor(30, 30, 30) };
        for (var y = cardTop + 60; y < cardTop + cardHeight - 60; y += 48)
        {
            canvas.DrawRect(SKRect.Create(cardLeft + 260, y, cardWidth - 320, 10), ink);
        }

        canvas.DrawRect(SKRect.Create(cardLeft + 40, cardTop + 80, 180, 320), ink);

        return bitmap;
    }

    private static SKBitmap CreateBitmap(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        return bitmap;
    }
}
