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

      expect(translateService.getDefaultLang()).toBe('en');
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
