namespace IdCardReader.Api;

/// <summary>Named rate limiting policies.</summary>
public static class RateLimitPolicies
{
    /// <summary>Policy applied to the OCR scan endpoint.</summary>
    public const string Scan = "scan";
}
