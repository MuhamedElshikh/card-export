using IdCardReader.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IdCardReader.Api;

/// <summary>Reports unhealthy while the database cannot be reached.</summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IdCardReaderDbContext _context;

    /// <summary>Creates the check.</summary>
    public DatabaseHealthCheck(IdCardReaderDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database is not reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The database is not reachable.", exception);
        }
    }
}
