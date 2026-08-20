import { Injectable, inject, signal } from '@angular/core';

import { I18nService } from './i18n.service';
import { LocationService } from './location.service';

const STORAGE_KEY = 'poracle-language';

/**
 * The language Poracle writes your alerts in: DM text and Pokemon names.
 *
 * Distinct from the display language, which only changes the UI. It lives in a service rather than a
 * component so the user menu can render it as flag rows beside the display-language menu, matching it
 * row for row — the two are only distinguishable at a glance if they look like siblings.
 */
@Injectable({ providedIn: 'root' })
export class AlertLanguageService {
  private readonly i18n = inject(I18nService);
  private readonly locationService = inject(LocationService);

  /** Every language Poracle can write alerts in. */
  readonly languages = this.i18n.allLanguages;

  readonly selected = signal<string>(localStorage.getItem(STORAGE_KEY) ?? 'en');

  /** Sets the alert language, rolling back if the write fails. Returns whether it stuck. */
  choose(locale: string): Promise<boolean> {
    const previous = this.selected();
    this.selected.set(locale);
    localStorage.setItem(STORAGE_KEY, locale);

    return new Promise(resolve => {
      this.locationService.setLanguage(locale).subscribe({
        error: () => {
          this.selected.set(previous);
          localStorage.setItem(STORAGE_KEY, previous);
          resolve(false);
        },
        next: () => resolve(true),
      });
    });
  }

  /**
   * Reconciles with the authoritative human.Language. The localStorage value is only a hint for an
   * instant first render, and the bot can change the real one out of band.
   */
  load(): void {
    this.locationService.getLanguage().subscribe({
      error: () => undefined,
      next: ({ language }) => {
        if (language && this.languages.some(l => l.code === language)) {
          this.selected.set(language);
          localStorage.setItem(STORAGE_KEY, language);
        }
      },
    });
  }
}
