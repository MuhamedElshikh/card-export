import { Injectable, inject, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { AR } from '../i18n/ar';
import { EN } from '../i18n/en';

export type AppLanguage = 'ar' | 'en';

/** Owns the active language and keeps `<html lang/dir>` in sync (Arabic + RTL by default). */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly translate = inject(TranslateService);

  readonly current = signal<AppLanguage>('ar');

  constructor() {
    this.translate.addLangs(['ar', 'en']);
    this.translate.setTranslation('ar', AR);
    this.translate.setTranslation('en', EN);
    this.apply('ar');
  }

  use(language: AppLanguage): void {
    this.apply(language);
  }

  toggle(): void {
    this.apply(this.current() === 'ar' ? 'en' : 'ar');
  }

  private apply(language: AppLanguage): void {
    this.current.set(language);
    this.translate.use(language);

    document.documentElement.lang = language;
    document.documentElement.dir = language === 'ar' ? 'rtl' : 'ltr';
  }
}
