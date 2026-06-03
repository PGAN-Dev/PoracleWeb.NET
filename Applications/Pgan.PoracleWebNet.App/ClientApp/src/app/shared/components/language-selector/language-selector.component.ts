import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslateModule } from '@ngx-translate/core';

import { I18nService } from '../../../core/services/i18n.service';
import { LocationService } from '../../../core/services/location.service';

const STORAGE_KEY = 'poracle-language';

/**
 * Sets the user's Poracle notification (DM) language — the language Poracle uses for alert text
 * and Pokémon names — by writing `human.Language` via PUT /api/location/language. This is distinct
 * from the toolbar display-language menu, which only changes the Angular UI translations.
 */
@Component({
  imports: [MatSelectModule, MatFormFieldModule, MatIconModule, MatSnackBarModule, TranslateModule],
  selector: 'app-language-selector',
  standalone: true,
  styleUrl: './language-selector.component.scss',
  templateUrl: './language-selector.component.html',
})
export class LanguageSelectorComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly locationService = inject(LocationService);
  private readonly snackBar = inject(MatSnackBar);

  /** Notification-language options, reconciled with the languages the app actually supports. */
  readonly languages = this.i18n.allLanguages;

  protected readonly selectedLanguage = signal('en');

  ngOnInit(): void {
    // Seed from the persisted localStorage hint for an instant render, then reconcile with the
    // authoritative human.Language from the backend (which the bot can also change out-of-band).
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) {
      this.selectedLanguage.set(stored);
    }

    this.locationService
      .getLanguage()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({ language }) => {
        if (language && this.languages.some(l => l.code === language)) {
          this.selectedLanguage.set(language);
          localStorage.setItem(STORAGE_KEY, language);
        }
      });
  }

  onLanguageChange(locale: string): void {
    const previous = this.selectedLanguage();
    this.selectedLanguage.set(locale);
    localStorage.setItem(STORAGE_KEY, locale);
    this.locationService
      .setLanguage(locale)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.selectedLanguage.set(previous);
          localStorage.setItem(STORAGE_KEY, previous);
          this.snackBar.open(this.i18n.instant('AREAS.SNACK_LANGUAGE_FAILED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: () => {
          this.snackBar.open(this.i18n.instant('AREAS.SNACK_LANGUAGE_UPDATED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
      });
  }
}
