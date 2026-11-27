using IdCardReader.Infrastructure.Imaging;

namespace IdCardReader.Infrastructure.IntegrationTests;

/// <summary>
/// The name and address shown for review must match the card's text layout.
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
    public void Select_RejectsFragmentedOcrNoiseAsName()
    {
        // Real failure seen on a phone photo: OCR noise produced two short shards that passed
        // every old check and became the "name".
        const string text = "بم عاله\n٤ ح صابر عابد - سكة الوايلي";

        var (name, _) = ArabicTextFields.Select(text);

        Assert.Null(name);
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

    [Fact]
    public void Select_ExtractsUnlabelledNameSplitAcrossAdjacentLines()
    {
        const string text =
            "محمد\nعبدالفتاح عبد الواحد البهى\n٤ ح صابر عابد - سكة الوايلي\nحدائق القبة - القاهرة";

        var (name, _) = ArabicTextFields.Select(text);

        Assert.Equal("محمد عبدالفتاح عبد الواحد البهى", name);
    }

    [Fact]
    public void Select_DoesNotTreatAnUnlabelledAddressAsAName()
    {
        var (name, _) = ArabicTextFields.Select("حدائق القبة - القاهرة");

        Assert.Null(name);
    }

    [Fact]
    public void Select_TakesTheLinesAfterAnUnlabelledNameAsTheAddress()
    {
        const string text =
            "محمد\nعبدالفتاح عبد الواحد البهى\n٤ ح صابر عابد - سكة الوايلي\nحدائق القبة - القاهرة";

        var (name, address) = ArabicTextFields.Select(text);

        Assert.Equal("محمد عبدالفتاح عبد الواحد البهى", name);
        Assert.Equal("٤ ح صابر عابد - سكة الوايلي حدائق القبة - القاهرة", address);
    }

    [Fact]
    public void Select_DoesNotInventAnAddressWhenThereIsNoName()
    {
        var (name, address) = ArabicTextFields.Select("حدائق القبة - القاهرة");

        Assert.Null(name);
        Assert.Null(address);
    }
}

public sealed class ArabicTextFieldsPositionedTests
{
    [Fact]
    public void SelectPositioned_OrdersLinesByTheirBoxesBeforeSelecting()
    {
        // Engine output order is not guaranteed; selection must follow the line positions.
        var lines = new[]
        {
            new ArabicTextFields.PositionedLine("شارع النيل", Top: 300, Confidence: 0.9),
            new ArabicTextFields.PositionedLine("أحمد محمد علي", Top: 120, Confidence: 0.9),
            new ArabicTextFields.PositionedLine("الاسم", Top: 100, Confidence: 0.9),
            new ArabicTextFields.PositionedLine("العنوان", Top: 280, Confidence: 0.9),
        };

        var (name, address) = ArabicTextFields.SelectPositioned(lines);

        Assert.Equal("أحمد محمد علي", name);
        Assert.Equal("شارع النيل", address);
    }

    [Fact]
    public void SelectPositioned_DropsLowConfidenceGarbage()
    {
        // The "بم عاله" failure shape: a fragmented low-confidence line read off the patterned
        // background must not become the name when the real name lines read confidently.
        var lines = new[]
        {
            new ArabicTextFields.PositionedLine("بم عاله", Top: 60, Confidence: 0.3),
            new ArabicTextFields.PositionedLine("محمد", Top: 100, Confidence: 0.9),
            new ArabicTextFields.PositionedLine("عبدالفتاح عبد الواحد البهى", Top: 140, Confidence: 0.92),
            new ArabicTextFields.PositionedLine("٤ ح صابر عابد - سكة الوايلي", Top: 200, Confidence: 0.88),
        };

        var (name, address) = ArabicTextFields.SelectPositioned(lines);

        Assert.Equal("محمد عبدالفتاح عبد الواحد البهى", name);
        Assert.Equal("٤ ح صابر عابد - سكة الوايلي", address);
    }

    [Fact]
    public void SelectPositioned_ReturnsNullsWhenEveryLineIsLowConfidence()
    {
        var lines = new[]
        {
            new ArabicTextFields.PositionedLine("بم عاله", Top: 100, Confidence: 0.3),
            new ArabicTextFields.PositionedLine("ح صابر عابد", Top: 140, Confidence: 0.4),
        };

        var (name, address) = ArabicTextFields.SelectPositioned(lines);

        Assert.Null(name);
        Assert.Null(address);
    }
}

