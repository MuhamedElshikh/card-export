namespace IdCardReader.Infrastructure.Imaging;

/// <summary>
/// The uploaded bytes passed the magic byte check but could not be decoded as an image,
/// or the image cannot be prepared for OCR. Surfaced to the client as a 400, not a 500.
/// </summary>
public sealed class ImageDecodeException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ImageDecodeException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner cause.</summary>
    public ImageDecodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
