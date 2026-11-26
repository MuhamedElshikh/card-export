using System.Diagnostics;
using FluentValidation;
using FluentValidation.Results;
using IdCardReader.Application;
using IdCardReader.Application.IdCards;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IdCardReader.Api.Controllers;

/// <summary>Egyptian national ID card endpoints.</summary>
[ApiController]
[Route("api/idcards")]
public sealed class IdCardsController : ControllerBase
{
    private const int MaxImageBytes = 5 * 1024 * 1024;
    private const int MaxScanRequestBytes = (2 * MaxImageBytes) + 16 * 1024;

    private readonly IIdCardOcrService _ocrService;
    private readonly IdCardService _idCardService;
    private readonly IValidator<SaveIdCardRequest> _validator;
    private readonly ILogger<IdCardsController> _logger;

    /// <summary>Creates the controller.</summary>
    public IdCardsController(
        IIdCardOcrService ocrService,
        IdCardService idCardService,
        IValidator<SaveIdCardRequest> validator,
        ILogger<IdCardsController> logger)
    {
        _ocrService = ocrService;
        _idCardService = idCardService;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>
    /// Runs OCR on both sides of an uploaded ID card and returns a reviewable preview.
    /// Nothing is persisted by this endpoint.
    /// </summary>
    [HttpPost("scan")]
    [EnableRateLimiting(RateLimitPolicies.Scan)]
    [RequestSizeLimit(MaxScanRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxScanRequestBytes)]
    [ProducesResponseType(typeof(ScanResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Scan(
        IFormFile? frontImage,
        IFormFile? backImage,
        CancellationToken cancellationToken)
    {
        if (frontImage is null || frontImage.Length == 0)
        {
            return ApiProblem(
                StatusCodes.Status400BadRequest,
                "front_image_required",
                "The front of the ID card is required.");
        }

        if (backImage is null || backImage.Length == 0)
        {
            return ApiProblem(
                StatusCodes.Status400BadRequest,
                "back_image_required",
                "The back of the ID card is required.");
        }

        if (frontImage.Length > MaxImageBytes || backImage.Length > MaxImageBytes)
        {
            return ApiProblem(
                StatusCodes.Status413PayloadTooLarge,
                "image_too_large",
                $"Each image must be {MaxImageBytes / (1024 * 1024)} MB or smaller.");
        }

        var frontBytes = await ReadImageBytesAsync(frontImage, cancellationToken);
        var backBytes = await ReadImageBytesAsync(backImage, cancellationToken);

        if (!ImageSignature.IsSupported(frontBytes) || !ImageSignature.IsSupported(backBytes))
        {
            return ApiProblem(
                StatusCodes.Status400BadRequest,
                "unsupported_image_format",
                "Both images must be valid JPEG or PNG files.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var ocrResult = await _ocrService.ScanAsync(frontBytes, backBytes, cancellationToken);
            var preview = _idCardService.ToScanResult(ocrResult);
            stopwatch.Stop();

            _logger.LogInformation(
                "ID card scan completed in {ElapsedMs} ms. NationalIdFound={NationalIdFound} LowConfidence={LowConfidence}",
                stopwatch.ElapsedMilliseconds,
                preview.NationalId is not null,
                preview.LowConfidence);

            return Ok(preview);
        }

        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("ID card scan cancelled by the client.");
            return ApiProblem(StatusCodes.Status499ClientClosedRequest, "scan_cancelled", "The request was cancelled.");
        }
        catch (ScanBusyException)
        {
            _logger.LogWarning("ID card scan refused: the OCR engine is saturated.");
            Response.Headers.RetryAfter = "5";
            return ApiProblem(
                StatusCodes.Status503ServiceUnavailable,
                "scan_busy",
                "The server is busy reading other cards. Try again in a moment.");
        }
        catch (ScanTimeoutException)
        {
            stopwatch.Stop();
            _logger.LogWarning("ID card scan timed out after {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
            return ApiProblem(
                StatusCodes.Status504GatewayTimeout,
                "scan_timeout",
                "Reading the card took too long. Try a smaller or clearer photo.");
        }
        catch (ImageDecodeException)
        {
            _logger.LogWarning("ID card scan rejected: the image could not be decoded.");
            return ApiProblem(
                StatusCodes.Status400BadRequest,
                "invalid_image",
                "The image could not be decoded. Please upload a clear JPEG or PNG photo.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "ID card scan failed after {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
            return ApiProblem(
                StatusCodes.Status500InternalServerError,
                "scan_failed",
                "The image could not be processed.");
        }
    }

    private static async Task<byte[]> ReadImageBytesAsync(IFormFile image, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await using var upload = image.OpenReadStream();
        await upload.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>Stores a reviewed ID card after re-validating the national ID on the server.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(IdCardDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] SaveIdCardRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ApiProblem(StatusCodes.Status400BadRequest, "invalid_request", "A request body is required.");
        }

        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        var result = await _idCardService.CreateAsync(request, cancellationToken);

        switch (result.Status)
        {
            case CreateIdCardStatus.Created:
                _logger.LogInformation("ID card {IdCardId} stored.", result.IdCard!.Id);
                return CreatedAtAction(nameof(GetById), new { id = result.IdCard.Id }, result.IdCard);

            case CreateIdCardStatus.Duplicate:
                return ApiProblem(
                    StatusCodes.Status409Conflict,
                    "duplicate_national_id",
                    "A card with this national ID number already exists.");

            case CreateIdCardStatus.ImageStorageDisabled:
                return ApiProblem(
                    StatusCodes.Status400BadRequest,
                    "image_storage_disabled",
                    "Image storage is disabled on this server.");

            case CreateIdCardStatus.InvalidImage:
                return ApiProblem(
                    StatusCodes.Status400BadRequest,
                    "unsupported_image_format",
                    "The supplied image is not a valid JPEG or PNG payload.");

            case CreateIdCardStatus.InvalidNationalId:
            default:
                return ApiProblem(
                    StatusCodes.Status400BadRequest,
                    "invalid_national_id",
                    result.ErrorDetail ?? "The national ID number is not valid.");
        }
    }

    /// <summary>Returns a page of stored cards.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<IdCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _idCardService.ListAsync(search, page, pageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>Returns a single stored card.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(IdCardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var card = await _idCardService.GetAsync(id, cancellationToken);
        if (card is null)
        {
            return ApiProblem(StatusCodes.Status404NotFound, "id_card_not_found", "No card with this identifier exists.");
        }

        return Ok(card);
    }

    private ObjectResult ApiProblem(int statusCode, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = code,
            Detail = detail,
            Type = $"https://httpstatuses.io/{statusCode}"
        };
        problem.Extensions["code"] = code;

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }

    private ObjectResult ValidationFailure(ValidationResult validation)
    {
        var errors = validation.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "validation_failed",
            Detail = "One or more validation errors occurred.",
            Type = "https://httpstatuses.io/400"
        };
        problem.Extensions["code"] = "validation_failed";

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        };
    }
}
