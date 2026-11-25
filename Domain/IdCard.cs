namespace IdCardReader.Domain;

/// <summary>
/// Persisted record of an Egyptian national ID card.
/// Only data the user explicitly confirmed is stored; images are optional and off by default.
/// </summary>
public class IdCard
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Normalized 14-digit ASCII national ID number (unique).</summary>
    public string NationalId { get; set; } = string.Empty;

    /// <summary>Full name as reviewed by the user.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Address as reviewed by the user (may be empty).</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Birth date derived from the national ID number.</summary>
    public DateOnly BirthDate { get; set; }

    /// <summary>Gender derived from digit 13 of the national ID number.</summary>
    public bool IsMale { get; set; }

    /// <summary>Two digit governorate code derived from digits 8-9.</summary>
    public string GovernorateCode { get; set; } = string.Empty;

    /// <summary>Governorate name in English.</summary>
    public string GovernorateEnglish { get; set; } = string.Empty;

    /// <summary>Governorate name in Arabic.</summary>
    public string GovernorateArabic { get; set; } = string.Empty;

    /// <summary>True when OCR confidence was below the accepted threshold at scan time.</summary>
    public bool LowConfidence { get; set; }

    /// <summary>
    /// Reference to the encrypted image blob when image storage is enabled.
    /// Null for the default configuration (no images are persisted).
    /// </summary>
    public string? ImageReference { get; set; }

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
