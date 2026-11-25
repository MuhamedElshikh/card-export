namespace IdCardReader.Application.IdCards;

/// <summary>
/// Detects real JPEG/PNG payloads by magic bytes so that a renamed text file cannot reach the OCR engine.
/// </summary>
public static class ImageSignature
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>Returns the detected format name, or null when the payload is neither JPEG nor PNG.</summary>
    public static string? Detect(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return "jpeg";
        }

        if (content.Length >= PngSignature.Length && content[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return "png";
        }

        return null;
    }

    /// <summary>True when the payload is a real JPEG or PNG image.</summary>
    public static bool IsSupported(ReadOnlySpan<byte> content) => Detect(content) is not null;
}
