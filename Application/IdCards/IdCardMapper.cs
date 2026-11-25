using IdCardReader.Domain;

namespace IdCardReader.Application.IdCards;

/// <summary>
/// Explicit entity to contract mapping.
/// Kept hand written on purpose: a mapping library would add a dependency (and, for the
/// AutoMapper versions compatible with this target, known high severity advisories) for a single map.
/// </summary>
public static class IdCardMapper
{
    /// <summary>Maps a stored card to its API representation.</summary>
    public static IdCardDto ToDto(IdCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new IdCardDto(
            Id: card.Id,
            NationalId: card.NationalId,
            FullName: card.FullName,
            Address: card.Address,
            BirthDate: card.BirthDate,
            IsMale: card.IsMale,
            GovernorateCode: card.GovernorateCode,
            GovernorateEnglish: card.GovernorateEnglish,
            GovernorateArabic: card.GovernorateArabic,
            LowConfidence: card.LowConfidence,
            CreatedAt: card.CreatedAt);
    }
}
