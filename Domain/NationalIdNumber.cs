using System;
using System.Globalization;

namespace IdCardReader.Domain
{
    /// <summary>
    /// Represents a validated Egyptian National ID number (14 digits).
    /// The raw string can contain Arabic-Indic or Persian digits which get normalized.
    /// </summary>
    public struct NationalIdNumber
    {
        // Fixed governorate codes matching the task specification exactly:
        // 01 Cairo, 02 Alexandria, 03 Port Said, 04 Suez,
        // 11 Damietta, 12 Dakahlia, 13 Sharqia, 14 Qalyubia,
        // 15 Kafr El Sheikh, 16 Gharbia, 17 Monufia, 18 Beheira,
        // 19 Ismailia, 20 Giza, 22 Beni Suef, 23 Fayoum,
        // 24 Minya, 25 Assiut, 26 Sohag, 27 Qena,
        // 28 Aswan, 29 Luxor, 31 Red Sea, 32 New Valley,
        // 33 Matrouh, 34 North Sinai, 35 South Sinai, 88 Born abroad
        private static readonly (int code, string english, string arabic)[] GovernorateMap =
        {
            (1, "Cairo", "القاهرة"),
            (2, "Alexandria", "الإسكندرية"),
            (3, "Port Said", "بور سعيد"),
            (4, "Suez", "السويس"),
            (11, "Damietta", "دمياط"),
            (12, "Dakahlia", "الدقهلية"),
            (13, "Sharqia", "الشرقية"),
            (14, "Qalyubia", "القليوبية"),
            (15, "Kafr El Sheikh", "كفر الشيخ"),
            (16, "Gharbia", "الغربية"),
            (17, "Monufia", "المنوفية"),
            (18, "Beheira", "البحيرة"),
            (19, "Ismailia", "الإسماعيلية"),
            (20, "Giza", "الجيزة"),
            (22, "Beni Suef", "بني سويف"),
            (23, "Fayoum", "الفيوم"),
            (24, "Minya", "المنيا"),
            (25, "Assiut", "أسيوط"),
            (26, "Sohag", "سوهاج"),
            (27, "Qena", "قنا"),
            (28, "Aswan", "أسوان"),
            (29, "Luxor", "الأقصر"),
            (31, "Red Sea", "البحر الأحمر"),
            (32, "New Valley", "الوادي الجديد"),
            (33, "Matrouh", "مطروح"),
            (34, "North Sinai", "شمال سيناء"),
            (35, "South Sinai", "جنوب سيناء"),
            (88, "Born abroad", "خارج البلاد")
        };

        private static readonly Dictionary<int, (string english, string arabic)> _governorateLookup;

        static NationalIdNumber()
        {
            _governorateLookup = new Dictionary<int, (string, string)>();
            foreach (var (code, english, arabic) in GovernorateMap)
            {
                _governorateLookup[code] = (english, arabic);
            }
        }

        private readonly string _normalized;

        /// <summary>
        /// Constructor - use Create() factory method instead
        /// </summary>
        private NationalIdNumber(string normalized)
        {
            _normalized = normalized;
        }

        /// <summary>
        /// Gets the normalized 14-digit ASCII national ID number (only digits).
        /// Throws if the number is invalid.
        /// </summary>
        public string Value => _normalized;

        /// <summary>
        /// Century digit: 2 = 1900s, 3 = 2000s
        /// </summary>
        public int CenturyDigit => _normalized[0] - '0';

        /// <summary>
        /// Birth date (YYMMDD) from digits 2-7
        /// </summary>
        public DateOnly BirthDate
        {
            get
            {
                int yy = int.Parse(_normalized.Substring(1, 2));
                int mm = int.Parse(_normalized.Substring(3, 2));
                int dd = int.Parse(_normalized.Substring(5, 2));
                int year = CenturyDigit == 2 ? 1900 + yy : 2000 + yy;
                return new DateOnly(year, mm, dd);
            }
        }

        /// <summary>
        /// Governorate code from digits 8-9
        /// </summary>
        public int GovernorateCode => int.Parse(_normalized.Substring(7, 2));

