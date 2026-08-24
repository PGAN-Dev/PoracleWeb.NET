import { Injectable, computed, inject, signal } from '@angular/core';

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
  /**
   * A language this user has actually been given, from localStorage or from humans.language. Null means
   * nobody has ever chosen one, which is the only case where Poracle's own locale gets to decide.
   */
  private readonly chosen = signal<string | null>(localStorage.getItem(STORAGE_KEY));
  private readonly i18n = inject(I18nService);

  private readonly locationService = inject(LocationService);

  /** Every language Poracle can write alerts in. */
  readonly languages = this.i18n.allLanguages;

  /**
   * The language Poracle will actually write in, or null when we cannot tell.
   *
   * Deliberately not `selected()`. That one coerces to 'en' so the picker always has a row highlighted,
   * which is right for a menu and wrong for anything that acts on the answer: when Poracle's own locale
   * maps onto no UI language (ja, ru, zh-cn), coercing would claim English and put Japanese prose on an
   * English card. Null says "unknown", and callers are expected to do nothing rather than guess.
   */
  readonly resolved = computed<string | null>(() => this.chosen() ?? this.i18n.serverDefaultLanguage());

  readonly selected = computed(() => this.chosen() ?? this.i18n.serverDefaultLanguage() ?? 'en');

  /** Sets the alert language, rolling back if the write fails. Returns whether it stuck. */
  choose(locale: string): Promise<boolean> {
    const previous = this.chosen();
    this.chosen.set(locale);
    localStorage.setItem(STORAGE_KEY, locale);

    return new Promise(resolve => {
      this.locationService.setLanguage(locale).subscribe({
        error: () => {
          this.chosen.set(previous);
          if (previous === null) localStorage.removeItem(STORAGE_KEY);
          else localStorage.setItem(STORAGE_KEY, previous);
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
        // Case-insensitively, and stored back in this list's casing. Poracle lowercases what it stores,
        // on both API versions and from the bot's own !language command, so humans.language for a
        // Brazilian Portuguese user reads back as 'pt-br' while the code here is 'pt-BR'. An exact
        // comparison dropped it silently and the picker fell back to the server default.
        const known = language ? this.languages.find(l => l.code.toLowerCase() === language.toLowerCase()) : undefined;
        if (known) {
          this.chosen.set(known.code);
          localStorage.setItem(STORAGE_KEY, known.code);
        }
      },
    });
  }
}
