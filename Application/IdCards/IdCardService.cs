using IdCardReader.Domain;

namespace IdCardReader.Application.IdCards;

/// <summary>Outcome of a create request.</summary>
public enum CreateIdCardStatus
{
    /// <summary>The card was stored.</summary>
    Created,

    /// <summary>The national ID failed domain validation.</summary>
    InvalidNationalId,

    /// <summary>A card with the same national ID already exists.</summary>
    Duplicate,

    /// <summary>The request carried an image but image storage is disabled.</summary>
    ImageStorageDisabled,

    /// <summary>The supplied image was not a valid JPEG/PNG payload.</summary>
    InvalidImage
}

/// <summary>Result of a create request, including a translatable error code when it failed.</summary>
public sealed record CreateIdCardResult(
    CreateIdCardStatus Status,
    IdCardDto? IdCard,
    string? ErrorCode,
    string? ErrorDetail);

/// <summary>
/// Application service for ID cards: builds scan previews, re-validates and stores reviewed cards,
/// and serves paged reads. All derived fields are recomputed from the national ID number.
/// </summary>
public sealed class IdCardService
{
    /// <summary>Hard upper bound for the page size.</summary>
    public const int MaxPageSize = 100;

    private readonly IIdCardRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IImageStore _imageStore;

    /// <summary>Creates the service.</summary>
    public IdCardService(
        IIdCardRepository repository,
        IUnitOfWork unitOfWork,
        IImageStore imageStore)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _imageStore = imageStore;
    }

    /// <summary>
    /// Converts a raw OCR result into the API preview contract.
    /// Derived values are computed from the national ID number only; unparsable numbers are dropped.
    /// </summary>
    public ScanResultDto ToScanResult(IdCardScanResult ocr)
    {
        NationalIdNumber? parsed = null;
        if (!string.IsNullOrWhiteSpace(ocr.NationalId))
        {
            try
            {
                parsed = NationalIdNumber.Create(ocr.NationalId);
            }
            catch (ArgumentException)
            {
                parsed = null;
            }
        }

        return new ScanResultDto(
            NationalId: parsed?.Value,
            FullName: TrimToNull(ocr.FullName),
            Address: TrimToNull(ocr.Address),
            Occupation: TrimToNull(ocr.Occupation),
            MaritalStatus: TrimToNull(ocr.MaritalStatus),
            ExpiryDate: ocr.ExpiryDate,
            NationalIdValid: parsed is not null,
            LowConfidence: ocr.LowConfidence || parsed is null,
            BirthDate: parsed?.BirthDate,
            IsMale: parsed?.IsMale,
            GovernorateCode: parsed is null ? null : parsed.Value.GovernorateCode.ToString("00"),
            GovernorateEnglish: parsed?.GetGovernorateEnglish(),
            GovernorateArabic: parsed?.GetGovernorateArabic());
    }

    /// <summary>Stores a reviewed card after re-validating the national ID on the server.</summary>
    public async Task<CreateIdCardResult> CreateAsync(
        SaveIdCardRequest request,
        CancellationToken cancellationToken = default)
    {
        NationalIdNumber parsed;
        try
        {
            parsed = NationalIdNumber.Create(request.NationalId);
        }
        catch (ArgumentException ex)
        {
            return new CreateIdCardResult(
                CreateIdCardStatus.InvalidNationalId,
                null,
                "invalid_national_id",
                ex.Message);
        }

        byte[]? imageBytes = null;
        if (!string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            if (!_imageStore.IsEnabled)
            {
                return new CreateIdCardResult(
                    CreateIdCardStatus.ImageStorageDisabled,
                    null,
                    "image_storage_disabled",
                    "Image storage is disabled on this server.");
            }

            try
            {
                imageBytes = Convert.FromBase64String(request.ImageBase64);
            }
            catch (FormatException)
            {
                return new CreateIdCardResult(
                    CreateIdCardStatus.InvalidImage,
                    null,
                    "invalid_image",
                    "ImageBase64 is not a valid base64 payload.");
            }

            if (!ImageSignature.IsSupported(imageBytes))
            {
                return new CreateIdCardResult(
                    CreateIdCardStatus.InvalidImage,
                    null,
                    "unsupported_image_format",
                    "Only JPEG and PNG images are accepted.");
            }
        }

        if (await _repository.ExistsAsync(parsed.Value, cancellationToken).ConfigureAwait(false))
        {
            return new CreateIdCardResult(
                CreateIdCardStatus.Duplicate,
                null,
                "duplicate_national_id",
                "A card with this national ID number already exists.");
        }

        var idCard = new IdCard
        {
            NationalId = parsed.Value,
            FullName = request.FullName?.Trim() ?? string.Empty,
            Address = request.Address?.Trim() ?? string.Empty,
            Occupation = TrimToNull(request.Occupation),
            MaritalStatus = TrimToNull(request.MaritalStatus),
            ExpiryDate = request.ExpiryDate,
            BirthDate = parsed.BirthDate,
            IsMale = parsed.IsMale,
            GovernorateCode = parsed.GovernorateCode.ToString("00"),
            GovernorateEnglish = parsed.GetGovernorateEnglish(),
            GovernorateArabic = parsed.GetGovernorateArabic(),
            LowConfidence = request.LowConfidence,
            CreatedAt = DateTime.UtcNow
        };

        if (imageBytes is not null)
        {
            idCard.ImageReference = await _imageStore.SaveAsync(imageBytes, cancellationToken).ConfigureAwait(false);
        }

        await _repository.AddAsync(idCard, cancellationToken).ConfigureAwait(false);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateNationalIdException)
        {
            return new CreateIdCardResult(
                CreateIdCardStatus.Duplicate,
                null,
                "duplicate_national_id",
                "A card with this national ID number already exists.");
        }

        return new CreateIdCardResult(CreateIdCardStatus.Created, IdCardMapper.ToDto(idCard), null, null);
    }

    /// <summary>Returns a page of stored cards.</summary>
    public async Task<PagedResult<IdCardDto>> ListAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safePageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var paged = await _repository
            .SearchAsync(search, safePage, safePageSize, cancellationToken)
            .ConfigureAwait(false);

        var items = paged.Items.Select(IdCardMapper.ToDto).ToList();
        return new PagedResult<IdCardDto>(items, safePage, safePageSize, paged.TotalCount);
    }

    /// <summary>Returns a single stored card, or null.</summary>
    public async Task<IdCardDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var card = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return card is null ? null : IdCardMapper.ToDto(card);
    }

    private static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
