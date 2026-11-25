using FluentValidation;
using IdCardReader.Application.IdCards;
using Microsoft.Extensions.DependencyInjection;

namespace IdCardReader.Application;

/// <summary>Composition root for the application layer.</summary>
public static class DependencyInjection
{
    /// <summary>Registers application services and validation.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<SaveIdCardRequest>, SaveIdCardRequestValidator>();
        services.AddScoped<IdCardService>();

        return services;
    }
}
