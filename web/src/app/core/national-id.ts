/**
 * Egyptian national ID parsing on the client.
 * Mirrors the server rules so the review screen can show derived values while the user types.
 * The server always re-validates and recomputes before storing.
 */

export interface DerivedInfo {
  nationalId: string;
  birthDate: string;
  isMale: boolean;
  governorateCode: string;
  governorateEnglish: string;
  governorateArabic: string;
}

export interface Governorate {
  english: string;
  arabic: string;
}

export const GOVERNORATES: Readonly<Record<string, Governorate>> = {
  '01': { english: 'Cairo', arabic: 'القاهرة' },
  '02': { english: 'Alexandria', arabic: 'الإسكندرية' },
  '03': { english: 'Port Said', arabic: 'بور سعيد' },
  '04': { english: 'Suez', arabic: 'السويس' },
  '11': { english: 'Damietta', arabic: 'دمياط' },
  '12': { english: 'Dakahlia', arabic: 'الدقهلية' },
  '13': { english: 'Sharqia', arabic: 'الشرقية' },
  '14': { english: 'Qalyubia', arabic: 'القليوبية' },
  '15': { english: 'Kafr El Sheikh', arabic: 'كفر الشيخ' },
  '16': { english: 'Gharbia', arabic: 'الغربية' },
  '17': { english: 'Monufia', arabic: 'المنوفية' },
  '18': { english: 'Beheira', arabic: 'البحيرة' },
  '19': { english: 'Ismailia', arabic: 'الإسماعيلية' },
  '21': { english: 'Giza', arabic: 'الجيزة' },
  '22': { english: 'Beni Suef', arabic: 'بني سويف' },
  '23': { english: 'Fayoum', arabic: 'الفيوم' },
  '24': { english: 'Minya', arabic: 'المنيا' },
  '25': { english: 'Assiut', arabic: 'أسيوط' },
  '26': { english: 'Sohag', arabic: 'سوهاج' },
  '27': { english: 'Qena', arabic: 'قنا' },
  '28': { english: 'Aswan', arabic: 'أسوان' },
  '29': { english: 'Luxor', arabic: 'الأقصر' },
  '31': { english: 'Red Sea', arabic: 'البحر الأحمر' },
  '32': { english: 'New Valley', arabic: 'الوادي الجديد' },
  '33': { english: 'Matrouh', arabic: 'مطروح' },
  '34': { english: 'North Sinai', arabic: 'شمال سيناء' },
  '35': { english: 'South Sinai', arabic: 'جنوب سيناء' },
  '88': { english: 'Born abroad', arabic: 'خارج البلاد' },
};

/** Converts Arabic-Indic (٠-٩) and Persian (۰-۹) digits to ASCII and strips non digits. */
export function normalizeDigits(raw: string): string {
  let result = '';

  for (const character of raw ?? '') {
    const code = character.charCodeAt(0);

    if (code >= 0x0660 && code <= 0x0669) {
      result += String.fromCharCode(0x30 + (code - 0x0660));
    } else if (code >= 0x06f0 && code <= 0x06f9) {
      result += String.fromCharCode(0x30 + (code - 0x06f0));
    } else if (code >= 0x30 && code <= 0x39) {
      result += character;
    }
  }

  return result;
}

/**
 * Parses a national ID number into its derived fields.
 * Returns null when the number is not exactly 14 digits, the century digit is unknown,
 * the birth date is not a real, non future date, or the governorate code is unknown.
 */
export function parseNationalId(raw: string, today: Date = new Date()): DerivedInfo | null {
  const digits = normalizeDigits(raw);
  if (digits.length !== 14) {
    return null;
  }

  const century = Number(digits[0]);
  if (century !== 2 && century !== 3) {
    return null;
  }

  const year = (century === 2 ? 1900 : 2000) + Number(digits.substring(1, 3));
  const month = Number(digits.substring(3, 5));
  const day = Number(digits.substring(5, 7));

  if (month < 1 || month > 12 || day < 1 || day > 31) {
    return null;
  }

  const birth = new Date(Date.UTC(year, month - 1, day));
  if (birth.getUTCFullYear() !== year || birth.getUTCMonth() !== month - 1 || birth.getUTCDate() !== day) {
    return null;
  }

  if (birth.getTime() > today.getTime()) {
    return null;
  }

  const governorateCode = digits.substring(7, 9);
  const governorate = GOVERNORATES[governorateCode];
  if (!governorate) {
    return null;
  }

  const genderDigit = Number(digits[12]);

  return {
    nationalId: digits,
    birthDate: `${year.toString().padStart(4, '0')}-${month.toString().padStart(2, '0')}-${day
      .toString()
      .padStart(2, '0')}`,
    isMale: genderDigit % 2 === 1,
    governorateCode,
    governorateEnglish: governorate.english,
    governorateArabic: governorate.arabic,
  };
}
