using IdCardReader.Application;
using IdCardReader.Domain;

namespace IdCardReader.Api.Tests;

/// <summary>Deterministic OCR result so the HTTP tests never touch Tesseract.</summary>
public sealed class StubIdCardOcrService : IIdCardOcrService
{
    /// <summary>National ID returned by the stub (synthetic, not a real person).</summary>
    public const string NationalId = "29001011234567";

    /// <inheritdoc />
    public Task<IdCardScanResult> ScanAsync(
        byte[] frontImageBytes,
        byte[] backImageBytes,
        CancellationToken cancellationToken = default)
    {
        var parsed = NationalIdNumber.Create(NationalId);
        var derived = NationalIdResult.FromParsedNumber(parsed, "TEST SUBJECT", "TEST ADDRESS");

        return Task.FromResult(new IdCardScanResult
        {
            NationalId = derived.NationalIdNumber,
            FullName = derived.FullName,
            Address = derived.Address,
            Occupation = "مهندس",
            MaritalStatus = "متزوج",
            ExpiryDate = new DateOnly(2030, 1, 1),
            NationalIdValid = true,
            LowConfidence = false,
            BirthDate = derived.BirthDate,
            Gender = derived.Gender,
            Governorate = derived.Governorate,
            GovernorateEnglish = derived.GovernorateEnglish,
            GovernorateArabic = derived.GovernorateArabic
        });
    }
}
