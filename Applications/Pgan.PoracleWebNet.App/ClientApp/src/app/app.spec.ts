import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { App } from './app';
import { AlertLanguageService } from './core/services/alert-language.service';
import { AuthService } from './core/services/auth.service';
import { DashboardService } from './core/services/dashboard.service';
import { I18nService } from './core/services/i18n.service';
import { SettingsService } from './core/services/settings.service';

interface NavItemShape {
  adminOnly?: boolean;
  disableKey?: string;
  route: string;
}

/**
 * Covers the nav-filter logic that #236 hardened: a `disable_*` setting takes effect for everyone,
 * admins included. Without these tests the iteration-1 fix ("admins shouldn't bypass the disable
 * filter in the nav") is silently regressable.
 *
 * What "takes effect" means differs by group, and deliberately. A disabled **settings** item is
 * hidden outright. A disabled **alarm type** keeps its nav item: the page still lists the rules a
 * user already has and still deletes them, and hiding the item would strand people with alarms they
 * can neither see nor remove — which matters most when the type was disabled in Poracle, whose bot
 * refuses the matching command too. Creating and editing are refused by the API either way.
 */
describe('App nav filtering (#236)', () => {
  let settingsSignal: WritableSignal<Record<string, string>>;
  let isAdminSignal: WritableSignal<boolean>;

  const setup = (siteSettings: Record<string, string>, isAdmin: boolean) => {
    settingsSignal = signal(siteSettings);
    isAdminSignal = signal(isAdmin);

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
          useValue: {
            isDisabled: (key: string) => settingsSignal()[key]?.toLowerCase() === 'true',
            loadOnce: () => of([]),
            siteSettings: settingsSignal,
          },
        },
        {
          provide: AuthService,
          useValue: {
            hasManagedWebhooks: () => false,
            isAdmin: () => isAdminSignal(),
            loadCurrentUser: jest.fn(),
            logout: jest.fn(),
            stopImpersonating: jest.fn(),
            toggleAlerts: () => of(null),
          },
        },
        {
          provide: DashboardService,
          useValue: { getCounts: () => of({}) },
        },
        {
          provide: I18nService,
          useValue: { init: jest.fn() },
        },
      ],
    });

    return TestBed.runInInjectionContext(() => {
      // Construct via the injector so inject() works inside the component's class fields.
      // We don't render the template — the computed signals are accessible directly.
      return new App();
    });
  };

  // Cast to any to read protected computed signals — TypeScript's `protected` is compile-only.
  const alarmRoutes = (app: App): string[] => (app as unknown as { alarmNavItems: () => NavItemShape[] }).alarmNavItems().map(i => i.route);
  const settingsRoutes = (app: App): string[] =>
    (app as unknown as { settingsNavItems: () => NavItemShape[] }).settingsNavItems().map(i => i.route);

  it('shows all alarm routes when nothing is disabled (non-admin)', () => {
    const app = setup({}, false);
    expect(alarmRoutes(app)).toEqual(
      expect.arrayContaining(['/dashboard', '/quick-picks', '/pokemon', '/raids', '/quests', '/invasions', '/lures', '/nests', '/gyms']),
    );
  });

  const ALARM_KEYS: [string, string][] = [
    ['disable_mons', '/pokemon'],
    ['disable_raids', '/raids'],
    ['disable_quests', '/quests'],
    ['disable_invasions', '/invasions'],
    ['disable_lures', '/lures'],
    ['disable_nests', '/nests'],
    ['disable_gyms', '/gyms'],
    ['disable_maxbattles', '/max-battles'],
    ['disable_fort_changes', '/fort-changes'],
  ];

  it.each(ALARM_KEYS)('keeps the %s route reachable so existing alarms can be viewed and deleted', (key, route) => {
    // Previously this hid the item. It now stays: the page is read-and-delete while disabled, and the
    // API refuses the creates and edits. Hiding it would leave alarms unreachable and undeletable.
    const app = setup({ [key]: 'true' }, false);
    expect(alarmRoutes(app)).toContain(route);
  });

  it.each(ALARM_KEYS)('keeps the %s route reachable for admins too (no divergence)', (key, route) => {
    const app = setup({ [key]: 'true' }, true);
    expect(alarmRoutes(app)).toContain(route);
  });

  it('still hides a disabled SETTINGS item from admins (no admin bypass)', () => {
    // The original #236 bug was a UI/API mismatch — leaving the nav visible to admins while the API
    // rejects them recreates the same defect class. Settings items are hidden outright, so this is
    // where that guarantee still lives.
    const app = setup({ disable_areas: 'true' }, true);
    expect(settingsRoutes(app)).not.toContain('/areas');
  });

  it('hides /profiles when disable_profiles is true (settings group)', () => {
    const app = setup({ disable_profiles: 'true' }, false);
    expect(settingsRoutes(app)).not.toContain('/profiles');
  });

  it('hides /areas when disable_areas is true (settings group)', () => {
    const app = setup({ disable_areas: 'true' }, false);
    expect(settingsRoutes(app)).not.toContain('/areas');
  });

  it('treats setting value "True" (capitalized) as disabled', () => {
    // Matches SettingsService.isDisabled — case-insensitive check. Asserted on a settings item,
    // since those are the ones still hidden outright.
    const app = setup({ disable_areas: 'True' }, false);
    expect(settingsRoutes(app)).not.toContain('/areas');
  });

  it('treats setting value "false" as enabled', () => {
    const app = setup({ disable_areas: 'false' }, false);
    expect(settingsRoutes(app)).toContain('/areas');
  });
});

