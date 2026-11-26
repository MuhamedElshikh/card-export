using IdCardReader.Infrastructure.Imaging;
using SkiaSharp;

namespace IdCardReader.Infrastructure.IntegrationTests;

/// <summary>
/// Glyph matching is tested with synthetic rectangles whose width encodes the digit, so the tests
/// do not depend on the card font. Real cards are measured with the Eval tool.
/// </summary>
public sealed class GlyphDigitReaderTests
{
    private const string ValidId = "29001011234567";
    private const int GlyphHeight = 30;

    [Fact]
    public void Read_FindsTheRowOfFourteenGlyphsAndReturnsTheNumber()
    {
        var reader = BuildReader();
        using var bitmap = DrawRow(ValidId, extraNoise: true);

        var read = reader.Read(bitmap);

        Assert.NotNull(read);
        Assert.Equal(ValidId, read!.Value.Value);
        Assert.True(read.Value.MeanDistance < GlyphDigitReader.MaximumMeanDistance);
    }

    [Fact]
    public void Read_ReturnsNullWhenThereAreNotFourteenGlyphs()
    {
        var reader = BuildReader();
        using var bitmap = DrawRow(ValidId[..13], extraNoise: false);

        Assert.Null(reader.Read(bitmap));
    }

    [Fact]
    public void Read_ReturnsNullWhenTheTemplateBankIsEmpty()
    {
        var reader = new GlyphDigitReader(Array.Empty<(char, float[])>());
        using var bitmap = DrawRow(ValidId, extraNoise: false);

        Assert.False(reader.HasTemplates);
        Assert.Null(reader.Read(bitmap));
    }

    [Fact]
    public void Read_RejectsDigitsThatAreNotAValidNationalId()
    {
        var reader = BuildReader();
        // Governorate 99 does not exist, so the domain parser must refuse the reading.
        using var bitmap = DrawRow("29001019912345", extraNoise: false);

        Assert.Null(reader.Read(bitmap));
    }

    [Fact]
    public void Classify_RefusesGlyphsFarFromEveryTemplate()
    {
        var reader = BuildReader();
        var blank = Enumerable.Range(0, 14)
            .Select(_ => Enumerable.Repeat(1f, GlyphDigitReader.GlyphSize * GlyphDigitReader.GlyphSize).ToArray())
            .ToArray();

        Assert.Null(reader.Classify(blank));
    }

    [Fact]
    public void FromJson_LoadsTheEmbeddedTemplateBank()
    {
        var reader = GlyphDigitReader.LoadDefault();

        Assert.True(reader.HasTemplates);
    }

    private static GlyphDigitReader BuildReader()
    {
        var templates = new List<(char, float[])>();
        for (var digit = 0; digit <= 9; digit++)
        {
            templates.Add(((char)('0' + digit), GlyphDigitReader.Features(Mask(digit), GlyphHeight)));
        }

        return new GlyphDigitReader(templates);
    }

    private static GlyphDigitReader.GlyphMask Mask(int digit)
    {
        var width = 6 + (digit * 2);
        return new GlyphDigitReader.GlyphMask(width, GlyphHeight, Enumerable.Repeat(true, width * GlyphHeight).ToArray());
    }

    private static SKBitmap DrawRow(string digits, bool extraNoise)
    {
        var bitmap = new SKBitmap(new SKImageInfo(700, 200, SKColorType.Gray8, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.White);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };

        var x = 20;
        foreach (var character in digits)
        {
            var width = 6 + ((character - '0') * 2);
            canvas.DrawRect(x, 80, width, GlyphHeight, paint);
            x += width + 14;
        }

        if (extraNoise)
        {
            // A thin rule and a speck must not be mistaken for number glyphs.
            canvas.DrawRect(10, 150, 600, 1, paint);
            canvas.DrawRect(650, 20, 2, 2, paint);
        }

        return bitmap;
    }
}
