import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { IdCardApiService, errorCodeOf } from '../../core/idcard-api.service';
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

  readonly language = inject(LanguageService);

  readonly nationalId = signal('');
  readonly fullName = signal('');
  readonly address = signal('');
  readonly lowConfidence = signal(false);

  readonly previewUrl = signal<string | null>(null);
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
    this.destroyRef.onDestroy(() => this.releasePreview());
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';

    if (!file) {
      return;
    }

    this.startScan(file);
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
    this.releasePreview();
    this.previewUrl.set(null);
    this.nationalId.set('');
    this.fullName.set('');
    this.address.set('');
    this.lowConfidence.set(false);
    this.showForm.set(false);
    this.errorCode.set(null);
    this.savedId.set(null);
    this.nationalIdTouched.set(false);
  }

  governorateLabel(derived: DerivedInfo): string {
    return this.language.current() === 'ar' ? derived.governorateArabic : derived.governorateEnglish;
  }

  private startScan(file: File): void {
    this.releasePreview();
    this.errorCode.set(null);
    this.savedId.set(null);
    this.previewUrl.set(URL.createObjectURL(file));
    this.scanning.set(true);

    this.api.scan(file).subscribe({
      next: (result) => {
        this.scanning.set(false);
        this.showForm.set(true);
        this.nationalId.set(result.nationalId ?? '');
        this.fullName.set(result.fullName ?? '');
        this.address.set(result.address ?? '');
        this.lowConfidence.set(result.lowConfidence);
        this.nationalIdTouched.set(true);
      },
      error: (error: unknown) => {
        this.scanning.set(false);
        this.showForm.set(true);
        this.errorCode.set(errorCodeOf(error));
      },
    });
  }

  private releasePreview(): void {
    const url = this.previewUrl();
    if (url) {
      URL.revokeObjectURL(url);
    }
  }
}
