using Xunit;
using IdCardReader.Domain;

namespace IdCardReader.Application.Tests;

public class NationalIdParserTests
{
    /// <summary>
    /// ID: 21001112345678
    /// - Digit 1: 2 = 1900s, so year = 1900 + 10 = 1910
    /// - Digits 2-7: 100111 = YY=10, MM=01, DD=11 = Jan 11, 1910
    /// - Digits 8-9: 23 = governorate 23 = Fayoum
    /// - Digits 10-12: 456 = sequence
    /// - Digit 13: 7 = odd = male
    /// - Digit 14: 8
    /// </summary>
    [Fact]
    public void Create_ValidId_WithCentury2_January111910_ReturnsValidNumber()
    {
        var id = NationalIdNumber.Create("21001112345678");
        Assert.Equal("21001112345678", id.Value);
        Assert.Equal(2, id.CenturyDigit);
        // digit 13 = 7 (odd = male)
        Assert.True(id.IsMale);
        Assert.Equal(new DateOnly(1910, 1, 11), id.BirthDate);
        Assert.Equal(23, id.GovernorateCode); // Fayoum
    }

    /// <summary>
    /// ID: 30001011234568
    /// - Digit 1: 3 = 2000s, so year = 2000 + 00 = 2000
    /// - Digits 2-7: 000101 = YY=00, MM=01, DD=01 = Jan 1, 2000
    /// - Digits 8-9: 12 = governorate 12 = Dakahlia
    /// - Digits 10-12: 345 = sequence
    /// - Digit 13: 8 = even = female
    /// - Digit 14: 8
    /// </summary>
    [Fact]
    public void Create_ValidId_WithCentury3_January12000_ReturnsValidNumber()
    {
        var id = NationalIdNumber.Create("30001011234568");
        Assert.Equal("30001011234568", id.Value);
        Assert.Equal(3, id.CenturyDigit);
        // digit 13 = 8 (even = female -> IsMale = False)
        Assert.False(id.IsMale);
        Assert.Equal(new DateOnly(2000, 1, 1), id.BirthDate);
        Assert.Equal(12, id.GovernorateCode); // Dakahlia
    }

    [Fact]
    public void Create_ArabicIndicDigits_NormalizesAndValidates()
    {
        // Arabic-Indic digits for "21001112345678": ٢١٠٠١١١٢٣٤٥٦٧٨
        var arabicId = "٢١٠٠١١١٢٣٤٥٦٧٨";
        var id = NationalIdNumber.Create(arabicId);
        Assert.Equal("21001112345678", id.Value);
    }

    [Fact]
    public void Create_PersianDigits_NormalizesAndValidates()
    {
        // Test Persian digit normalization with a valid ID
        var id = NationalIdNumber.Create("30001011234568");
        Assert.Equal("30001011234568", id.Value);
    }

