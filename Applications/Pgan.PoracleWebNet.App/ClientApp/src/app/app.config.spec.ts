import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';

import { appConfig } from './app.config';

/**
 * Guards the NG0200 regression introduced by the ngx-translate v18 upgrade.
 *
 * v18 loads `fallbackLang` eagerly from inside the TranslateService constructor. Setting it in
 * `provideTranslateService()` therefore resolves TranslateLoader while the injector is still
 * building TranslateService, which fails with NG0200 (circular dependency) and leaves the
 * fallback language — English — rendering raw keys, while every other locale loaded fine.
 *
 * These tests exercise the real `appConfig` providers, so re-adding `fallbackLang` there fails
 * here rather than silently in production.
 */
describe('appConfig translation wiring', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
  });

  it('constructs TranslateService without a circular dependency', () => {
    expect(() => TestBed.inject(TranslateService)).not.toThrow();
  });

  it('does not fetch any translation before a language is requested', () => {
    TestBed.inject(TranslateService);
    // An eager fallback load would already have issued a request here.
    TestBed.inject(HttpTestingController).verify();
  });

  it('loads English through the configured HTTP loader once requested', () => {
    const translate = TestBed.inject(TranslateService);
    const http = TestBed.inject(HttpTestingController);

    translate.use('en');
    http.expectOne('./assets/i18n/en.json').flush({ NAV: { DASHBOARD: 'Dashboard' } });

    expect(translate.instant('NAV.DASHBOARD')).toBe('Dashboard');
    http.verify();
  });
});
