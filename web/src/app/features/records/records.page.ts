import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { IdCardApiService, errorCodeOf } from '../../core/idcard-api.service';
import { LanguageService } from '../../core/language.service';
import { IdCard } from '../../core/models';

@Component({
  selector: 'app-records-page',
  imports: [TranslatePipe],
  templateUrl: './records.page.html',
  styleUrl: './records.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecordsPage implements OnInit {
  private readonly api = inject(IdCardApiService);

  protected readonly language = inject(LanguageService);

  protected readonly items = signal<IdCard[]>([]);
  protected readonly page = signal(1);
  protected readonly totalCount = signal(0);
  protected readonly totalPages = signal(0);
  protected readonly loading = signal(false);
  protected readonly errorCode = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly pageSize = 10;

  ngOnInit(): void {
    this.load();
  }

  protected onSearchInput(event: Event): void {
    this.searchTerm.set((event.target as HTMLInputElement).value);
  }

  protected applySearch(): void {
    this.page.set(1);
    this.load();
  }

  protected clearSearch(): void {
    this.searchTerm.set('');
    this.page.set(1);
    this.load();
  }

  protected goToPage(page: number): void {
    if (page < 1 || (this.totalPages() > 0 && page > this.totalPages()) || this.loading()) {
      return;
    }

    this.page.set(page);
    this.load();
  }

  protected governorateLabel(card: IdCard): string {
    return this.language.current() === 'ar' ? card.governorateArabic : card.governorateEnglish;
  }

  protected genderLabel(card: IdCard): string {
    return card.isMale ? 'common.male' : 'common.female';
  }

  protected birthDateLabel(card: IdCard): string {
    const locale = this.language.current() === 'ar' ? 'ar-EG' : 'en-GB';

    return new Date(`${card.birthDate}T00:00:00Z`).toLocaleDateString(locale, {
      year: 'numeric',
      month: 'short',
      day: '2-digit',
      timeZone: 'UTC',
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorCode.set(null);

    this.api.list(this.searchTerm(), this.page(), this.pageSize).subscribe({
      next: (result) => {
        this.loading.set(false);
        this.items.set(result.items);
        this.totalCount.set(result.totalCount);
        this.totalPages.set(result.totalPages);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.items.set([]);
        this.errorCode.set(errorCodeOf(error));
      },
    });
  }
}