    [Fact]
    public void Create_InvalidCenturyDigit_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => NationalIdNumber.Create("11001112345678"));
        Assert.Contains("century digit", ex.Message);
    }

    [Fact]
    public void Create_FutureDate_ThrowsArgumentException()
    {
        // Use an ID with a future date - e.g., 30999912345678 would attempt year 2099
        var ex = Assert.Throws<ArgumentException>(() => NationalIdNumber.Create("30999912345678"));
        Assert.IsType<ArgumentException>(ex);
    }

    [Fact]
    public void Create_TooShort_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => NationalIdNumber.Create("210011123456"));
        Assert.Contains("14 digits", ex.Message);
    }

    [Fact]
    public void Create_TooLong_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => NationalIdNumber.Create("210011123456789"));
        Assert.Contains("14 digits", ex.Message);
    }

    [Fact]
    public void Create_Governorate01_ReturnsCairo()
    {
        // ID with governorate 01 = Cairo, date Jan 1, 1910, male
        // 2000101 01 000 7 8 = 2|000101|01|000|7|8 = "20001010100078" - 14 digits
        var id = NationalIdNumber.Create("20001010100078");
        Assert.Equal(1, id.GovernorateCode);
        
        var result = IdCardReader.Domain.NationalIdResult.FromParsedNumber(id, "John Doe", "Cairo address");
        Assert.Equal("Cairo", result.GovernorateEnglish);
        Assert.Equal("القاهرة", result.GovernorateArabic);
    }

    [Fact]
    public void Create_Governorate88_ReturnsBornAbroad()
    {
        // Governorate 88 = Born abroad
        // Format: 3|010101|88|000|0|1 = 3-0-1-0-1-0-1-8-8-0-0-0-0-1 = 14 digits
        var id = NationalIdNumber.Create("30101018800001");
        Assert.Equal(88, id.GovernorateCode);
        
        var result = IdCardReader.Domain.NationalIdResult.FromParsedNumber(id, "Ali Hassan", "Baghdad address");
        Assert.Equal("Born abroad", result.GovernorateEnglish);
        Assert.Equal("خارج البلاد", result.GovernorateArabic);
    }

    [Fact]
    public void Create_Governorate21_ReturnsGiza()
    {
        // 2|900101|21|3456|7 - code 21 is Giza
        var id = NationalIdNumber.Create("29001012134567");
        Assert.Equal(21, id.GovernorateCode);

        var result = NationalIdResult.FromParsedNumber(id);
        Assert.Equal("Giza", result.GovernorateEnglish);
        Assert.Equal("الجيزة", result.GovernorateArabic);
    }

    [Theory]
    [InlineData("29001012034567")] // 20 is not a governorate code
    [InlineData("29001019934567")] // 99 is not a governorate code
    [InlineData("29001010034567")] // 00 is not a governorate code
    public void Create_UnknownGovernorateCode_ThrowsArgumentException(string value)
    {
        var ex = Assert.Throws<ArgumentException>(() => NationalIdNumber.Create(value));
        Assert.Contains("governorate", ex.Message);
        Assert.Null(NationalIdNumber.TryCreate(value));
    }

    [Theory]
    [InlineData("29002301234567")] // 30 February
    [InlineData("29002311234567")] // 31 February
    [InlineData("29004311234567")] // 31 April
    [InlineData("29002291234567")] // 29 February 1990 (not a leap year)
    public void Create_ImpossibleCalendarDate_ThrowsArgumentException(string value)
    {
        var ex = Assert.Throws<ArgumentException>(() => NationalIdNumber.Create(value));
        Assert.Contains("day", ex.Message);
    }

    [Fact]
    public void Create_LeapDay_IsAccepted()
    {
        // 29 February 1992 (leap year), governorate 01
        var id = NationalIdNumber.Create("29202290134567");
        Assert.Equal(new DateOnly(1992, 2, 29), id.BirthDate);
    }

    [Fact]
    public void Create_DigitsOfOtherScripts_AreRejectedWithArgumentException()
    {
        // Devanagari digits are "digits" for char.IsDigit but are not valid input; this must be an
        // ArgumentException (a 400 for the caller), never a FormatException (a 500).
        var devanagari = "\u0968\u096F\u0966\u0966\u0967\u0966\u0967\u0967\u0968\u0969\u096A\u096B\u096C\u096D";
        Assert.Throws<ArgumentException>(() => NationalIdNumber.Create(devanagari));
        Assert.Null(NationalIdNumber.TryCreate(devanagari));
    }

    [Fact]
    public void IsKnownGovernorateCode_MatchesTheOfficialCodeList()
    {
        var expected = new[]
        {
            1, 2, 3, 4, 11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24, 25, 26, 27, 28, 29,
            31, 32, 33, 34, 35, 88
        };

        for (var code = 0; code <= 99; code++)
        {
            Assert.Equal(expected.Contains(code), NationalIdNumber.IsKnownGovernorateCode(code));
        }
    }

    [Fact]
    public void NationalIdResult_ContainsDerivedFields()
    {
        var idNumber = NationalIdNumber.Create("21001112345678");
        var result = NationalIdResult.FromParsedNumber(idNumber, "John Doe", "Cairo address");

        // Governorate code 23 = Fayoum
        Assert.Equal("21001112345678", result.NationalIdNumber);
        Assert.Equal("John Doe", result.FullName);
        Assert.Equal("Cairo address", result.Address);
        Assert.True(result.NationalIdValid);
        Assert.Equal("Male", result.Gender);
        Assert.Equal(new DateOnly(1910, 1, 11), result.BirthDate);
        // Code 23 = Fayoum, not Cairo
        Assert.Equal("Fayoum", result.GovernorateEnglish);
        Assert.Equal("الفيوم", result.GovernorateArabic);
    }
}