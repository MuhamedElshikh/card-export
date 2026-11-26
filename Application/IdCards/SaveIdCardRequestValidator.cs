using FluentValidation;

namespace IdCardReader.Application.IdCards;

/// <summary>
/// Input shape validation for the create endpoint.
/// Domain validity of the national ID is checked separately by <see cref="IdCardService"/>.
/// </summary>
public sealed class SaveIdCardRequestValidator : AbstractValidator<SaveIdCardRequest>
{
    /// <summary>Registers the rules.</summary>
    public SaveIdCardRequestValidator()
    {
        RuleFor(x => x.NationalId)
            .NotEmpty()
            .MaximumLength(32);

        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Address)
            .MaximumLength(500)
            .When(x => x.Address is not null);

        RuleFor(x => x.Occupation)
            .MaximumLength(200)
            .When(x => x.Occupation is not null);

        RuleFor(x => x.MaritalStatus)
            .MaximumLength(100)
            .When(x => x.MaritalStatus is not null);

        RuleFor(x => x.ImageBase64)
            .MaximumLength(14_000_000)
            .When(x => x.ImageBase64 is not null);
    }
}
