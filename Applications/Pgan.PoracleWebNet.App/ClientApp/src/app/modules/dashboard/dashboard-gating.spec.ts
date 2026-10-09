import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { DashboardComponent } from './dashboard.component';
import { DashboardCounts } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { AuthService } from '../../core/services/auth.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { LocationService } from '../../core/services/location.service';
import { ProfileService } from '../../core/services/profile.service';
import { SettingsService } from '../../core/services/settings.service';

const NO_ALARMS: DashboardCounts = {
  raids: 0,
  eggs: 0,
  fortChanges: 0,
  gyms: 0,
  invasions: 0,
  lures: 0,
  maxBattles: 0,
  nests: 0,
  pokemon: 0,
  pokestopEvents: 0,
  quests: 0,
};

/**
 * The dashboard's quick actions and tips predate the `disable_*` switches and linked to whatever they
 * liked: an Add Pokemon button on an instance without Pokemon alarms, a "Set up areas" tip with Areas
 * switched off, and an address lookup that 403s with `disable_nominatim` on. Each now follows the same
 * key the sidebar item carries.
 */
describe('DashboardComponent feature gating', () => {
  const setup = (disabled: string[], opts: { location?: { latitude: number; longitude: number } } = {}) => {
    localStorage.setItem('poracle-onboarding-complete', 'true');
    sessionStorage.removeItem('dismissed-tips');
    const settings = signal<Record<string, string>>(Object.fromEntries(disabled.map(k => [k, 'true'])));
    const location = {
      getLocation: jest.fn(() => of(opts.location ?? { latitude: 0, longitude: 0 })),
      getStaticMapUrl: jest.fn(() => of(null)),
      getWeather: jest.fn(() => of(null)),
      reverseGeocode: jest.fn(() => of({ display_name: '1 Main St' })),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTranslateService(),
        {
          provide: SettingsService,
          useValue: {
            isDisabled: (key: string) => settings()[key]?.toLowerCase() === 'true',
            isForcedByPoracle: () => false,
            siteSettings: settings,
          },
        },
        { provide: DashboardService, useValue: { getCounts: () => of(NO_ALARMS) } },
        {
          provide: AuthService,
          useValue: {
            isAdmin: () => false,
            profileResynced: () => false,
            user: signal({ username: 'someone', enabled: true, profileNo: 1 }),
          },
        },
        { provide: AreaService, useValue: { getGeofencePolygons: () => of([]), getSelected: () => of([]) } },
        { provide: ProfileService, useValue: { getAll: () => of([]) } },
        { provide: LocationService, useValue: location },
      ],
    });

    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return {
      actions: () =>
        [...el.querySelectorAll('.quick-actions .action-btn')].map(b =>
          [...b.classList].find(c => c.startsWith('action-') && c !== 'action-btn'),
        ),
      component: fixture.componentInstance,
      location,
      tips: () => fixture.componentInstance.tips().map(t => t.id),
    };
  };

  afterEach(() => localStorage.removeItem('poracle-onboarding-complete'));

  it('offers every quick action and tip while nothing is disabled', () => {
    const { actions, tips } = setup([]);

    expect(actions()).toEqual(['action-pokemon', 'action-raids', 'action-quests', 'action-areas', 'action-cleaning']);
    expect(tips()).toEqual(['no-location', 'no-areas', 'no-alarms']);
  });

  it('drops the add button for each disabled alarm type', () => {
    expect(setup(['disable_mons']).actions()).not.toContain('action-pokemon');
    expect(setup(['disable_raids']).actions()).not.toContain('action-raids');
    expect(setup(['disable_quests']).actions()).not.toContain('action-quests');
  });

  it('drops Manage Areas and the areas tip when areas are disabled', () => {
    const { actions, tips } = setup(['disable_areas']);

    expect(actions()).not.toContain('action-areas');
    expect(tips()).not.toContain('no-areas');
  });

  it('drops the set-location tip when location is disabled', () => {
    expect(setup(['disable_location']).tips()).not.toContain('no-location');
  });

  /** The tip exists to get a first alarm made, so it points at the first type this instance offers. */
  it('points the no-alarms tip at an enabled type when Pokemon is disabled', () => {
    const { component } = setup(['disable_mons']);

    expect(component.tips().find(t => t.id === 'no-alarms')?.route).toBe('/raids');
  });

  it('drops the no-alarms tip when every alarm type is disabled', () => {
    const everything = (c: DashboardComponent) => c.cards.map(card => card.disableKey);
    const probe = setup([]);
    const { tips } = setup(everything(probe.component));

    expect(tips()).not.toContain('no-alarms');
  });

  it('reverse geocodes the pin while geocoding is enabled', () => {
    const { location } = setup([], { location: { latitude: 37.5, longitude: -77.4 } });

    expect(location.reverseGeocode).toHaveBeenCalledWith(37.5, -77.4);
  });

  it('does not reverse geocode the pin when geocoding is disabled', () => {
    const { location } = setup(['disable_nominatim'], { location: { latitude: 37.5, longitude: -77.4 } });

    expect(location.getLocation).toHaveBeenCalled();
    expect(location.reverseGeocode).not.toHaveBeenCalled();
  });
});
