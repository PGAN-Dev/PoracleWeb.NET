import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { Observable, of, throwError } from 'rxjs';

import { AlertLanguageService } from './alert-language.service';
import { I18nService } from './i18n.service';
import { LocationService } from './location.service';

describe('AlertLanguageService', () => {
  let locationService: { getLanguage: jest.Mock; setLanguage: jest.Mock };
  let store: Record<string, string>;

  const create = (): { alert: AlertLanguageService; i18n: I18nService } => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), { provide: LocationService, useValue: locationService }],
    });
    return { alert: TestBed.inject(AlertLanguageService), i18n: TestBed.inject(I18nService) };
  };

  beforeEach(() => {
    store = {};
    jest.spyOn(Storage.prototype, 'getItem').mockImplementation((key: string) => store[key] ?? null);
    jest.spyOn(Storage.prototype, 'setItem').mockImplementation((key: string, value: string) => {
      store[key] = value;
    });
    jest.spyOn(Storage.prototype, 'removeItem').mockImplementation((key: string) => {
      delete store[key];
    });
    // A browser the UI ships nothing for, so the server locale is the only thing left to fall back on.
    jest.spyOn(navigator, 'languages', 'get').mockReturnValue(['ja-JP', 'ja']);

    locationService = {
      getLanguage: jest.fn<Observable<{ language: null | string }>, []>(() => of({ language: null })),
      setLanguage: jest.fn<Observable<void>, [string]>(() => of(undefined)),
    };
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('should default to en when nothing else says otherwise', () => {
    const { alert, i18n } = create();
    i18n.init();

    expect(alert.selected()).toBe('en');
  });

  it("should default to Poracle's locale when the user has never chosen one", () => {
    const { alert, i18n } = create();
    i18n.init(undefined, 'de');

    expect(alert.selected()).toBe('de');
  });

  it('should adopt a server locale that arrives after the service is constructed', () => {
    const { alert, i18n } = create();
    expect(alert.selected()).toBe('en');

    i18n.init(undefined, 'de');

    expect(alert.selected()).toBe('de');
  });

  it('should keep a stored choice ahead of the server locale', () => {
    store['poracle-language'] = 'fr';
    const { alert, i18n } = create();
    i18n.init(undefined, 'de');

    expect(alert.selected()).toBe('fr');
  });

  it('should fall back to en for a locale this UI does not ship', () => {
    const { alert, i18n } = create();
    i18n.init(undefined, 'ru');

    expect(alert.selected()).toBe('en');
  });

  it('should fall back to en for a locale the admin excluded from allowed_languages', () => {
    const { alert, i18n } = create();
    i18n.init('en,fr', 'de');

    expect(alert.selected()).toBe('en');
  });

  it('should let humans.language override the server locale', () => {
    locationService.getLanguage.mockReturnValue(of({ language: 'it' }));
    const { alert, i18n } = create();
    i18n.init(undefined, 'de');

    alert.load();

    expect(alert.selected()).toBe('it');
    expect(store['poracle-language']).toBe('it');
  });

  it('should recognise a stored language whose case Poracle changed', () => {
    // Poracle lowercases what it stores, on both API versions and from the bot's !language command, so
    // humans.language reads back 'pt-br' where this list says 'pt-BR'. An exact match dropped it and
    // the picker silently reverted to the server default.
    locationService.getLanguage.mockReturnValue(of({ language: 'pt-br' }));
    const { alert, i18n } = create();
    i18n.init(undefined, 'de');

    alert.load();

    expect(alert.selected()).toBe('pt-BR');
    expect(store['poracle-language']).toBe('pt-BR');
  });

  it('should still ignore a language this UI does not ship', () => {
    // The other half: Poracle carries translations we do not, and coercing one of them onto a UI
    // language would put Japanese prose behind an English flag.
    locationService.getLanguage.mockReturnValue(of({ language: 'ja' }));
    const { alert, i18n } = create();
    i18n.init(undefined, 'de');

    alert.load();

    expect(alert.selected()).toBe('de');
    expect(store['poracle-language']).toBeUndefined();
  });

  it('should keep the server locale when humans.language is unset', () => {
    const { alert, i18n } = create();
    i18n.init(undefined, 'de');

    alert.load();

    expect(alert.selected()).toBe('de');
  });

  it('should store the chosen language and report success', async () => {
    const { alert, i18n } = create();
    i18n.init(undefined, 'de');

    await expect(alert.choose('sv')).resolves.toBe(true);

    expect(alert.selected()).toBe('sv');
    expect(store['poracle-language']).toBe('sv');
  });

  it('should roll back to the server locale when the write fails and nothing was stored', async () => {
    locationService.setLanguage.mockReturnValue(throwError(() => new Error('nope')));
    const { alert, i18n } = create();
    i18n.init(undefined, 'de');

    await expect(alert.choose('sv')).resolves.toBe(false);

    expect(alert.selected()).toBe('de');
    expect(store['poracle-language']).toBeUndefined();
  });

  it('should roll back to the previous stored choice when the write fails', async () => {
    store['poracle-language'] = 'fr';
    locationService.setLanguage.mockReturnValue(throwError(() => new Error('nope')));
    const { alert, i18n } = create();
    i18n.init(undefined, 'de');

    await expect(alert.choose('sv')).resolves.toBe(false);

    expect(alert.selected()).toBe('fr');
    expect(store['poracle-language']).toBe('fr');
  });
  describe('the languages Poracle will accept', () => {
    it('should offer all eleven when Poracle restricts nothing', () => {
      const { alert, i18n } = create();
      i18n.init();
      alert.restrictTo(undefined);

      expect(alert.languages().length).toBe(i18n.allLanguages.length);
    });

    it('should offer all eleven on a Poracle too old to say', () => {
      // 5.1.0 has no availableLanguages field, so the settings response carries no such key at all.
      const { alert, i18n } = create();
      i18n.init();

      expect(alert.languages().length).toBe(i18n.allLanguages.length);
    });

    it('should offer only what Poracle accepts when it is restricted', () => {
      const { alert, i18n } = create();
      i18n.init();
      alert.restrictTo('en,de,ja');

      // ja is Poracle's to offer and not this UI's to render -- there is no flag row for it.
      expect(alert.languages().map(l => l.code)).toEqual(['en', 'de']);
    });

    it('should match case-insensitively, because Poracle keeps its own casing', () => {
      const { alert, i18n } = create();
      i18n.init();
      alert.restrictTo('EN,pt-br');

      expect(alert.languages().map(l => l.code)).toEqual(['en', 'pt-BR']);
    });

    it('should offer nothing when Poracle accepts nothing this UI ships', () => {
      const { alert, i18n } = create();
      i18n.init();
      alert.restrictTo('ja,ru');

      expect(alert.languages()).toEqual([]);
    });
  });
});
