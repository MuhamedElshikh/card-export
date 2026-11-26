using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using IdCardReader.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IdCardReader.Api.Tests;

/// <summary>Scan overload and timeout contract, and the readiness probe.</summary>
public sealed class ScanFailureAndHealthTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Scan_WhenTheEngineIsBusy_Returns503WithRetryAfter()
    {
        using var factory = new ApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIdCardOcrService>();
                services.AddSingleton<IIdCardOcrService>(new ThrowingOcrService(new ScanBusyException()));
            }));
        using var client = factory.CreateClient();

        var response = await PostPngAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("scan_busy", await ReadErrorCodeAsync(response));
        Assert.True(response.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task Scan_WhenTheBudgetIsUsedUp_Returns504()
    {
        using var factory = new ApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIdCardOcrService>();
                services.AddSingleton<IIdCardOcrService>(
                    new ThrowingOcrService(new ScanTimeoutException(TimeSpan.FromSeconds(90))));
            }));
        using var client = factory.CreateClient();

        var response = await PostPngAsync(client);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("scan_timeout", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Ready_ReportsHealthyWhenTheDatabaseIsReachable()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_StaysALivenessProbe()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostPngAsync(HttpClient client)
    {
        using var content = new MultipartFormDataContent();
        var front = new ByteArrayContent(OnePixelPng);
        front.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(front, "frontImage", "front.png");
        var back = new ByteArrayContent(OnePixelPng);
        back.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(back, "backImage", "back.png");

        return await client.PostAsync("/api/idcards/scan", content);
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed class ThrowingOcrService : IIdCardOcrService
    {
        private readonly Exception _exception;

        public ThrowingOcrService(Exception exception)
        {
            _exception = exception;
        }

        public Task<IdCardScanResult> ScanAsync(
            byte[] frontImageBytes,
            byte[] backImageBytes,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }
    }
}