        /// <summary>
        /// Gender: digit 13 odd = male, even = female
        /// </summary>
        public bool IsMale => (_normalized[12] - '0') % 2 == 1;

        /// <summary>
        /// Raw 14-digit string (always ASCII after normalization)
        /// </summary>
        public override string ToString() => _normalized;

        /// <summary>
        /// Gets the governorate name in English based on the governorate code.
        /// </summary>
        public string GetGovernorateEnglish() => _governorateLookup.TryGetValue(GovernorateCode, out var result) ? result.english : "Unknown";

        /// <summary>
        /// Gets the governorate name in Arabic based on the governorate code.
        /// </summary>
        public string GetGovernorateArabic() => _governorateLookup.TryGetValue(GovernorateCode, out var result) ? result.arabic : "Unknown";

        /// <summary>
        /// Non-throwing variant of <see cref="Create"/>; returns null when the value is not a valid ID.
        /// </summary>
        public static NationalIdNumber? TryCreate(string? raw)
        {
            try
            {
                return Create(raw ?? string.Empty);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Creates a NationalIdNumber from a raw string, normalizing digits and validating.
        /// </summary>
        /// <param name="raw">The raw national ID number (may contain Arabic-Indic or Persian digits)</param>
        /// <exceptionArgumentException>If the normalized number is not exactly 14 digits or fails validation</exceptionArgumentException>
        public static NationalIdNumber Create(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("National ID number cannot be null or empty", nameof(raw));

            // Normalize Arabic-Indic digits (٠-٩) to Western (0-9)
            // Normalize Persian digits (۰-۹) to Western (0-9)
            var normalized = raw;
            // Arabic-Indic: U+0660-U+0669
            // Persian: U+06F0-U+06F9
            normalized = normalized.Replace("\u0660", "0").Replace("\u0661", "1").Replace("\u0662", "2").Replace("\u0663", "3").Replace("\u0664", "4")
                .Replace("\u0665", "5").Replace("\u0666", "6").Replace("\u0667", "7").Replace("\u0668", "8").Replace("\u0669", "9");
            // Persian: U+06F0-U+06F9
            normalized = normalized.Replace("\u06F0", "0").Replace("\u06F1", "1").Replace("\u06F2", "2").Replace("\u06F3", "3").Replace("\u06F4", "4")
                .Replace("\u06F5", "5").Replace("\u06F6", "6").Replace("\u06F7", "7").Replace("\u06F8", "8").Replace("\u06F9", "9");

            // Strip all non-digit characters
            var digitsOnly = string.Join("", normalized.Where(char.IsDigit));

            if (digitsOnly.Length != 14)
                throw new ArgumentException($"National ID number must have exactly 14 digits after normalization, got {digitsOnly.Length}", nameof(raw));

            // Check century digit
            int centuryDigit = digitsOnly[0] - '0';
            if (centuryDigit != 2 && centuryDigit != 3)
                throw new ArgumentException($"Invalid century digit '{centuryDigit}'. Must be 2 (1900s) or 3 (2000s).", nameof(raw));

            // Validate date (digits 2-7 = YYMMDD)
            int yy = int.Parse(digitsOnly.Substring(1, 2));
            int mm = int.Parse(digitsOnly.Substring(3, 2));
            int dd = int.Parse(digitsOnly.Substring(5, 2));
            int year = centuryDigit == 2 ? 1900 + yy : 2000 + yy;

            // Check month range
            if (mm < 1 || mm > 12)
                throw new ArgumentException($"Invalid month '{mm}' in national ID birth date. Must be between 01 and 12.", nameof(raw));

            // Check day range (basic - full validation by DateOnly constructor)
            if (dd < 1 || dd > 31)
                throw new ArgumentException($"Invalid day '{dd}' in national ID birth date. Must be between 01 and 31.", nameof(raw));

            // Check not in the future
            if (new DateOnly(year, mm, dd) > DateOnly.FromDateTime(DateTime.UtcNow))
                throw new ArgumentException("Birth date cannot be in the future.", nameof(raw));

            return new NationalIdNumber(digitsOnly);
        }
    }
}