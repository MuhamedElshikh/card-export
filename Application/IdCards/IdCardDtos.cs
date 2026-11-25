namespace IdCardReader.Application.IdCards;

/// <summary>
/// Preview returned by the scan endpoint. Nothing is persisted.
/// All derived values come from the national ID number, never from OCR text.
/// </summary>
public sealed record ScanResultDto(
    string? NationalId,
    string? FullName,
    string? Address,
    bool NationalIdValid,
    bool LowConfidence,
    DateOnly? BirthDate,
    bool? IsMale,
    string? GovernorateCode,
    string? GovernorateEnglish,
    string? GovernorateArabic);

/// <summary>
/// Payload for creating a stored ID card record.
/// The national ID is re-validated on the server; derived fields are never trusted from the client.
/// </summary>
public sealed record SaveIdCardRequest(
    string NationalId,
    string FullName,
    string Address,
    bool LowConfidence,
    string? ImageBase64 = null);

/// <summary>Stored ID card representation.</summary>
public sealed record IdCardDto(
    int Id,
    string NationalId,
    string FullName,
    string Address,
    DateOnly BirthDate,
    bool IsMale,
    string GovernorateCode,
    string GovernorateEnglish,
    string GovernorateArabic,
    bool LowConfidence,
    DateTime CreatedAt);

/// <summary>Generic paged response envelope.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    /// <summary>Total number of pages for the requested page size.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
