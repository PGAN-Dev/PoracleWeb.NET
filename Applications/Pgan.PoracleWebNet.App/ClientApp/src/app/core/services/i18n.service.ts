import { Injectable, computed, inject, signal } from '@angular/core';
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

  /** Poracle's configured locale, verbatim. Empty until the settings response arrives. */
  private readonly serverLocale = signal('');

  /**
   * How the active language was chosen. Only a language that fell through to the hardcoded default is
   * still open to being replaced by the server locale, which arrives after the first init() -- a user's
   * stored choice and a browser match both outrank it and must not be overwritten when it lands.
   */
  private source: 'browser' | 'fallback' | 'stored' = 'fallback';

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
  readonly availableLanguages = computed(() => {
    const allowed = this.allowedCodes();
    if (allowed.length === 0) return this.allLanguages;
    return this.allLanguages.filter(l => l.code === 'en' || allowed.includes(l.code));
  });

  /** Currently active UI language code. */
  readonly currentLang = signal('en');

  /**
   * Poracle's locale mapped onto a language this UI actually ships and the admin actually permits,
   * or null when it maps onto neither. PoracleNG carries translations we do not (ja, ru, zh-cn), and
   * an admin can restrict the UI to a subset, so both filters have to pass before it can be a default.
   */
  readonly serverDefaultLanguage = computed<string | null>(() => {
    const raw = this.serverLocale().trim().toLowerCase();
    if (!raw) return null;

    const available = this.availableLanguages();
    const exact = available.find(l => l.code.toLowerCase() === raw);
    if (exact) return exact.code;

    const base = raw.split('-')[0];
    return available.find(l => l.code.toLowerCase() === base)?.code ?? null;
  });

  /**
   * Initialize the translation service. Safe to call multiple times.
   * The first call sets the active language. Later calls carry the admin settings, which arrive after
   * bootstrap: they update the allowed list, and may swap in Poracle's locale if the first call had
   * nothing better than the hardcoded fallback to go on.
   */
  init(allowedLanguages?: string, serverLocale?: string): void {
    if (allowedLanguages) {
      this.allowedCodes.set(
        allowedLanguages
          .split(',')
          .map(c => c.trim())
          .filter(Boolean),
      );
    }

    if (serverLocale !== undefined) this.serverLocale.set(serverLocale);

    if (this.initialized) {
      if (this.source === 'fallback') {
        const fromServer = this.serverDefaultLanguage();
        if (fromServer && fromServer !== this.currentLang()) this.apply(fromServer, false);
      }
      return;
    }

    this.initialized = true;

    this.translate.addLangs(this.allLanguages.map(l => l.code));
    this.translate.setFallbackLang('en');

    const stored = localStorage.getItem(STORAGE_KEY);
    const detected = stored ? null : this.detectBrowserLanguage();
    this.source = stored ? 'stored' : detected ? 'browser' : 'fallback';

    this.apply(stored || detected || this.serverDefaultLanguage() || 'en', false);
  }

  /** Returns a translated string synchronously (for use in TypeScript code). */
  instant(key: string, params?: Record<string, unknown>): string {
    return this.translate.instant(key, params);
  }

  /** Switch UI language, and remember it as this user's choice. */
  use(code: string): void {
    this.apply(code, true);
  }

  /**
   * Switches language, persisting only a deliberate choice. A detected or server-supplied default is
   * left unwritten so it can be re-decided next visit: persisting it made the first load authoritative
   * forever, which meant a visitor who arrived while Poracle was unreachable, and so fell through to
   * English, stayed on English no matter what locale the server reported afterwards.
   */
  private apply(code: string, persist: boolean): void {
    const valid = this.allLanguages.some(l => l.code === code);
    const lang = valid ? code : 'en';
    this.translate.use(lang);
    this.currentLang.set(lang);
    if (persist) localStorage.setItem(STORAGE_KEY, lang);
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
