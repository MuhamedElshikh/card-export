using IdCardReader.Application;
using IdCardReader.Application.IdCards;
using IdCardReader.Domain;

namespace IdCardReader.Application.Tests;

/// <summary>Unit tests for the application service (no database, no OCR engine).</summary>
public sealed class IdCardServiceTests
{
    private const string ValidNationalId = "29001011234567";
    private const string ArabicIndicNationalId = "٢٩٠٠١٠١١٢٣٤٥٦٧";

    [Fact]
    public async Task CreateAsync_WithValidNationalId_DerivesFieldsFromNumber()
    {
        var repository = new FakeIdCardRepository();
        var service = CreateService(repository);

        var result = await service.CreateAsync(
            new SaveIdCardRequest(ValidNationalId, "  TEST SUBJECT  ", "  TEST ADDRESS  ", LowConfidence: false));

        Assert.Equal(CreateIdCardStatus.Created, result.Status);
        Assert.NotNull(result.IdCard);
        Assert.Equal(ValidNationalId, result.IdCard!.NationalId);
        Assert.Equal("TEST SUBJECT", result.IdCard.FullName);
        Assert.Equal("TEST ADDRESS", result.IdCard.Address);
        Assert.Equal(new DateOnly(1990, 1, 1), result.IdCard.BirthDate);
        Assert.False(result.IdCard.IsMale);
        Assert.Equal("12", result.IdCard.GovernorateCode);
        Assert.Equal("Dakahlia", result.IdCard.GovernorateEnglish);
        Assert.Equal("الدقهلية", result.IdCard.GovernorateArabic);
        Assert.Single(repository.Cards);
    }

    [Fact]
    public async Task CreateAsync_WithGizaNationalId_StoresGizaGovernorate()
    {
        var repository = new FakeIdCardRepository();
        var service = CreateService(repository);

        var result = await service.CreateAsync(
            new SaveIdCardRequest("29001012134567", "TEST SUBJECT", "TEST ADDRESS", LowConfidence: false));

        Assert.Equal(CreateIdCardStatus.Created, result.Status);
        Assert.Equal("21", result.IdCard!.GovernorateCode);
        Assert.Equal("Giza", result.IdCard.GovernorateEnglish);
        Assert.Equal("الجيزة", result.IdCard.GovernorateArabic);
    }

    [Fact]
    public async Task CreateAsync_WithUnknownGovernorateCode_ReturnsInvalidStatus()
    {
        var repository = new FakeIdCardRepository();
        var service = CreateService(repository);

        var result = await service.CreateAsync(
            new SaveIdCardRequest("29001019934567", "TEST SUBJECT", "TEST ADDRESS", LowConfidence: false));

        Assert.Equal(CreateIdCardStatus.InvalidNationalId, result.Status);
        Assert.Empty(repository.Cards);
    }

    [Fact]
    public async Task CreateAsync_WithArabicIndicDigits_NormalizesAndStores()
    {
        var repository = new FakeIdCardRepository();
        var service = CreateService(repository);

        var result = await service.CreateAsync(
            new SaveIdCardRequest(ArabicIndicNationalId, "TEST SUBJECT", "TEST ADDRESS", LowConfidence: false));

        Assert.Equal(CreateIdCardStatus.Created, result.Status);
        Assert.Equal(ValidNationalId, result.IdCard!.NationalId);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidNationalId_ReturnsInvalidStatus()
    {
        var repository = new FakeIdCardRepository();
        var service = CreateService(repository);

        var result = await service.CreateAsync(
            new SaveIdCardRequest("12345", "TEST SUBJECT", "TEST ADDRESS", LowConfidence: false));

        Assert.Equal(CreateIdCardStatus.InvalidNationalId, result.Status);
        Assert.Equal("invalid_national_id", result.ErrorCode);
        Assert.Empty(repository.Cards);
    }

    [Fact]
    public async Task CreateAsync_WhenNumberAlreadyExists_ReturnsDuplicate()
    {
        var repository = new FakeIdCardRepository();
        var service = CreateService(repository);
        var request = new SaveIdCardRequest(ValidNationalId, "TEST SUBJECT", "TEST ADDRESS", LowConfidence: false);

        var first = await service.CreateAsync(request);
        var second = await service.CreateAsync(request);

        Assert.Equal(CreateIdCardStatus.Created, first.Status);
        Assert.Equal(CreateIdCardStatus.Duplicate, second.Status);
        Assert.Equal("duplicate_national_id", second.ErrorCode);
        Assert.Single(repository.Cards);
    }

    [Fact]
    public async Task CreateAsync_WithImageWhenStorageDisabled_IsRejected()
    {
        var repository = new FakeIdCardRepository();
        var service = CreateService(repository);

        var result = await service.CreateAsync(new SaveIdCardRequest(
            ValidNationalId,
            "TEST SUBJECT",
            "TEST ADDRESS",
            LowConfidence: false,
            ImageBase64: Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })));

