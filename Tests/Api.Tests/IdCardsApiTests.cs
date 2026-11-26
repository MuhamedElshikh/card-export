using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace IdCardReader.Api.Tests;

/// <summary>HTTP contract tests for the ID card endpoints.</summary>
public sealed class IdCardsApiTests
{
    private const string ValidNationalId = "29001011234567";
    private const string SecondValidNationalId = "30101011234571";

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Create_WithValidNationalId_DerivesFieldsAndReturns201()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/idcards", new
        {
            nationalId = ValidNationalId,
            fullName = "TEST SUBJECT",
            address = "TEST ADDRESS",
            lowConfidence = false
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(ValidNationalId, root.GetProperty("nationalId").GetString());
        Assert.Equal("TEST SUBJECT", root.GetProperty("fullName").GetString());
        Assert.Equal("1990-01-01", root.GetProperty("birthDate").GetString());
        Assert.False(root.GetProperty("isMale").GetBoolean());
        Assert.Equal("12", root.GetProperty("governorateCode").GetString());
        Assert.Equal("Dakahlia", root.GetProperty("governorateEnglish").GetString());
        Assert.Equal("الدقهلية", root.GetProperty("governorateArabic").GetString());
    }

    [Fact]
    public async Task Create_WithInvalidNationalId_Returns400WithCode()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/idcards", new
        {
            nationalId = "12345",
            fullName = "TEST SUBJECT",
            address = "TEST ADDRESS",
            lowConfidence = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_national_id", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Create_WithMissingName_ReturnsValidationFailure()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/idcards", new
        {
            nationalId = ValidNationalId,
            fullName = "",
            address = "TEST ADDRESS",
            lowConfidence = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Create_TwiceWithSameNationalId_Returns409()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var payload = new
        {
            nationalId = ValidNationalId,
            fullName = "TEST SUBJECT",
            address = "TEST ADDRESS",
            lowConfidence = false
        };

        var first = await client.PostAsJsonAsync("/api/idcards", payload);
        var second = await client.PostAsJsonAsync("/api/idcards", payload);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("duplicate_national_id", await ReadErrorCodeAsync(second));
    }

    [Fact]
    public async Task Create_WithImageWhenStorageDisabled_Returns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/idcards", new
        {
            nationalId = ValidNationalId,
            fullName = "TEST SUBJECT",
            address = "TEST ADDRESS",
            lowConfidence = false,
            imageBase64 = Convert.ToBase64String(OnePixelPng)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("image_storage_disabled", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Scan_WithNonImagePayload_Returns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("this is not an image"));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "frontImage", "fake.png");
        var back = new ByteArrayContent(OnePixelPng);
        back.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(back, "backImage", "back.png");

        var response = await client.PostAsync("/api/idcards/scan", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unsupported_image_format", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Scan_WithoutFile_Returns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("nothing"), "notAnImage");

        var response = await client.PostAsync("/api/idcards/scan", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("front_image_required", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Scan_WithoutFrontImage_ReturnsFrontImageRequired()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        var back = new ByteArrayContent(OnePixelPng);
        back.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(back, "backImage", "back.png");

        var response = await client.PostAsync("/api/idcards/scan", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("front_image_required", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Scan_WithOnlyFrontImage_ReturnsBackImageRequired()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        var front = new ByteArrayContent(OnePixelPng);
        front.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(front, "frontImage", "front.png");

        var response = await client.PostAsync("/api/idcards/scan", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("back_image_required", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Scan_WithBothPngImages_ReturnsPreviewIncludingBackFields()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        var front = new ByteArrayContent(OnePixelPng);
        front.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(front, "frontImage", "front.png");
        var back = new ByteArrayContent(OnePixelPng);
        back.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(back, "backImage", "back.png");

        var response = await client.PostAsync("/api/idcards/scan", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(ValidNationalId, root.GetProperty("nationalId").GetString());
        Assert.True(root.GetProperty("nationalIdValid").GetBoolean());
        Assert.False(root.GetProperty("lowConfidence").GetBoolean());
        Assert.Equal("12", root.GetProperty("governorateCode").GetString());
        Assert.Equal("مهندس", root.GetProperty("occupation").GetString());
        Assert.Equal("متزوج", root.GetProperty("maritalStatus").GetString());
        Assert.Equal("2030-01-01", root.GetProperty("expiryDate").GetString());
    }

    [Fact]
    public async Task Create_WithAdditionalCardFields_PersistsTheyForRetrieval()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/idcards", new
        {
            nationalId = ValidNationalId,
            fullName = "TEST SUBJECT",
            address = "TEST ADDRESS",
            lowConfidence = false,
            occupation = "مهندس",
            maritalStatus = "متزوج",
            expiryDate = "2030-01-01"
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var location = create.Headers.Location;
        Assert.NotNull(location);

        using var document = JsonDocument.Parse(await (await client.GetAsync(location)).Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("مهندس", root.GetProperty("occupation").GetString());
        Assert.Equal("متزوج", root.GetProperty("maritalStatus").GetString());
        Assert.Equal("2030-01-01", root.GetProperty("expiryDate").GetString());
    }

    [Fact]
    public async Task List_ReturnsPagedResults()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/idcards", new
        {
            nationalId = ValidNationalId,
            fullName = "TEST SUBJECT",
            address = "TEST ADDRESS",
            lowConfidence = false
        });
        await client.PostAsJsonAsync("/api/idcards", new
        {
            nationalId = SecondValidNationalId,
            fullName = "SECOND SUBJECT",
            address = "SECOND ADDRESS",
            lowConfidence = true
        });

        var response = await client.GetAsync("/api/idcards?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(2, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, root.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, root.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_WithSearch_FiltersByName()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/idcards", new
        {
            nationalId = ValidNationalId,
            fullName = "TEST SUBJECT",
            address = "TEST ADDRESS",
            lowConfidence = false
        });
        await client.PostAsJsonAsync("/api/idcards", new
        {
            nationalId = SecondValidNationalId,
            fullName = "SECOND SUBJECT",
            address = "SECOND ADDRESS",
            lowConfidence = false
        });

        var response = await client.GetAsync("/api/idcards?search=SECOND");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("totalCount").GetInt32());
        Assert.Equal("SECOND SUBJECT", root.GetProperty("items")[0].GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task GetById_ReturnsStoredCard()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/api/idcards", new
        {
            nationalId = ValidNationalId,
            fullName = "TEST SUBJECT",
            address = "TEST ADDRESS",
            lowConfidence = false
        });

        using var createdDocument = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdDocument.RootElement.GetProperty("id").GetInt32();

        var response = await client.GetAsync($"/api/idcards/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ValidNationalId, document.RootElement.GetProperty("nationalId").GetString());
    }

    [Fact]
    public async Task GetById_WithUnknownId_Returns404()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/idcards/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("id_card_not_found", await ReadErrorCodeAsync(response));
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
