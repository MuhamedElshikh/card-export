import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { IdCardApiService, ApiError } from '../../core/idcard-api.service';
import { LanguageService } from '../../core/language.service';
import { ScanResult } from '../../core/models';
import { ScanPage } from './scan.page';

describe('ScanPage', () => {
  const scanResult: ScanResult = {
    nationalId: '29001011234567',
    fullName: 'TEST SUBJECT',
    address: 'TEST ADDRESS',
    nationalIdValid: true,
    lowConfidence: true,
    birthDate: '1990-01-01',
    isMale: false,
    governorateCode: '12',
    governorateEnglish: 'Dakahlia',
    governorateArabic: 'الدقهلية',
  };

  let api: { scan: ReturnType<typeof vi.fn>; create: ReturnType<typeof vi.fn> };
  let page: ScanPage;

  beforeEach(() => {
    api = { scan: vi.fn(), create: vi.fn() };

    // jsdom does not implement object URLs.
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    URL.revokeObjectURL = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        { provide: IdCardApiService, useValue: api },
        { provide: LanguageService, useValue: { current: signal('ar') } },
      ],
    });

    page = TestBed.runInInjectionContext(() => new ScanPage());
  });

  function selectFile(): void {
    const input = document.createElement('input');
    const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'card.png', { type: 'image/png' });
    Object.defineProperty(input, 'files', { value: [file] });

    page.onFileSelected({ target: input } as unknown as Event);
  }

  it('fills the review form from the OCR preview', () => {
    api.scan.mockReturnValue(of(scanResult));

    selectFile();

    expect(api.scan).toHaveBeenCalledTimes(1);
    expect(page.nationalId()).toBe('29001011234567');
    expect(page.fullName()).toBe('TEST SUBJECT');
    expect(page.address()).toBe('TEST ADDRESS');
    expect(page.lowConfidence()).toBe(true);
    expect(page.showForm()).toBe(true);
    expect(page.scanning()).toBe(false);
  });

  it('derives birth date, gender and governorate from the scanned number', () => {
    api.scan.mockReturnValue(of(scanResult));

    selectFile();

    expect(page.derived()?.birthDate).toBe('1990-01-01');
    expect(page.derived()?.isMale).toBe(false);
    expect(page.derived()?.governorateEnglish).toBe('Dakahlia');
    expect(page.canSave()).toBe(true);
  });

  it('disables saving while the national ID is invalid', () => {
    api.scan.mockReturnValue(of({ ...scanResult, nationalId: '123' }));

    selectFile();

    expect(page.derived()).toBeNull();
    expect(page.canSave()).toBe(false);
  });

  it('recomputes derived fields when the user corrects the number', () => {
    api.scan.mockReturnValue(of(scanResult));
    selectFile();

    page.onNationalIdInput({ target: { value: '30101011234571' } } as unknown as Event);

    expect(page.derived()?.birthDate).toBe('2001-01-01');
    expect(page.derived()?.isMale).toBe(true);
  });

  it('saves the reviewed values and reports the stored id', () => {
    api.scan.mockReturnValue(of(scanResult));
    api.create.mockReturnValue(of({ id: 7 }));

    selectFile();
    page.onFullNameInput({ target: { value: ' CORRECTED NAME ' } } as unknown as Event);
    page.save();

    expect(api.create).toHaveBeenCalledWith({
      nationalId: '29001011234567',
      fullName: 'CORRECTED NAME',
      address: 'TEST ADDRESS',
      lowConfidence: true,
    });
    expect(page.savedId()).toBe(7);
    expect(page.saving()).toBe(false);
  });

  it('surfaces the API error code when saving fails', () => {
    api.scan.mockReturnValue(of(scanResult));
    api.create.mockReturnValue(throwError(() => new ApiError(new Error('duplicate'))));

    selectFile();
    page.save();

    expect(page.errorCode()).toBe('unknown_error');
    expect(page.savedId()).toBeNull();
  });

  it('surfaces the API error code when scanning fails', () => {
    api.scan.mockReturnValue(throwError(() => new ApiError(new Error('bad image'))));

    selectFile();

    expect(page.scanning()).toBe(false);
    expect(page.showForm()).toBe(true);
    expect(page.errorCode()).toBe('unknown_error');
  });
});
