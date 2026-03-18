import { Component, inject, signal, OnInit } from '@angular/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';

import { LocationService } from '../../../core/services/location.service';

interface LanguageOption {
  code: string;
  label: string;
}

@Component({
  imports: [MatSelectModule, MatFormFieldModule, MatIconModule],
  selector: 'app-language-selector',
  standalone: true,
  styleUrl: './language-selector.component.scss',
  templateUrl: './language-selector.component.html',
})
export class LanguageSelectorComponent implements OnInit {
  private readonly locationService = inject(LocationService);

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

  protected readonly selectedLanguage = signal('en');

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
