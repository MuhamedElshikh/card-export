using IdCardReader.Domain;

namespace IdCardReader.Application.IdCards;

/// <summary>Read/write access to stored ID cards.</summary>
public interface IIdCardRepository
{
    /// <summary>Returns true when a card with the same normalized national ID already exists.</summary>
    Task<bool> ExistsAsync(string nationalId, CancellationToken cancellationToken = default);

    /// <summary>Returns the card with the given identifier, or null.</summary>
    Task<IdCard?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a page of cards, optionally filtered by a term matched against the name or national ID.
    /// </summary>
    Task<PagedResult<IdCard>> SearchAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Stages a new card for insertion.</summary>
    Task AddAsync(IdCard idCard, CancellationToken cancellationToken = default);
}

/// <summary>Transaction boundary over the persistence layer.</summary>
public interface IUnitOfWork
{
    /// <summary>Commits staged changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
