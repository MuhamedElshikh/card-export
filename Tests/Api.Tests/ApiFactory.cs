using IdCardReader.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IdCardReader.Api.Tests;

/// <summary>
/// Boots the real API pipeline with a throw away SQLite file and a deterministic OCR stub,
/// so the HTTP contract is exercised without a SQL Server instance or the Tesseract engine.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"idcard-tests-{Guid.NewGuid():N}.db");

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:IdCardReaderDb", $"Data Source={_databasePath}");
        builder.UseSetting("RateLimiting:Scan:PermitLimit", "1000");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IIdCardOcrService>();
            services.AddSingleton<IIdCardOcrService, StubIdCardOcrService>();
        });
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup of the temporary database file.
        }
    }
}
