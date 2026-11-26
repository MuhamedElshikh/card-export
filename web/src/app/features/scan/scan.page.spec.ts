import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { IdCardApiService, ApiError } from '../../core/idcard-api.service';
import { LanguageService } from '../../core/language.service';
import { ScanResult } from '../../core/models';
import { ScanPage } from './scan.page';

describe('ScanPage', () => {
  const scanResult: ScanResult & {
    occupation: string | null;
    maritalStatus: string | null;
    expiryDate: string | null;
  } = {
    nationalId: '29001011234567',
    fullName: 'TEST SUBJECT',
    address: 'TEST ADDRESS',
    occupation: 'مهندس',
    maritalStatus: 'متزوج',
    expiryDate: '2030-01-01',
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

  /** Image preparation is asynchronous, so the scan request starts a moment after the selection. */
  async function selectSide(side: 'frontImage' | 'backImage'): Promise<void> {
    const input = document.createElement('input');
    input.name = side;
    const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], `${side}.png`, { type: 'image/png' });
    Object.defineProperty(input, 'files', { value: [file] });

    page.onFileSelected({ target: input } as unknown as Event);
  }

  async function selectFiles(): Promise<void> {
    await selectSide('frontImage');
    await selectSide('backImage');
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
  }

  it('fills the review form from the OCR preview', async () => {
    api.scan.mockReturnValue(of(scanResult));

    await selectFiles();

    expect(api.scan).toHaveBeenCalledTimes(1);
    expect(api.scan).toHaveBeenCalledWith(expect.any(File), expect.any(File));
    expect(page.nationalId()).toBe('29001011234567');
    expect(page.fullName()).toBe('TEST SUBJECT');
    expect(page.address()).toBe('TEST ADDRESS');
    expect(page.lowConfidence()).toBe(true);
    expect(page.showForm()).toBe(true);
    expect(page.scanning()).toBe(false);
  });

  it('derives birth date, gender and governorate from the scanned number', async () => {
    api.scan.mockReturnValue(of(scanResult));

    await selectFiles();

    expect(page.derived()?.birthDate).toBe('1990-01-01');
    expect(page.derived()?.isMale).toBe(false);
    expect(page.derived()?.governorateEnglish).toBe('Dakahlia');
    expect(page.canSave()).toBe(true);
  });

  it('disables saving while the national ID is invalid', async () => {
    api.scan.mockReturnValue(of({ ...scanResult, nationalId: '123' }));

    await selectFiles();

    expect(page.derived()).toBeNull();
    expect(page.canSave()).toBe(false);
  });

  it('recomputes derived fields when the user corrects the number', async () => {
    api.scan.mockReturnValue(of(scanResult));
    await selectFiles();

    page.onNationalIdInput({ target: { value: '30101011234571' } } as unknown as Event);

    expect(page.derived()?.birthDate).toBe('2001-01-01');
    expect(page.derived()?.isMale).toBe(true);
  });

  it('saves the reviewed values and reports the stored id', async () => {
    api.scan.mockReturnValue(of(scanResult));
    api.create.mockReturnValue(of({ id: 7 }));

    await selectFiles();
    page.onFullNameInput({ target: { value: ' CORRECTED NAME ' } } as unknown as Event);
    page.save();

    expect(api.create).toHaveBeenCalledWith({
      nationalId: '29001011234567',
      fullName: 'CORRECTED NAME',
      address: 'TEST ADDRESS',
      lowConfidence: true,
      occupation: 'مهندس',
      maritalStatus: 'متزوج',
      expiryDate: '2030-01-01',
    });
    expect(page.savedId()).toBe(7);
    expect(page.saving()).toBe(false);
  });

  it('shows and saves the values read from the back of the card', async () => {
    api.scan.mockReturnValue(of(scanResult));
    api.create.mockReturnValue(of({ id: 8 }));

    await selectFiles();

    const fields = page as ScanPage & {
      occupation: { (): string; set(value: string): void };
      maritalStatus: { (): string; set(value: string): void };
      expiryDate: { (): string; set(value: string): void };
    };

    expect(fields.occupation()).toBe('مهندس');
    expect(fields.maritalStatus()).toBe('متزوج');
    expect(fields.expiryDate()).toBe('2030-01-01');

    fields.occupation.set('طبيب');
    fields.maritalStatus.set('أعزب');
    fields.expiryDate.set('2031-02-03');
    page.save();

    expect(api.create).toHaveBeenCalledWith(
      expect.objectContaining({
        occupation: 'طبيب',
        maritalStatus: 'أعزب',
        expiryDate: '2031-02-03',
      }),
    );
  });

  it('surfaces the API error code when saving fails', async () => {
    api.scan.mockReturnValue(of(scanResult));
    api.create.mockReturnValue(throwError(() => new ApiError(new Error('duplicate'))));

    await selectFiles();
    page.save();

    expect(page.errorCode()).toBe('unknown_error');
    expect(page.savedId()).toBeNull();
  });

  it('surfaces the API error code when scanning fails', async () => {
    api.scan.mockReturnValue(throwError(() => new ApiError(new Error('bad image'))));

    await selectFiles();

    expect(page.scanning()).toBe(false);
    expect(page.showForm()).toBe(true);
    expect(page.errorCode()).toBe('unknown_error');
  });

  it('cancels a scan in progress and aborts the request', async () => {
    const unsubscribed = vi.fn();
    api.scan.mockReturnValue(new Observable<ScanResult>(() => unsubscribed));

    await selectFiles();
    expect(page.scanning()).toBe(true);

    page.cancelScan();

    expect(page.scanning()).toBe(false);
    expect(unsubscribed).toHaveBeenCalledTimes(1);
    expect(page.showForm()).toBe(false);
  });

  it('ignores a late result from a scan that was cancelled before the request started', async () => {
    api.scan.mockReturnValue(of(scanResult));

    const input = document.createElement('input');
    const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'card.png', { type: 'image/png' });
    Object.defineProperty(input, 'files', { value: [file] });

    input.name = 'frontImage';
    page.onFileSelected({ target: input } as unknown as Event);
    const back = document.createElement('input');
    back.name = 'backImage';
    Object.defineProperty(back, 'files', { value: [new File([new Uint8Array([1, 2, 3])], 'back.png')] });
    page.onFileSelected({ target: back } as unknown as Event);
    page.cancelScan();
    await new Promise<void>((resolve) => setTimeout(resolve, 0));

    expect(api.scan).not.toHaveBeenCalled();
    expect(page.scanning()).toBe(false);
    expect(page.showForm()).toBe(false);
  });

  it('only the latest of two quick selections reaches the API', async () => {
    api.scan.mockReturnValue(of(scanResult));

    const make = (): Event => {
      const input = document.createElement('input');
      const file = new File([new Uint8Array([1, 2, 3])], 'card.png', { type: 'image/png' });
      Object.defineProperty(input, 'files', { value: [file] });
      return { target: input } as unknown as Event;
    };

    const front = make();
    (front.target as HTMLInputElement).name = 'frontImage';
    const back = make();
    (back.target as HTMLInputElement).name = 'backImage';
    page.onFileSelected(front);
    page.onFileSelected(back);
    await new Promise<void>((resolve) => setTimeout(resolve, 0));

    expect(api.scan).toHaveBeenCalledTimes(1);
  });

  it('does not start OCR until both sides have been selected', async () => {
    api.scan.mockReturnValue(of(scanResult));

    await selectSide('frontImage');
    await new Promise<void>((resolve) => setTimeout(resolve, 0));

    expect(api.scan).not.toHaveBeenCalled();
    expect(page.scanning()).toBe(false);

    await selectSide('backImage');
    await new Promise<void>((resolve) => setTimeout(resolve, 0));

    expect(api.scan).toHaveBeenCalledTimes(1);
  });

  it('requires both sides again after a completed scan before scanning a new card', async () => {
    api.scan.mockReturnValue(of(scanResult));

    await selectFiles();
    await selectSide('frontImage');
    await new Promise<void>((resolve) => setTimeout(resolve, 0));

    expect(api.scan).toHaveBeenCalledTimes(1);

    await selectSide('backImage');
    await new Promise<void>((resolve) => setTimeout(resolve, 0));

    expect(api.scan).toHaveBeenCalledTimes(2);
  });
});
