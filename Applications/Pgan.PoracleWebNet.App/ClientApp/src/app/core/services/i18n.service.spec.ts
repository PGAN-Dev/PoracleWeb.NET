import { TestBed } from '@angular/core/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';

import { I18nService } from './i18n.service';

describe('I18nService', () => {
  let service: I18nService;
  let translateService: TranslateService;

  beforeEach(() => {
    jest.spyOn(Storage.prototype, 'getItem').mockReturnValue(null);
    jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {});

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService()],
    });

    service = TestBed.inject(I18nService);
    translateService = TestBed.inject(TranslateService);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('init()', () => {
    it('should set default language to en', () => {
      service.init();

      expect(translateService.getFallbackLang()).toBe('en');
    });

    it('should use browser detection when no stored language exists', () => {
      const languagesGetter = jest.spyOn(navigator, 'languages', 'get').mockReturnValue(['de-DE', 'de']);

      service.init();

      expect(service.currentLang()).toBe('de');
      languagesGetter.mockRestore();
    });

    it('should use stored language from localStorage', () => {
      (Storage.prototype.getItem as jest.Mock).mockReturnValue('fr');

      service.init();

      expect(service.currentLang()).toBe('fr');
    });

    it('should filter availableLanguages when allowedLanguages is provided', () => {
      service.init('de,fr');

      const codes = service.availableLanguages().map(l => l.code);
      expect(codes).toContain('en');
      expect(codes).toContain('de');
      expect(codes).toContain('fr');
      expect(codes).not.toContain('es');
      expect(codes).not.toContain('it');
    });

    it('should always include en in availableLanguages even when not in allowed list', () => {
      service.init('de,fr');

      const codes = service.availableLanguages().map(l => l.code);
      expect(codes).toContain('en');
    });
  });

  describe("init() with Poracle's locale", () => {
    /** Browser languages the UI ships nothing for, so detection returns null and the locale gets a say. */
    const unplaceableBrowser = (): jest.SpyInstance => jest.spyOn(navigator, 'languages', 'get').mockReturnValue(['ja-JP', 'ja']);

    it('should use the server locale when there is no stored choice and no browser match', () => {
      unplaceableBrowser();

      service.init(undefined, 'de');

      expect(service.currentLang()).toBe('de');
    });

    it('should keep a stored choice ahead of the server locale', () => {
      unplaceableBrowser();
      (Storage.prototype.getItem as jest.Mock).mockReturnValue('fr');

      service.init(undefined, 'de');

      expect(service.currentLang()).toBe('fr');
    });

    it('should keep a browser match ahead of the server locale', () => {
      jest.spyOn(navigator, 'languages', 'get').mockReturnValue(['it-IT', 'it']);

      service.init(undefined, 'de');

      expect(service.currentLang()).toBe('it');
    });

    it('should fall back to en for a locale this UI does not ship', () => {
      unplaceableBrowser();

      service.init(undefined, 'zh-cn');

      expect(service.currentLang()).toBe('en');
      expect(service.serverDefaultLanguage()).toBeNull();
    });

    it('should fall back to en for a locale the admin excluded from allowed_languages', () => {
      unplaceableBrowser();

      service.init('en,fr', 'de');

      expect(service.currentLang()).toBe('en');
      expect(service.serverDefaultLanguage()).toBeNull();
    });

    it('should match a regional server locale to its base language', () => {
      unplaceableBrowser();

      service.init(undefined, 'de-AT');

      expect(service.currentLang()).toBe('de');
    });

    it('should ignore an empty or absent server locale', () => {
      unplaceableBrowser();

      service.init(undefined, '');

      expect(service.currentLang()).toBe('en');
    });

    it('should adopt a server locale that arrives after the first init', () => {
      unplaceableBrowser();

      service.init();
      expect(service.currentLang()).toBe('en');

      service.init(undefined, 'de');

      expect(service.currentLang()).toBe('de');
    });

    it('should not overwrite a browser match when the server locale arrives late', () => {
      jest.spyOn(navigator, 'languages', 'get').mockReturnValue(['it-IT', 'it']);

      service.init();
      service.init(undefined, 'de');

      expect(service.currentLang()).toBe('it');
    });

    it('should not persist an auto-picked language, so a later visit can re-decide', () => {
      unplaceableBrowser();

      service.init();

      expect(service.currentLang()).toBe('en');
      expect(localStorage.setItem).not.toHaveBeenCalledWith('poracle-ui-language', expect.anything());
    });

    it('should not overwrite a stored choice when the server locale arrives late', () => {
      unplaceableBrowser();
      (Storage.prototype.getItem as jest.Mock).mockReturnValue('fr');

      service.init();
      service.init(undefined, 'de');

      expect(service.currentLang()).toBe('fr');
    });
  });

  describe('use()', () => {
    beforeEach(() => {
      service.init();
    });

    it('should change currentLang signal', () => {
      service.use('de');

      expect(service.currentLang()).toBe('de');
    });

    it('should store language in localStorage', () => {
      service.use('es');

      expect(localStorage.setItem).toHaveBeenCalledWith('poracle-ui-language', 'es');
    });

    it('should fall back to en with invalid code', () => {
      service.use('xx');

      expect(service.currentLang()).toBe('en');
    });
  });

  describe('instant()', () => {
    it('should delegate to TranslateService', () => {
      service.init();
      const spy = jest.spyOn(translateService, 'instant').mockReturnValue('Hello');

      const result = service.instant('GREETING', { name: 'World' });

      expect(spy).toHaveBeenCalledWith('GREETING', { name: 'World' });
      expect(result).toBe('Hello');
    });
  });

  describe('availableLanguages', () => {
    it('should return all languages when no allowedLanguages filter is set', () => {
      service.init();

      expect(service.availableLanguages().length).toBe(service.allLanguages.length);
    });
  });
});
