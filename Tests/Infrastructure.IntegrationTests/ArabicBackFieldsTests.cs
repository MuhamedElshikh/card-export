using IdCardReader.Infrastructure.Imaging;

namespace IdCardReader.Infrastructure.IntegrationTests;

public sealed class ArabicBackFieldsTests
{
    [Fact]
    public void Select_ExtractsLabeledValuesAndNormalizesArabicExpiryDigits()
    {
        var fields = ArabicBackFields.Select(
            "المهنة: مهندس\nالحالة الاجتماعية: متزوج\nتاريخ الانتهاء: ٣١/١٢/٢٠٣٠");

        Assert.Equal("مهندس", fields.Occupation);
        Assert.Equal("متزوج", fields.MaritalStatus);
        Assert.Equal(new DateOnly(2030, 12, 31), fields.ExpiryDate);
    }

    [Fact]
    public void Select_ReadsValuesOnTheLineAfterTheirLabels()
    {
        var fields = ArabicBackFields.Select(
            "المهنة\nمحاسب\nالحالة الاجتماعية\nأعزب\nصالحة حتى\n2032-04-05");

        Assert.Equal("محاسب", fields.Occupation);
        Assert.Equal("أعزب", fields.MaritalStatus);
        Assert.Equal(new DateOnly(2032, 4, 5), fields.ExpiryDate);
    }

    [Fact]
    public void Select_ExtractsUnlabelledFieldsFromEgyptianCardBackLayout()
    {
        var fields = ArabicBackFields.Select(
            "بكالوريوس المعهد العالي لعلوم\nالحاسب ونظم المعلومات\nذكر مسلم أعزب\nالبطاقة سارية حتى ٢٠٣٢/٠٢/٢٨");

        Assert.Equal("بكالوريوس المعهد العالي لعلوم الحاسب ونظم المعلومات", fields.Occupation);
        Assert.Equal("أعزب", fields.MaritalStatus);
        Assert.Equal(new DateOnly(2032, 2, 28), fields.ExpiryDate);
    }

    [Fact]
    public void Select_LeavesUnlabelledOccupationNullWithoutAStatusRow()
    {
        var fields = ArabicBackFields.Select("بكالوريوس المعهد العالي لعلوم\nالحاسب ونظم المعلومات");

        Assert.Null(fields.Occupation);
    }

    [Fact]
    public void Select_ExtractsAUniqueUnlabelledBackDate()
    {
        var fields = ArabicBackFields.Select("ذكر مسلم أعزب\n٢٠٣٢/٠٢/٢٨");

        Assert.Equal(new DateOnly(2032, 2, 28), fields.ExpiryDate);
    }

    [Fact]
    public void Select_DoesNotGuessBetweenMultipleUnlabelledDates()
    {
        var fields = ArabicBackFields.Select("ذكر مسلم أعزب\n٢٠٣٢/٠٢/٢٨\n٢٠٢٠/٠١/٠١");

        Assert.Null(fields.ExpiryDate);
    }
}
