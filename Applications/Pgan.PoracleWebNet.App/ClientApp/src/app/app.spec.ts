import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { App } from './app';
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
 * Covers the nav-filter logic that #236 hardened: `disable_*` settings hide nav items
 * for everyone (including admins). Without these tests the iteration-1 fix
 * ("admins shouldn't bypass the disable filter in the nav") is silently regressable.
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

  it.each([
    ['disable_mons', '/pokemon'],
    ['disable_raids', '/raids'],
    ['disable_quests', '/quests'],
    ['disable_invasions', '/invasions'],
    ['disable_lures', '/lures'],
    ['disable_nests', '/nests'],
    ['disable_gyms', '/gyms'],
    ['disable_maxbattles', '/max-battles'],
    ['disable_fort_changes', '/fort-changes'],
  ])('hides %s route from non-admin nav', (key, route) => {
    const app = setup({ [key]: 'true' }, false);
    expect(alarmRoutes(app)).not.toContain(route);
  });

  it.each([
    ['disable_mons', '/pokemon'],
    ['disable_raids', '/raids'],
    ['disable_quests', '/quests'],
    ['disable_invasions', '/invasions'],
    ['disable_lures', '/lures'],
    ['disable_nests', '/nests'],
    ['disable_gyms', '/gyms'],
    ['disable_maxbattles', '/max-battles'],
    ['disable_fort_changes', '/fort-changes'],
  ])('hides %s route from ADMIN nav too (no admin bypass)', (key, route) => {
    // The original #236 bug was a UI/API mismatch — leaving the nav visible to admins
    // while the API rejects them recreates the same defect class in miniature.
    const app = setup({ [key]: 'true' }, true);
    expect(alarmRoutes(app)).not.toContain(route);
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
    // Matches SettingsService.isDisabled — case-insensitive check.
    const app = setup({ disable_mons: 'True' }, false);
    expect(alarmRoutes(app)).not.toContain('/pokemon');
  });

  it('treats setting value "false" as enabled', () => {
    const app = setup({ disable_mons: 'false' }, false);
    expect(alarmRoutes(app)).toContain('/pokemon');
  });
});
