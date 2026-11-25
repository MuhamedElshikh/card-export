namespace IdCardReader.Application.IdCards;

/// <summary>
/// Optional encrypted image storage. Disabled by default so that no ID image is ever persisted
/// unless the operator explicitly opts in with <c>Storage:StoreImage=true</c>.
/// </summary>
public interface IImageStore
{
    /// <summary>True when image persistence is enabled and a valid encryption key is configured.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Encrypts and writes the image outside the web root.
    /// Returns an opaque reference that can be stored in the database.
    /// </summary>
    Task<string> SaveAsync(byte[] content, CancellationToken cancellationToken = default);
}

/// <summary>Thrown by the unit of work when the unique national ID index rejects an insert.</summary>
public sealed class DuplicateNationalIdException : Exception
{
    /// <summary>Creates the exception.</summary>
    public DuplicateNationalIdException(Exception? innerException = null)
        : base("A national ID card with the same national ID number already exists.", innerException)
    {
    }
}
