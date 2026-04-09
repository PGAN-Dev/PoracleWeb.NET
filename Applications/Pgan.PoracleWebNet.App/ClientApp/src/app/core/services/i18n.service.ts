import { Injectable, inject, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

export interface UiLanguage {
  code: string;
  /** Two-letter country code for display (emoji flags don't render on Windows) */
  countryCode: string;
  flag: string;
  name: string;
}

const STORAGE_KEY = 'poracle-ui-language';

@Injectable({ providedIn: 'root' })
export class I18nService {
  /** Languages enabled by admin (subset of allLanguages). When empty, all are enabled. */
  private allowedCodes = signal<string[]>([]);

  private initialized = false;

  private readonly translate = inject(TranslateService);

  /** All languages supported by the UI (matching PoracleWeb PHP). */
  readonly allLanguages: UiLanguage[] = [
    { name: 'English', code: 'en', countryCode: 'gb', flag: '\u{1F1EC}\u{1F1E7}' },
    { name: 'Fran\u00E7ais', code: 'fr', countryCode: 'fr', flag: '\u{1F1EB}\u{1F1F7}' },
    { name: 'Deutsch', code: 'de', countryCode: 'de', flag: '\u{1F1E9}\u{1F1EA}' },
    { name: 'Espa\u00F1ol', code: 'es', countryCode: 'es', flag: '\u{1F1EA}\u{1F1F8}' },
    { name: 'Nederlands', code: 'nl', countryCode: 'nl', flag: '\u{1F1F3}\u{1F1F1}' },
    { name: 'Italiano', code: 'it', countryCode: 'it', flag: '\u{1F1EE}\u{1F1F9}' },
    { name: 'Portugu\u00EAs', code: 'pt', countryCode: 'pt', flag: '\u{1F1F5}\u{1F1F9}' },
    { name: 'Portugu\u00EAs (BR)', code: 'pt-BR', countryCode: 'br', flag: '\u{1F1E7}\u{1F1F7}' },
    { name: 'Polski', code: 'pl', countryCode: 'pl', flag: '\u{1F1F5}\u{1F1F1}' },
    { name: 'Dansk', code: 'da', countryCode: 'dk', flag: '\u{1F1E9}\u{1F1F0}' },
    { name: 'Svenska', code: 'sv', countryCode: 'se', flag: '\u{1F1F8}\u{1F1EA}' },
  ];

  /** Languages available for selection (filtered by admin setting). */
  readonly availableLanguages = () => {
    const allowed = this.allowedCodes();
    if (allowed.length === 0) return this.allLanguages;
    return this.allLanguages.filter(l => l.code === 'en' || allowed.includes(l.code));
  };

  /** Currently active UI language code. */
  readonly currentLang = signal('en');

  /**
   * Initialize the translation service. Safe to call multiple times.
   * First call sets the active language. Subsequent calls only update allowed languages.
   */
  init(allowedLanguages?: string): void {
    if (allowedLanguages) {
      this.allowedCodes.set(
        allowedLanguages
          .split(',')
          .map(c => c.trim())
          .filter(Boolean),
      );
    }

    if (this.initialized) return;
    this.initialized = true;

    this.translate.addLangs(this.allLanguages.map(l => l.code));
    this.translate.setDefaultLang('en');

    const stored = localStorage.getItem(STORAGE_KEY);
    const detected = this.detectBrowserLanguage();
    const lang = stored || detected || 'en';

    this.use(lang);
  }

  /** Returns a translated string synchronously (for use in TypeScript code). */
  instant(key: string, params?: Record<string, unknown>): string {
    return this.translate.instant(key, params);
  }

  /** Switch UI language. */
  use(code: string): void {
    const valid = this.allLanguages.some(l => l.code === code);
    const lang = valid ? code : 'en';
    this.translate.use(lang);
    this.currentLang.set(lang);
    localStorage.setItem(STORAGE_KEY, lang);
    document.documentElement.lang = lang;
  }

  /** Detect best matching language from browser settings. */
  private detectBrowserLanguage(): string | null {
    const browserLangs = navigator.languages || [navigator.language];
    const codes = this.allLanguages.map(l => l.code);

    for (const bl of browserLangs) {
      // Exact match (e.g., pt-BR)
      if (codes.includes(bl)) return bl;
      // Base language match (e.g., "de-AT" -> "de")
      const base = bl.split('-')[0];
      if (codes.includes(base)) return base;
    }
    return null;
  }
}
