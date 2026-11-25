using IdCardReader.Application;
using IdCardReader.Application.IdCards;
using IdCardReader.Infrastructure.Persistence;
using IdCardReader.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IdCardReader.Infrastructure;

/// <summary>Composition root for the infrastructure layer.</summary>
public static class DependencyInjection
{
    /// <summary>Registers persistence, OCR and optional image storage.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdCardReaderDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("IdCardReaderDb")
                ?? throw new InvalidOperationException("Connection string 'IdCardReaderDb' is not configured.");

            var provider = configuration["Database:Provider"];
            if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connectionString);
            }
            else
            {
                options.UseSqlServer(connectionString);
            }
        });
        services.AddScoped<IIdCardRepository, IdCardRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IImageStore, EncryptedImageStore>();

        services.Configure<TesseractOptions>(configuration.GetSection(TesseractOptions.SectionName));
        services.AddSingleton<IIdCardOcrService, TesseractIdCardOcrService>();

        return services;
    }
}
