import { Component, inject, signal, OnInit } from '@angular/core';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { LocationService } from '../../../core/services/location.service';

interface LanguageOption {
  code: string;
  label: string;
}

@Component({
  selector: 'app-language-selector',
  standalone: true,
  imports: [MatSelectModule, MatFormFieldModule, MatIconModule],
  template: `
    <mat-form-field appearance="outline" class="language-field">
      <mat-icon matPrefix>language</mat-icon>
      <mat-select
        [value]="selectedLanguage()"
        (selectionChange)="onLanguageChange($event.value)"
        panelClass="language-panel"
      >
        @for (lang of languages; track lang.code) {
          <mat-option [value]="lang.code">{{ lang.label }}</mat-option>
        }
      </mat-select>
    </mat-form-field>
  `,
  styles: [
    `
      .language-field {
        width: 110px;
        margin: 0 4px;

        ::ng-deep .mat-mdc-form-field-subscript-wrapper {
          display: none;
        }

        ::ng-deep .mat-mdc-text-field-wrapper {
          height: 36px;
          padding: 0 8px;
        }

        ::ng-deep .mat-mdc-form-field-infix {
          padding: 4px 0;
          min-height: unset;
        }

        ::ng-deep .mat-mdc-select-trigger {
          font-size: 13px;
        }
      }
    `,
  ],
})
export class LanguageSelectorComponent implements OnInit {
  private readonly locationService = inject(LocationService);

  protected readonly selectedLanguage = signal('en');

  readonly languages: LanguageOption[] = [
    { code: 'en', label: 'English' },
    { code: 'de', label: 'Deutsch' },
    { code: 'fr', label: 'Francais' },
    { code: 'es', label: 'Espanol' },
    { code: 'it', label: 'Italiano' },
    { code: 'pt', label: 'Portugues' },
    { code: 'ja', label: 'Japanese' },
    { code: 'ko', label: 'Korean' },
    { code: 'zh', label: 'Chinese' },
    { code: 'ru', label: 'Russian' },
    { code: 'pl', label: 'Polski' },
    { code: 'nl', label: 'Nederlands' },
    { code: 'sv', label: 'Svenska' },
    { code: 'no', label: 'Norsk' },
    { code: 'da', label: 'Dansk' },
    { code: 'fi', label: 'Suomi' },
    { code: 'th', label: 'Thai' },
    { code: 'tr', label: 'Turkish' },
  ];

  ngOnInit(): void {
    const stored = localStorage.getItem('poracle-language');
    if (stored) {
      this.selectedLanguage.set(stored);
    }
  }

  onLanguageChange(locale: string): void {
    this.selectedLanguage.set(locale);
    localStorage.setItem('poracle-language', locale);
    this.locationService.setLanguage(locale).subscribe();
  }
}
