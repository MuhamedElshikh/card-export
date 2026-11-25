using System.Threading.RateLimiting;
using IdCardReader.Api;
using IdCardReader.Application;
using IdCardReader.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy => policy
        .WithOrigins(
            builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:4200" })
        .AllowAnyMethod()
        .AllowAnyHeader());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Scan, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:Scan:PermitLimit", 10),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngularApp");
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

await ApplyMigrationsAsync(app);

app.Run();

static async Task ApplyMigrationsAsync(WebApplication application)
{
    using var scope = application.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var context = scope.ServiceProvider.GetRequiredService<IdCardReaderDbContext>();

    try
    {
        if (context.Database.IsSqlite())
        {
            // SQLite is only used for local/dev and automated tests: build the schema from the model
            // because the migrations are authored for SQL Server.
            await context.Database.EnsureCreatedAsync();
            logger.LogInformation("SQLite schema ensured from the model.");
        }
        else
        {
            await context.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(
            ex,
            "Database migration failed. The API starts anyway, but /api/idcards requires a reachable SQL Server.");
    }
}

/// <summary>Marker type so integration tests can reference the application entry point.</summary>
public partial class Program;
