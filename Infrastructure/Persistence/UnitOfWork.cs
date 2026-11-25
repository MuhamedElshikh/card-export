using IdCardReader.Application.IdCards;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IdCardReader.Infrastructure.Persistence;

/// <summary>Unit of work over <see cref="IdCardReaderDbContext"/>.</summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly IdCardReaderDbContext _context;

    /// <summary>Creates the unit of work.</summary>
    public UnitOfWork(IdCardReaderDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicateNationalIdException(ex);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        // SQL Server: 2601 duplicate key row in index, 2627 unique constraint violation.
        if (exception.InnerException is SqlException sqlException)
        {
            return sqlException.Number is 2601 or 2627;
        }

        // SQLite (used by the test suite): UNIQUE constraint failed.
        return exception.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;
    }
}