/**
 * The bootstrap path, which #426 already broke once: a signed-out visitor must reach only the
 * anonymous settings endpoint. `/api/config` is `[Authorize]`, so sourcing Poracle's locale from
 * there would 401 on every visit to the login page. The locale rides on the public settings call
 * instead, and these tests pin that it does.
 */
describe('App bootstrap language defaults (#770)', () => {
  const setup = (opts: { authenticated: boolean; settings: Record<string, string> }) => {
    const loadOnce = jest.fn(() => of([]));
    const loadPublic = jest.fn(() => of([]));
    const init = jest.fn();
    const alertLanguage = { languages: [], load: jest.fn(), selected: signal('en') };

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
          useValue: {
            isDisabled: () => false,
            loadOnce,
            loadPublic,
            siteSettings: signal(opts.settings),
          },
        },
        {
          provide: AuthService,
          useValue: {
            getProviders: () => of({}),
            hasManagedWebhooks: () => false,
            isAdmin: () => false,
            isAuthenticated: () => opts.authenticated,
            loadCurrentUser: jest.fn(),
            logout: jest.fn(),
            stopImpersonating: jest.fn(),
            toggleAlerts: () => of(null),
          },
        },
        { provide: AlertLanguageService, useValue: alertLanguage },
        { provide: DashboardService, useValue: { getCounts: () => of({}) } },
        { provide: I18nService, useValue: { init } },
      ],
    });

    const app = TestBed.runInInjectionContext(() => new App());
    app.ngOnInit();
    return { alertLanguage, app, init, loadOnce, loadPublic };
  };

  it('uses only the anonymous settings endpoint when signed out', () => {
    const { loadOnce, loadPublic } = setup({ authenticated: false, settings: {} });

    expect(loadPublic).toHaveBeenCalled();
    expect(loadOnce).not.toHaveBeenCalled();
  });

  it('forwards the Poracle locale from the anonymous response to i18n', () => {
    const { init } = setup({ authenticated: false, settings: { allowed_languages: 'en,de', poracle_locale: 'de' } });

    expect(init).toHaveBeenLastCalledWith('en,de', 'de');
  });

  it('still uses the authenticated endpoint when signed in', () => {
    const { loadOnce, loadPublic } = setup({ authenticated: true, settings: { poracle_locale: 'de' } });

    expect(loadOnce).toHaveBeenCalled();
    expect(loadPublic).not.toHaveBeenCalled();
  });

  it('passes undefined rather than failing when Poracle reports no locale', () => {
    const { init } = setup({ authenticated: false, settings: {} });

    expect(init).toHaveBeenLastCalledWith(undefined, undefined);
  });

  it('does not reconcile the alert language while signed out (#775)', () => {
    // GET /api/location/language is [Authorize]. Calling it here guaranteed a 401 on every login-page
    // visit; LocationService swallowing the error is what kept it invisible.
    const { alertLanguage } = setup({ authenticated: false, settings: {} });

    expect(alertLanguage.load).not.toHaveBeenCalled();
  });

  it('reconciles the alert language when signed in', () => {
    const { alertLanguage } = setup({ authenticated: true, settings: {} });

    expect(alertLanguage.load).toHaveBeenCalled();
  });
});
