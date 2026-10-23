import * as fs from 'fs';
import * as path from 'path';

import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { config, of, throwError } from 'rxjs';

import { App } from './app';
import { AlertLanguageService } from './core/services/alert-language.service';
import { AuthService } from './core/services/auth.service';
import { DashboardService } from './core/services/dashboard.service';
import { I18nService } from './core/services/i18n.service';
import { SettingsService } from './core/services/settings.service';

/**
 * Landmarks and the keyboard way in (UI audit cross D9). The shell is too heavy to render here, so
 * this reads the template, as the disabled-banner tests do.
 */
describe('App shell landmarks', () => {
  const template = fs.readFileSync(path.join(__dirname, 'app.html'), 'utf8');
  const index = fs.readFileSync(path.join(__dirname, '..', 'index.html'), 'utf8');

  it('puts the toolbar inside a header landmark', () => {
    expect(template).toMatch(/<header[^>]*>\s*<mat-toolbar/);
  });

  it('offers a skip link to the main content before the toolbar', () => {
    const skip = template.indexOf('class="skip-link"');

    expect(skip).toBeGreaterThan(-1);
    expect(skip).toBeLessThan(template.indexOf('<mat-toolbar'));
    expect(template).toContain("'TOOLBAR.SKIP_TO_CONTENT' | translate");
    expect(template).toMatch(/<main id="main-content" tabindex="-1"/);
  });

  it('labels the keyboard-shortcut overlay as a dialog', () => {
    const overlay = template.slice(template.indexOf('class="shortcut-card"') - 200);

    expect(overlay).toMatch(/role="dialog"/);
    expect(overlay).toMatch(/aria-modal="true"/);
    expect(overlay).toMatch(/aria-labelledby="shortcut-help-title"/);
    expect(overlay).toMatch(/id="shortcut-help-title"/);
  });

  it('names the product before the app has booted, not one deployment', () => {
    expect(index).toContain('<title>PoracleWeb.NET</title>');
  });
});

/**
 * A 429 on the shell's own bootstrap requests logged an uncaught `ERROR` (cross audit, rate-limit note).
 * A failed provider read only means "Sign out everywhere" is not offered.
 */
describe('App bootstrap under rate limiting', () => {
  let unhandled: jest.Mock;
  const previous = config.onUnhandledError;

  beforeEach(() => {
    unhandled = jest.fn();
    config.onUnhandledError = unhandled;
  });
  afterEach(() => (config.onUnhandledError = previous));

  it('swallows a refused provider read', async () => {
    const tooMany = new HttpErrorResponse({ status: 429, url: '/api/auth/providers' });
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([]),
        provideTranslateService(),
        {
          provide: SettingsService,
          useValue: { isDisabled: () => false, loadOnce: () => of([]), loadPublic: () => of([]), siteSettings: signal({}) },
        },
        {
          provide: AuthService,
          useValue: {
            getProviders: () => throwError(() => tooMany),
            hasManagedWebhooks: () => false,
            isAdmin: () => false,
            isAuthenticated: () => true,
          },
        },
        {
          provide: AlertLanguageService,
          useValue: { languages: signal([]), load: jest.fn(), restrictTo: jest.fn(), selected: signal('en') },
        },
        { provide: DashboardService, useValue: { getCounts: () => of({}) } },
        { provide: I18nService, useValue: { init: jest.fn() } },
      ],
    });

    const app = TestBed.runInInjectionContext(() => new App());
    app.ngOnInit();
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(unhandled).not.toHaveBeenCalled();
  });
});
