using System;
using System.Globalization;

namespace IdCardReader.Domain
{
    /// <summary>
    /// Result of parsing an Egyptian National ID number.
    /// Contains the raw number and all derived fields.
    /// </summary>
    public class NationalIdResult
    {
        /// <summary>
        /// The normalized 14-digit national ID number (ASCII only), or null if not available.
        /// </summary>
        public string? NationalIdNumber { get; set; }

        /// <summary>
        /// Full name as it appears on the ID (best-effort OCR, may be null).
        /// </summary>
        public string? FullName { get; set; }

        /// <summary>
        /// Address as it appears on the ID (best-effort OCR, may be null).
        /// </summary>
        public string? Address { get; set; }

        /// <summary>
        /// Whether the national ID number is valid according to domain rules.
        /// </summary>
        public bool NationalIdValid { get; set; }

        /// <summary>
        /// Low confidence flag - the OCR result for the national ID should be highlighted in UI.
        /// </summary>
        public bool LowConfidence { get; set; }

        /// <summary>
        /// Parsed birth date (not saved to DB directly, derived from number).
        /// </summary>
        public DateOnly? BirthDate { get; set; }

        /// <summary>
        /// Parsed gender (not saved to DB directly, derived from number).
        /// </summary>
        public string? Gender { get; set; }

        /// <summary>
        /// Parsed governorate (not saved to DB directly, derived from number).
        /// </summary>
        public string? Governorate { get; set; }

        /// <summary>
        /// Parsed governorate in English.
        /// </summary>
        public string? GovernorateEnglish { get; set; }

        /// <summary>
        /// Parsed governorate in Arabic.
        /// </summary>
        public string? GovernorateArabic { get; set; }

        /// <summary>
        /// Creates a NationalIdResult from a validated NationalIdNumber and optional OCR fields.
        /// </summary>
        public static NationalIdResult FromParsedNumber(NationalIdNumber idNumber, string? fullName = null, string? address = null)
        {
            var result = new NationalIdResult
            {
                NationalIdNumber = idNumber.Value,
                FullName = fullName,
                Address = address,
                NationalIdValid = true,
                LowConfidence = false,
                BirthDate = idNumber.BirthDate,
                Gender = idNumber.IsMale ? "Male" : "Female",
                Governorate = idNumber.GetGovernorateEnglish(),
                GovernorateEnglish = idNumber.GetGovernorateEnglish(),
                GovernorateArabic = idNumber.GetGovernorateArabic()
            };

            return result;
        }

        /// <summary>
        /// Gets the age based on birth date.
        /// </summary>
        public int Age => BirthDate.HasValue
            ? DateOnly.FromDateTime(DateTime.UtcNow).Year - BirthDate.Value.Year
            : 0;
    }
}