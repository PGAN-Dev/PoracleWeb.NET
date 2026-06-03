import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { LanguageSelectorComponent } from './language-selector.component';
import { I18nService } from '../../../core/services/i18n.service';
import { LocationService } from '../../../core/services/location.service';

describe('LanguageSelectorComponent', () => {
  let component: LanguageSelectorComponent;
  let locationService: { getLanguage: jest.Mock; setLanguage: jest.Mock };
  let snackBar: { open: jest.Mock };

  function setup(overrides: { language?: string | null; setLanguageResult?: 'ok' | 'error' } = {}) {
    const language = overrides.language === undefined ? 'en' : overrides.language;
    locationService = {
      getLanguage: jest.fn(() => of({ language })),
      setLanguage: jest.fn(() => (overrides.setLanguageResult === 'error' ? throwError(() => new Error('fail')) : of(undefined))),
    };
    snackBar = { open: jest.fn() };
    localStorage.clear();

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: LocationService, useValue: locationService },
        { provide: MatSnackBar, useValue: snackBar },
        {
          provide: I18nService,
          useValue: {
            allLanguages: [
              { name: 'English', code: 'en' },
              { name: 'Deutsch', code: 'de' },
            ],
            instant: (key: string) => key,
          },
        },
      ],
      imports: [LanguageSelectorComponent, NoopAnimationsModule],
    }).overrideComponent(LanguageSelectorComponent, {
      // MatSnackBarModule provides MatSnackBar at the component injector, which shadows the
      // module-level test provider — override at component scope so feedback is captured.
      add: { providers: [{ provide: MatSnackBar, useValue: snackBar }] },
    });

    const fixture = TestBed.createComponent(LanguageSelectorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('should create', () => {
    setup();
    expect(component).toBeTruthy();
  });

  it('seeds the selected language from the persisted human.Language', () => {
    setup({ language: 'de' });
    expect(locationService.getLanguage).toHaveBeenCalledTimes(1);
    expect(component['selectedLanguage']()).toBe('de');
    expect(localStorage.getItem('poracle-language')).toBe('de');
  });

  it('ignores a backend language not in the supported set', () => {
    setup({ language: 'xx' });
    expect(component['selectedLanguage']()).toBe('en');
  });

  it('persists a language change and confirms via snackbar', () => {
    setup();
    component.onLanguageChange('de');
    expect(locationService.setLanguage).toHaveBeenCalledWith('de');
    expect(localStorage.getItem('poracle-language')).toBe('de');
    expect(snackBar.open).toHaveBeenCalledWith('AREAS.SNACK_LANGUAGE_UPDATED', 'TOAST.OK', { duration: 3000 });
  });

  it('reverts the selection and warns when the save fails', () => {
    setup({ language: 'en', setLanguageResult: 'error' });
    component.onLanguageChange('de');
    expect(component['selectedLanguage']()).toBe('en');
    expect(localStorage.getItem('poracle-language')).toBe('en');
    expect(snackBar.open).toHaveBeenCalledWith('AREAS.SNACK_LANGUAGE_FAILED', 'TOAST.OK', { duration: 3000 });
  });
});
