using IdCardReader.Infrastructure.Imaging;

namespace IdCardReader.Infrastructure.IntegrationTests;

/// <summary>
/// The name and address shown for review must come from labelled text on the card.
/// Guessing a name from unrelated Arabic text is worse than leaving the field empty.
/// </summary>
public sealed class ArabicTextFieldsTests
{
    [Fact]
    public void Select_ReadsNameAfterFullArabicLabel()
    {
        const string text = "الاسم بالكامل: أحمد محمد علي\nالعنوان: شارع النيل، الدقهلية";

        var (name, address) = ArabicTextFields.Select(text);

        Assert.Equal("أحمد محمد علي", name);
        Assert.Equal("شارع النيل، الدقهلية", address);
    }

    [Fact]
    public void Select_NeverReturnsLabelRemainderAsName()
    {
        const string text = "الاسم بالكامل\nأحمد محمد علي";

        var (name, _) = ArabicTextFields.Select(text);

        Assert.Equal("أحمد محمد علي", name);
    }

    [Fact]
    public void Select_DoesNotGuessNameFromUnlabelledText()
    {
        const string text = "جمهورية مصر العربية\nبطاقة رقم قومي\nالدقهلية";

        var (name, _) = ArabicTextFields.Select(text);

        Assert.Null(name);
    }

    [Fact]
    public void Select_StillReadsNameOnLineAfterBareLabel()
    {
        const string text = "الاسم\nأحمد محمد علي\nالعنوان\nشارع النيل";

        var (name, address) = ArabicTextFields.Select(text);

        Assert.Equal("أحمد محمد علي", name);
        Assert.Equal("شارع النيل", address);
    }
}