public sealed class ArabicTextFieldsPaddlePhrasingTests
{
    [Fact]
    public void Select_SplitsNameAndAddressBoxedIntoOneLine()
    {
        // PP-OCR's detector can box the name and the first address line together: the house
        // number then fails the name checks even though both fields were read perfectly.
        const string text =
            "محمد عبدالفتاح عبد الواحد البهى ٤ ح صابر عابد - سكة الوايلي\nحدائق القبة - القاهرة";

        var (name, address) = ArabicTextFields.Select(text);

        Assert.Equal("محمد عبدالفتاح عبد الواحد البهى", name);
        Assert.Equal("٤ ح صابر عابد - سكة الوايلي حدائق القبة - القاهرة", address);
    }

    [Fact]
    public void Select_SplitLineDoesNotTurnFragmentedNoiseIntoAName()
    {
        // The digit split must keep the garbage guards: a digit line without a real name
        // prefix still yields nothing.
        const string text = "بم عاله\n٤ ح صابر عابد - سكة الوايلي";

        var (name, _) = ArabicTextFields.Select(text);

        Assert.Null(name);
    }

    [Fact]
    public void Select_IgnoresExtendedDiacriticsFromNeuralReaders()
    {
        // PP-OCR emits the superscript alef and annotation marks for decorated fonts; they are
        // not letters and must not break the ratio checks.
        const string text = "مٓحمد عبدالفتاح عبد الواحد البهىٓ\n٤ ح صابر عابد";

        var (name, _) = ArabicTextFields.Select(text);

        Assert.Equal("محمد عبدالفتاح عبد الواحد البهى", name);
    }

    [Fact]
    public void Select_NeverReturnsCardVocabularyAsAName()
    {
        // The card's own headings ("وزارة الداخلية") are clean, confident text that is not a
        // name; the person's name further down must win.
        const string text = "وزارة الداخلية\nمحمد عبدالفتاح عبد الواحد البهى\n٤ ح صابر عابد";

        var (name, _) = ArabicTextFields.Select(text);

        Assert.Equal("محمد عبدالفتاح عبد الواحد البهى", name);
    }
}

public sealed class ArabicTextFieldsUnlabelledAddressTests
{
    [Fact]
    public void Select_ReturnsADigitAnchoredAddressEvenWhenTheNameIsLost()
    {
        // The name's decorated font can defeat every reader while the address reads cleanly;
        // the house number anchors the address without any help from the name.
        const string text =
            "محمد\nبم عاله\n٤ ح صابر عابد - سكة الوايلي\nحدائق القبة - القاهرة";

        var (name, address) = ArabicTextFields.Select(text);

        Assert.Null(name);
        Assert.Equal("٤ ح صابر عابد - سكة الوايلي حدائق القبة - القاهرة", address);
    }

    [Fact]
    public void Select_DoesNotTakeTheNumberOrADateAsAnAddress()
    {
        const string text = "٢٩٤٠٥٠٥٠١٠٢٨٨١١\n٢٠٣٥/١٢/٣١";

        var (_, address) = ArabicTextFields.Select(text);

        Assert.Null(address);
    }

    [Fact]
    public void Select_RejectsAMisreadHeaderEvenWithNormalizedSpellings()
    {
        // A header read with ة/ه or ى/ي confusion is still card vocabulary, never a name.
        const string text = "جمهوريه مصر العربيه\nمحمد عبدالفتاح عبد الواحد البهى\n٤ ح صابر عابد";

        var (name, _) = ArabicTextFields.Select(text);

        Assert.Equal("محمد عبدالفتاح عبد الواحد البهى", name);
    }

    [Fact]
    public void Select_DoesNotInventAnAddressWithoutADigitAnchor()
    {
        // The unlabelled-address path requires the house-number anchor; a bare district line
        // on its own is not enough.
        var (_, address) = ArabicTextFields.Select("حدائق القبة - القاهرة");

        Assert.Null(address);
    }
}
