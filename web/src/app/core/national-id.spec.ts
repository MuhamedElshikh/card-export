import { describe, expect, it } from 'vitest';
import { GOVERNORATES, normalizeDigits, parseNationalId } from './national-id';

describe('normalizeDigits', () => {
  it('converts Arabic-Indic digits', () => {
    expect(normalizeDigits('٢٩٠٠١٠١١٢٣٤٥٦٧')).toBe('29001011234567');
  });

  it('converts Persian digits', () => {
    expect(normalizeDigits('۲۹۰۰۱۰۱۱۲۳۴۵۶۷')).toBe('29001011234567');
  });

  it('strips spaces, letters and separators', () => {
    expect(normalizeDigits(' 2900-1011 234567 abc')).toBe('29001011234567');
  });
});

describe('parseNationalId', () => {
  it('derives birth date, gender and governorate for a 1900s number', () => {
    const result = parseNationalId('29001011234567');

    expect(result).not.toBeNull();
    expect(result?.nationalId).toBe('29001011234567');
    expect(result?.birthDate).toBe('1990-01-01');
    expect(result?.isMale).toBe(false);
    expect(result?.governorateCode).toBe('12');
    expect(result?.governorateEnglish).toBe('Dakahlia');
    expect(result?.governorateArabic).toBe('الدقهلية');
  });

  it('uses the 2000s century for a leading 3 and detects male for an odd gender digit', () => {
    const result = parseNationalId('30101011234571');

    expect(result?.birthDate).toBe('2001-01-01');
    expect(result?.isMale).toBe(true);
  });

  it('accepts Arabic-Indic digits', () => {
    expect(parseNationalId('٢٩٠٠١٠١١٢٣٤٥٦٧')?.nationalId).toBe('29001011234567');
  });

  it('rejects a number that is not exactly 14 digits', () => {
    expect(parseNationalId('2900101123456')).toBeNull();
    expect(parseNationalId('290010112345678')).toBeNull();
  });

  it('rejects an unknown century digit', () => {
    expect(parseNationalId('49001011234567')).toBeNull();
  });

  it('rejects an impossible date', () => {
    expect(parseNationalId('29013011234567')).toBeNull(); // month 13
    expect(parseNationalId('29023011234567')).toBeNull(); // 30 February
  });

  it('rejects a future birth date', () => {
    const today = new Date(Date.UTC(2000, 0, 1));
    expect(parseNationalId('30101011234571', today)).toBeNull();
  });

  it('rejects an unknown governorate code', () => {
    expect(parseNationalId('29001012134567')).toBeNull(); // code 21 is not in the map
  });

  it('knows every governorate code required by the specification', () => {
    const required = ['01', '02', '03', '04', '11', '12', '13', '14', '15', '16', '17', '18', '19', '20',
      '22', '23', '24', '25', '26', '27', '28', '29', '31', '32', '33', '34', '35', '88'];

    for (const code of required) {
      expect(GOVERNORATES[code], `missing governorate ${code}`).toBeDefined();
      expect(GOVERNORATES[code].arabic.length).toBeGreaterThan(1);
    }
  });
});
