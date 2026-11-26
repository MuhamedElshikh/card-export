import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { IdCardApiService, errorCodeOf } from '../../core/idcard-api.service';
import { prepareImage } from '../../core/image-prepare';
import { LanguageService } from '../../core/language.service';
import { DerivedInfo, parseNationalId } from '../../core/national-id';

@Component({
  selector: 'app-scan-page',
  imports: [TranslatePipe],
  templateUrl: './scan.page.html',
  styleUrl: './scan.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ScanPage {
  private readonly api = inject(IdCardApiService);
  private readonly destroyRef = inject(DestroyRef);

  /** Identifies the scan that is allowed to deliver results; bumping it abandons older scans. */
  private scanRun = 0;
  private scanSubscription: Subscription | null = null;

  readonly language = inject(LanguageService);

  readonly nationalId = signal('');
  readonly fullName = signal('');
  readonly address = signal('');
  readonly occupation = signal('');
  readonly maritalStatus = signal('');
  readonly expiryDate = signal('');
  readonly lowConfidence = signal(false);

  readonly frontPreviewUrl = signal<string | null>(null);
  readonly backPreviewUrl = signal<string | null>(null);
  private readonly frontImage = signal<File | null>(null);
  private readonly backImage = signal<File | null>(null);
  readonly scanning = signal(false);
  readonly saving = signal(false);
  readonly showForm = signal(false);
  readonly errorCode = signal<string | null>(null);
  readonly savedId = signal<number | null>(null);

  readonly derived = computed<DerivedInfo | null>(() => parseNationalId(this.nationalId()));
  readonly nationalIdTouched = signal(false);
  readonly canSave = computed(
    () => this.derived() !== null && this.fullName().trim().length > 0 && !this.saving(),
  );

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.abandonScan();
      this.releasePreview();
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    const side = input.name;
    input.value = '';

    if (!file || (side !== 'frontImage' && side !== 'backImage')) {
      return;
    }

    const replacingReviewedCard = this.showForm();
    this.abandonScan();
    this.scanning.set(false);
    this.showForm.set(false);
    this.errorCode.set(null);
    this.savedId.set(null);

    if (replacingReviewedCard) {
      this.clearImages();
    }

    if (side === 'frontImage') {
      this.frontImage.set(file);
      this.replacePreview('front', file);
    } else {
      this.backImage.set(file);
      this.replacePreview('back', file);
    }

    const front = this.frontImage();
    const back = this.backImage();
    if (front && back) {
      this.startScan(front, back);
    }
  }

  /** Stops the scan in progress. The in-flight request is aborted and the preview is kept. */
  cancelScan(): void {
    if (!this.scanning()) {
      return;
    }

    this.abandonScan();
    this.scanning.set(false);
  }

  onNationalIdInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.nationalId.set(value);
    this.nationalIdTouched.set(true);
    this.savedId.set(null);
  }

  onFullNameInput(event: Event): void {
    this.fullName.set((event.target as HTMLInputElement).value);
  }

  onAddressInput(event: Event): void {
    this.address.set((event.target as HTMLTextAreaElement).value);
  }

  onOccupationInput(event: Event): void {
    this.occupation.set((event.target as HTMLInputElement).value);
  }

  onMaritalStatusInput(event: Event): void {
    this.maritalStatus.set((event.target as HTMLInputElement).value);
  }

  onExpiryDateInput(event: Event): void {
    this.expiryDate.set((event.target as HTMLInputElement).value);
  }

  save(): void {
    const derived = this.derived();
    if (!derived || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorCode.set(null);
    this.savedId.set(null);

    this.api
      .create({
        nationalId: derived.nationalId,
        fullName: this.fullName().trim(),
        address: this.address().trim(),
        lowConfidence: this.lowConfidence(),
        occupation: this.occupation().trim() || null,
        maritalStatus: this.maritalStatus().trim() || null,
        expiryDate: this.expiryDate() || null,
      })
      .subscribe({
        next: (card) => {
          this.saving.set(false);
          this.savedId.set(card.id);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.errorCode.set(errorCodeOf(error));
        },
      });
  }

  reset(): void {
    this.abandonScan();
    this.scanning.set(false);
    this.clearImages();
    this.nationalId.set('');
    this.fullName.set('');
    this.address.set('');
    this.occupation.set('');
    this.maritalStatus.set('');
    this.expiryDate.set('');
    this.lowConfidence.set(false);
    this.showForm.set(false);
    this.errorCode.set(null);
    this.savedId.set(null);
    this.nationalIdTouched.set(false);
  }

  governorateLabel(derived: DerivedInfo): string {
    return this.language.current() === 'ar' ? derived.governorateArabic : derived.governorateEnglish;
  }

  private startScan(frontFile: File, backFile: File): void {
    this.abandonScan();
    this.errorCode.set(null);
    this.savedId.set(null);
    this.nationalId.set('');
    this.fullName.set('');
    this.address.set('');
    this.occupation.set('');
    this.maritalStatus.set('');
    this.expiryDate.set('');
    this.lowConfidence.set(false);
    this.scanning.set(true);
    this.showForm.set(false);

    const run = ++this.scanRun;

    // Large phone photos are shrunk in the browser first; the original is used if that fails.
    void Promise.all([prepareImage(frontFile), prepareImage(backFile)]).then(([front, back]) => {
      if (run !== this.scanRun) {
        return;
      }

      this.scanSubscription = this.api.scan(front, back).subscribe({
        next: (result) => {
          this.scanning.set(false);
          this.showForm.set(true);
          this.nationalId.set(result.nationalId ?? '');
          this.fullName.set(result.fullName ?? '');
          this.address.set(result.address ?? '');
          this.occupation.set(result.occupation ?? '');
          this.maritalStatus.set(result.maritalStatus ?? '');
          this.expiryDate.set(result.expiryDate ?? '');
          this.lowConfidence.set(result.lowConfidence);
          this.nationalIdTouched.set(true);
        },
        error: (error: unknown) => {
          this.scanning.set(false);
          this.showForm.set(true);
          this.errorCode.set(errorCodeOf(error));
        },
      });
    });
  }

  /** Invalidates the current scan and aborts its HTTP request, if one was started. */
  private abandonScan(): void {
    this.scanRun++;
    this.scanSubscription?.unsubscribe();
    this.scanSubscription = null;
  }

  private releasePreview(): void {
    for (const url of [this.frontPreviewUrl(), this.backPreviewUrl()]) {
      if (url) {
        URL.revokeObjectURL(url);
      }
    }
  }

  private clearImages(): void {
    this.releasePreview();
    this.frontPreviewUrl.set(null);
    this.backPreviewUrl.set(null);
    this.frontImage.set(null);
    this.backImage.set(null);
  }

  private replacePreview(side: 'front' | 'back', file: File): void {
    const current = side === 'front' ? this.frontPreviewUrl() : this.backPreviewUrl();
    if (current) {
      URL.revokeObjectURL(current);
    }

    const next = URL.createObjectURL(file);
    if (side === 'front') {
      this.frontPreviewUrl.set(next);
    } else {
      this.backPreviewUrl.set(next);
    }
  }
}
