using System.Security.Cryptography;
using IdCardReader.Application.IdCards;
using Microsoft.Extensions.Options;

namespace IdCardReader.Infrastructure.Storage;

/// <summary>Image storage configuration. Disabled by default.</summary>
public sealed class StorageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Storage";

    /// <summary>When false (default) no image is ever written to disk.</summary>
    public bool StoreImage { get; set; }

    /// <summary>Absolute or app-relative directory for encrypted blobs.</summary>
    public string? Path { get; set; }

    /// <summary>Base64 encoded 32-byte AES key. Required only when <see cref="StoreImage"/> is true.</summary>
    public string? EncryptionKey { get; set; }
}

/// <summary>
/// AES-GCM encrypted image store. Files live outside the web root; only an opaque relative
/// reference is persisted in the database.
/// </summary>
public sealed class EncryptedImageStore : IImageStore
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[]? _key;
    private readonly string _root;

    /// <summary>Creates the store and validates the configuration.</summary>
    public EncryptedImageStore(IOptions<StorageOptions> options)
    {
        var settings = options.Value;
        var configuredPath = string.IsNullOrWhiteSpace(settings.Path)
            ? System.IO.Path.Combine(AppContext.BaseDirectory, "App_Data", "idcards")
            : settings.Path;
        _root = System.IO.Path.GetFullPath(configuredPath!);

        if (!settings.StoreImage)
        {
            _key = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.EncryptionKey))
        {
            throw new InvalidOperationException(
                "Storage:EncryptionKey must be set to a base64 encoded 32-byte key when Storage:StoreImage is true.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(settings.EncryptionKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Storage:EncryptionKey must be valid base64.", ex);
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException("Storage:EncryptionKey must decode to exactly 32 bytes (AES-256).");
        }

        _key = key;
    }

    /// <inheritdoc />
    public bool IsEnabled => _key is not null;

    /// <inheritdoc />
    public async Task<string> SaveAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        if (_key is null)
        {
            throw new InvalidOperationException("Image storage is disabled.");
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[content.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, content, cipher, tag);
        }

        var relativeDirectory = DateTime.UtcNow.ToString("yyyy/MM");
        var directory = System.IO.Path.Combine(_root, relativeDirectory.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);

        var fileName = $"{Guid.NewGuid():N}.bin";
        var payload = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        cipher.CopyTo(payload, NonceSize + TagSize);

        await File.WriteAllBytesAsync(System.IO.Path.Combine(directory, fileName), payload, cancellationToken)
            .ConfigureAwait(false);

        return $"{relativeDirectory}/{fileName}";
    }
}
