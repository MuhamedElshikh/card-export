using IdCardReader.Application;
using IdCardReader.Infrastructure;
using IdCardReader.Infrastructure.Imaging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace IdCardReader.Infrastructure.IntegrationTests;

/// <summary>
/// Runs the real Tesseract and PP-OCRv5 engines over generated typography fixtures.
/// These are not sample ID cards and say nothing about accuracy on real photos: they only prove
/// that the decode, preprocessing, rotation search, voting and domain validation chain works.
/// </summary>
public sealed class TesseractIdCardOcrServiceTests
{
    private const string Number = "29001011234567";

    [Fact]
    public async Task Scan_ReadsAnUprightNumber()
    {
        using var service = CreateService();
        var bytes = RenderNumber();

        var result = await service.ScanAsync(bytes, RenderBlankBack());

        Assert.Equal(Number, result.NationalId);
        Assert.True(result.NationalIdValid);
        Assert.False(result.LowConfidence);
        Assert.Equal(new DateOnly(1990, 1, 1), result.BirthDate);
        Assert.Equal("Female", result.Gender);
        Assert.Equal("Dakahlia", result.GovernorateEnglish);
    }

    [Fact]
    public async Task Scan_ReadsANumberInASidewaysPhoto()
    {
        using var service = CreateService();

        using var upright = RenderBitmap();
        using var sideways = ImagePreprocessor.RotateQuarterTurns(upright, 1);
        var bytes = EncodePng(sideways);

        var result = await service.ScanAsync(bytes, RenderBlankBack());

        Assert.Equal(Number, result.NationalId);
    }

    [Fact]
    public async Task Scan_UsesAlternateOcrWhenPrimaryReadIsLowConfidence()
    {
        using var service = CreateService();
        var front = RenderDegradedNumber();

        var result = await service.ScanAsync(front, RenderBlankBack());

        Assert.Equal(Number, result.NationalId);
        Assert.True(result.NationalIdValid);
        Assert.True(result.LowConfidence);
    }

    [Fact]
    public async Task Scan_RejectsUndecodableBytes()
    {
        using var service = CreateService();

        await Assert.ThrowsAsync<ImageDecodeException>(
            () => service.ScanAsync(new byte[] { 1, 2, 3, 4 }, new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public async Task Scan_ReturnsNoNumberForBlankPaper()
    {
        using var service = CreateService();

        using var blank = ImagePreprocessorTests.CreateBitmap(1400, 500);
        var bytes = EncodePng(blank);

        var result = await service.ScanAsync(bytes, RenderBlankBack());

        Assert.Null(result.NationalId);
        Assert.True(result.LowConfidence);
        Assert.Null(result.FullName);
        Assert.Null(result.Address);
    }

    [Fact]
    public async Task Scan_ThrowsTimeoutWhenTheBudgetIsUsedUp()
    {
        using var service = new TesseractIdCardOcrService(
            Options.Create(new TesseractOptions { ScanTimeout = TimeSpan.FromTicks(1) }));

        using var blank = ImagePreprocessorTests.CreateBitmap(1400, 500);

        await Assert.ThrowsAsync<ScanTimeoutException>(() => service.ScanAsync(EncodePng(blank), RenderBlankBack()));
    }

    [Fact]
    public async Task Scan_ReportsCallerCancellationAsCancellationNotTimeout()
    {
        using var service = CreateService();
        using var blank = ImagePreprocessorTests.CreateBitmap(1400, 500);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ScanAsync(EncodePng(blank), RenderBlankBack(), cancelled.Token));
    }

    [Fact]
    public async Task Scan_RefusesWhenTheServerIsSaturated()
    {
        // One running scan and no waiting room: a second upload must be refused at once.
        using var service = new TesseractIdCardOcrService(
            Options.Create(new TesseractOptions { MaxConcurrentScans = 1, MaxQueuedScans = 0 }));

        using var blank = ImagePreprocessorTests.CreateBitmap(1400, 500);
        var bytes = EncodePng(blank);

        // The first call takes its slot synchronously, before it yields.
        var first = service.ScanAsync(bytes, RenderBlankBack());

        await Assert.ThrowsAsync<ScanBusyException>(() => service.ScanAsync(bytes, bytes));

        await first;

        // The slot is free again once the first scan has finished.
        var third = await service.ScanAsync(bytes, RenderBlankBack());
        Assert.Null(third.NationalId);
    }

    private static TesseractIdCardOcrService CreateService()
    {
        return new TesseractIdCardOcrService(Options.Create(new TesseractOptions()));
    }

    private static byte[] RenderNumber()
    {
        using var bitmap = RenderBitmap();
        return EncodePng(bitmap);
    }

    private static byte[] RenderDegradedNumber()
    {
        using var original = new SKBitmap(new SKImageInfo(768, 768, SKColorType.Bgra8888, SKAlphaType.Opaque));

        using (var canvas = new SKCanvas(original))
        using (var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold))
        using (var font = new SKFont(typeface, 21))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText("2 90 01 01 12 3456 7", 20, 460, font, paint);
            canvas.Flush();
        }

        using var lowResolution = original.Resize(
            new SKImageInfo(256, 256, SKColorType.Bgra8888, SKAlphaType.Opaque),
            SKFilterQuality.Low);
        using var recompressed = lowResolution.Resize(
            new SKImageInfo(768, 768, SKColorType.Bgra8888, SKAlphaType.Opaque),
            SKFilterQuality.Low);
        using var image = SKImage.FromBitmap(recompressed);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 25);

        return data.ToArray();
    }

    private static byte[] RenderBlankBack()
    {
        using var bitmap = ImagePreprocessorTests.CreateBitmap(1400, 500);
        return EncodePng(bitmap);
    }

    private static SKBitmap RenderBitmap()
    {
        var bitmap = ImagePreprocessorTests.CreateBitmap(1600, 500);

        using var canvas = new SKCanvas(bitmap);
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 110) { Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        // Print the number in the grouped layout used on the card, so the grouping is exercised too.
        const string printed = "2 90 01 01 12 3456 7";
        canvas.DrawText(printed, 100, 300, font, paint);
        canvas.Flush();

        return bitmap;
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