        Assert.Equal(CreateIdCardStatus.ImageStorageDisabled, result.Status);
        Assert.Empty(repository.Cards);
    }

    [Fact]
    public void ToScanResult_WithUnparsableNumber_DropsItAndFlagsLowConfidence()
    {
        var service = CreateService(new FakeIdCardRepository());

        var preview = service.ToScanResult(new IdCardScanResult
        {
            NationalId = "999",
            FullName = "  TEST SUBJECT  ",
            NationalIdValid = true
        });

        Assert.Null(preview.NationalId);
        Assert.False(preview.NationalIdValid);
        Assert.True(preview.LowConfidence);
        Assert.Equal("TEST SUBJECT", preview.FullName);
        Assert.Null(preview.BirthDate);
    }

    [Fact]
    public void ToScanResult_WithArabicIndicNumber_NormalizesAndDerives()
    {
        var service = CreateService(new FakeIdCardRepository());

        var preview = service.ToScanResult(new IdCardScanResult
        {
            NationalId = ArabicIndicNationalId,
            FullName = "TEST SUBJECT",
            Address = "TEST ADDRESS"
        });

        Assert.Equal(ValidNationalId, preview.NationalId);
        Assert.True(preview.NationalIdValid);
        Assert.Equal(new DateOnly(1990, 1, 1), preview.BirthDate);
        Assert.Equal("12", preview.GovernorateCode);
        Assert.False(preview.IsMale);
    }

    [Fact]
    public async Task ListAsync_ClampsPagingArguments()
    {
        var repository = new FakeIdCardRepository();
        var service = CreateService(repository);

        var result = await service.ListAsync(search: null, page: 0, pageSize: 5000);

        Assert.Equal(1, result.Page);
        Assert.Equal(IdCardService.MaxPageSize, result.PageSize);
    }

    private static IdCardService CreateService(FakeIdCardRepository repository)
    {
        return new IdCardService(repository, new FakeUnitOfWork(), new DisabledImageStore());
    }

    private sealed class FakeIdCardRepository : IIdCardRepository
    {
        public List<IdCard> Cards { get; } = new();

        public Task<bool> ExistsAsync(string nationalId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Cards.Any(card => card.NationalId == nationalId));
        }

        public Task<IdCard?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Cards.FirstOrDefault(card => card.Id == id));
        }

        public Task<PagedResult<IdCard>> SearchAsync(
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var filtered = Cards
                .Where(card => string.IsNullOrWhiteSpace(search)
                    || card.FullName.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || card.NationalId.Contains(search, StringComparison.Ordinal))
                .ToList();

            var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(new PagedResult<IdCard>(items, page, pageSize, filtered.Count));
        }

        public Task AddAsync(IdCard idCard, CancellationToken cancellationToken = default)
        {
            idCard.Id = Cards.Count + 1;
            Cards.Add(idCard);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }
    }

    private sealed class DisabledImageStore : IImageStore
    {
        public bool IsEnabled => false;

        public Task<string> SaveAsync(byte[] content, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Image storage is disabled in this test.");
        }
    }
}
