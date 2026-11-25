using IdCardReader.Domain;
using IdCardReader.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace IdCardReader.Infrastructure.IntegrationTests;

/// <summary>
/// Exercises the real EF Core model (indexes, paging, duplicate detection) against a
/// relational provider, so the unique index is verified by the database and not by a mock.
/// </summary>
public sealed class IdCardRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IdCardReaderDbContext _context;

    public IdCardRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<IdCardReaderDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new IdCardReaderDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task UniqueIndex_RejectsDuplicateNationalId()
    {
        _context.IdCards.Add(CreateCard("29001011234567", "FIRST SUBJECT"));
        await _context.SaveChangesAsync();

        _context.IdCards.Add(CreateCard("29001011234567", "SECOND SUBJECT"));

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task UnitOfWork_TranslatesUniqueViolationToDomainException()
    {
        _context.IdCards.Add(CreateCard("29001011234567", "FIRST SUBJECT"));
        await _context.SaveChangesAsync();

        _context.IdCards.Add(CreateCard("29001011234567", "SECOND SUBJECT"));

        var unitOfWork = new UnitOfWork(_context);
        await Assert.ThrowsAsync<Application.IdCards.DuplicateNationalIdException>(
            () => unitOfWork.SaveChangesAsync());
    }

    [Fact]
    public async Task Search_ReturnsPagedFilteredResults()
    {
        _context.IdCards.AddRange(
            CreateCard("29001011234567", "AHMED TEST"),
            CreateCard("30101011234571", "MONA TEST"),
            CreateCard("29001011234569", "OTHER NAME"));
        await _context.SaveChangesAsync();

        var repository = new IdCardRepository(_context);

        var page = await repository.SearchAsync("TEST", page: 1, pageSize: 1);
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);

        var secondPage = await repository.SearchAsync("TEST", page: 2, pageSize: 1);
        Assert.Single(secondPage.Items);
        Assert.NotEqual(page.Items[0].Id, secondPage.Items[0].Id);
    }

    [Fact]
    public async Task Search_ByNationalIdFragment_Matches()
    {
        _context.IdCards.AddRange(
            CreateCard("29001011234567", "AHMED TEST"),
            CreateCard("30101011234571", "MONA TEST"));
        await _context.SaveChangesAsync();

        var repository = new IdCardRepository(_context);
        var page = await repository.SearchAsync("2900", page: 1, pageSize: 10);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("29001011234567", page.Items[0].NationalId);
    }

    [Fact]
    public async Task ExistsAsync_IsTrueOnlyForStoredNumbers()
    {
        _context.IdCards.Add(CreateCard("29001011234567", "AHMED TEST"));
        await _context.SaveChangesAsync();

        var repository = new IdCardRepository(_context);

        Assert.True(await repository.ExistsAsync("29001011234567"));
        Assert.False(await repository.ExistsAsync("30101011234571"));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static IdCard CreateCard(string nationalId, string fullName)
    {
        var parsed = NationalIdNumber.Create(nationalId);

        return new IdCard
        {
            NationalId = parsed.Value,
            FullName = fullName,
            Address = "TEST ADDRESS",
            BirthDate = parsed.BirthDate,
            IsMale = parsed.IsMale,
            GovernorateCode = parsed.GovernorateCode.ToString("00"),
            GovernorateEnglish = parsed.GetGovernorateEnglish(),
            GovernorateArabic = parsed.GetGovernorateArabic(),
            LowConfidence = false,
            CreatedAt = DateTime.UtcNow
        };
    }
}
