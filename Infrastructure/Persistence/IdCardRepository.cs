using IdCardReader.Application.IdCards;
using IdCardReader.Domain;
using Microsoft.EntityFrameworkCore;

namespace IdCardReader.Infrastructure.Persistence;

/// <summary>EF Core implementation of the ID card repository.</summary>
public sealed class IdCardRepository : IIdCardRepository
{
    private readonly IdCardReaderDbContext _context;

    /// <summary>Creates the repository.</summary>
    public IdCardRepository(IdCardReaderDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string nationalId, CancellationToken cancellationToken = default)
    {
        return _context.IdCards
            .AsNoTracking()
            .AnyAsync(card => card.NationalId == nationalId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IdCard?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return _context.IdCards
            .AsNoTracking()
            .FirstOrDefaultAsync(card => card.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedResult<IdCard>> SearchAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<IdCard> query = _context.IdCards.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(card =>
                EF.Functions.Like(card.FullName, $"%{term}%") ||
                EF.Functions.Like(card.NationalId, $"%{term}%"));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(card => card.CreatedAt)
            .ThenByDescending(card => card.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<IdCard>(items, page, pageSize, total);
    }

    /// <inheritdoc />
    public async Task AddAsync(IdCard idCard, CancellationToken cancellationToken = default)
    {
        await _context.IdCards.AddAsync(idCard, cancellationToken).ConfigureAwait(false);
    }
}
