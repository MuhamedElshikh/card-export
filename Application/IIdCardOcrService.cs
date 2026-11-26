using IdCardReader.Domain;

namespace IdCardReader.Application;

/// <summary>
/// Interface for OCR services that extract Egyptian national ID data from images.
/// </summary>
public interface IIdCardOcrService
{
    /// <summary>
    /// Extracts data from the front and back of an Egyptian national ID card.
    /// Returns a preview with the national ID (which must be correct)
    /// and best-effort fields that the user can review/edit.
    /// </summary>
    /// <param name="frontImageBytes">The front image bytes (JPEG/PNG, max 5MB)</param>
    /// <param name="backImageBytes">The back image bytes (JPEG/PNG, max 5MB)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>IdCardScanResult with preview data</returns>
    Task<IdCardScanResult> ScanAsync(
        byte[] frontImageBytes,
        byte[] backImageBytes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an ID card scan operation.
/// Contains the national ID (which must be correct),
/// name/address (best-effort OCR, user-reviewed),
/// and derived information (birth date, gender, governorate).
/// </summary>
public class IdCardScanResult
{
    /// <summary>
    /// The 14-digit national ID number (normalized ASCII), or null if not found.
    /// </summary>
    public string? NationalId { get; set; }

    /// <summary>
    /// Full name as it appears on the ID (best-effort OCR).
    /// </summary>
    public string? FullName { get; set; }

    /// <summary>
    /// Address as it appears on the ID (best-effort OCR).
    /// </summary>
    public string? Address { get; set; }

    /// <summary>Occupation read from the back of the card, or null if not recognized.</summary>
    public string? Occupation { get; set; }

    /// <summary>Marital status read from the card, or null if not recognized.</summary>
    public string? MaritalStatus { get; set; }

    /// <summary>Expiry date read from the back of the card, or null if not recognized.</summary>
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>
    /// Whether the national ID number is valid according to domain rules.
    /// </summary>
    public bool NationalIdValid { get; set; }

    /// <summary>
    /// Low confidence flag - the OCR result for the national ID should be highlighted in UI.
    /// </summary>
    public bool LowConfidence { get; set; }

    /// <summary>
    /// Parsed birth date (derived from national ID number).
    /// </summary>
    public DateOnly? BirthDate { get; set; }

    /// <summary>
    /// Parsed gender (derived from national ID number).
    /// </summary>
    public string? Gender { get; set; }

    /// <summary>
    /// Parsed governorate (derived from national ID number).
    /// </summary>
    public string? Governorate { get; set; }

    /// <summary>
    /// Parsed governorate in English.
    /// </summary>
    public string? GovernorateEnglish { get; set; }

    /// <summary>
    /// Parsed governorate in Arabic.
    /// </summary>
    public string? GovernorateArabic { get; set; }
}