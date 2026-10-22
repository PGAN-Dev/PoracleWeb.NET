import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { Observable, Subject, of } from 'rxjs';

import { DashboardComponent } from './dashboard.component';
import { DashboardCounts, Profile, WeatherData } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { AuthService } from '../../core/services/auth.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { I18nService } from '../../core/services/i18n.service';
import { LocationService } from '../../core/services/location.service';
import { ProfileService } from '../../core/services/profile.service';
import { SettingsService } from '../../core/services/settings.service';
import { ADD_QUERY_PARAM } from '../../shared/utils/add-on-arrival';

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

/** The dashboard defects from the UI audit: resync wording, the 0,0 pin, the weather card, keyboard reach. */
describe('DashboardComponent shell', () => {
  afterEach(() => jest.restoreAllMocks());

  interface Options {
    location?: { latitude: number; longitude: number };
    profiles?: Partial<Profile>[];
    resynced?: boolean;
    user?: Record<string, unknown>;
    weather?: WeatherData | null | Observable<WeatherData | null>;
  }

  const setup = (opts: Options = {}) => {
    localStorage.setItem('poracle-onboarding-complete', 'true');
    sessionStorage.removeItem('dismissed-tips');
    const resynced = signal(opts.resynced ?? false);
    const auth = {
      clearProfileResynced: jest.fn(() => resynced.set(false)),
      isAdmin: () => false,
      profileResynced: resynced,
      user: signal({ username: 'someone', enabled: true, profileName: null, profileNo: 1, ...opts.user }),
    };
    const location = {
      getLocation: jest.fn(() => of(opts.location ?? { latitude: 0, longitude: 0 })),
      getStaticMapUrl: jest.fn(() => of(null)),
      getWeather: jest.fn(() => (opts.weather instanceof Observable ? opts.weather : of(opts.weather ?? null))),
      reverseGeocode: jest.fn(() => of(null)),
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
          useValue: { isDisabled: () => false, isForcedByPoracle: () => false, siteSettings: signal({}) },
        },
        { provide: DashboardService, useValue: { getCounts: () => of(NO_ALARMS) } },
        { provide: AuthService, useValue: auth },
        { provide: AreaService, useValue: { getGeofencePolygons: () => of([]), getSelected: () => of([]) } },
        { provide: ProfileService, useValue: { getAll: () => of(opts.profiles ?? []) } },
        { provide: LocationService, useValue: location },
      ],
    });

    // The component imports MatSnackBarModule, which provides its own instance; spy on all of them.
    const snackBar = { open: jest.spyOn(MatSnackBar.prototype, 'open').mockReturnValue(undefined as never) };
    const instant = jest.spyOn(TestBed.inject(I18nService), 'instant');
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { auth, el, fixture, instant, resynced, snackBar };
  };

  describe('profile resync (flows D1)', () => {
    // The profile list is still loading when the dashboard opens; the old code read the name from it.
    it('names the profile the server switched to before the profile list has loaded', () => {
      const { instant, snackBar } = setup({
        profiles: [],
        resynced: true,
        user: { profileName: 'Weekend Raids', profileNo: 2 },
      });

      expect(instant).toHaveBeenCalledWith('DASHBOARD.PROFILE_RESYNCED', { name: 'Weekend Raids' });
      expect(snackBar.open).toHaveBeenCalledTimes(1);
    });

    it('waits for the refreshed user when the resync lands after the dashboard opened', () => {
      const { auth, fixture, instant, resynced, snackBar } = setup();
      expect(snackBar.open).not.toHaveBeenCalled();

      auth.user.set({ ...auth.user(), profileName: 'Weekend Raids', profileNo: 2 });
      resynced.set(true);
      fixture.detectChanges();

      expect(instant).toHaveBeenCalledWith('DASHBOARD.PROFILE_RESYNCED', { name: 'Weekend Raids' });
      expect(snackBar.open).toHaveBeenCalledTimes(1);
      expect(auth.clearProfileResynced).toHaveBeenCalled();
    });

    it('says nothing when no resync happened', () => {
      const { snackBar } = setup();

      expect(snackBar.open).not.toHaveBeenCalled();
    });
  });

  describe('location card (flows D14)', () => {
    it('reads as not set on a 0,0 profile', () => {
      const { el } = setup({ location: { latitude: 0, longitude: 0 } });
      const card = el.querySelector('.location-card-full')!;

      expect(card.textContent).toContain('DASHBOARD.LOCATION_NOT_SET');
      expect(card.textContent).not.toContain('0.0000');
    });

    it('still shows coordinates for a real pin', () => {
      const { el } = setup({ location: { latitude: 41.6539, longitude: -83.5379 } });

      expect(el.querySelector('.location-card-full')!.textContent).toContain('41.6539, -83.5379');
    });
  });

  describe('weather card (U9)', () => {
    // A skeleton that then vanished on "no weather" made the status row jump; nothing is drawn until
    // there is weather to draw.
    it('reserves no slot while the weather is loading', () => {
      const pending = new Subject<WeatherData | null>();
      const { el, fixture } = setup({ location: { latitude: 41.6539, longitude: -83.5379 }, weather: pending });

      expect(el.querySelector('.weather-card')).toBeNull();

      pending.next({ boostedTypes: [], conditionName: 'Clear', hasWarning: false, icon: 'wb_sunny' } as unknown as WeatherData);
      fixture.detectChanges();

      expect(el.querySelector('.weather-card')?.textContent).toContain('Clear');
    });

    it('is absent when the server has no weather for the pin', () => {
      const { el } = setup({ location: { latitude: 41.6539, longitude: -83.5379 }, weather: null });

      expect(el.querySelector('.weather-card')).toBeNull();
    });

    it('is shown when there is weather', () => {
      const weather = { boostedTypes: [], conditionName: 'Clear', hasWarning: false, icon: 'wb_sunny' } as unknown as WeatherData;
      const { el } = setup({ location: { latitude: 41.6539, longitude: -83.5379 }, weather });

      expect(el.querySelector('.weather-card')?.textContent).toContain('Clear');
    });
  });

  describe('keyboard reach (U12)', () => {
    it('makes every clickable status card a focusable button', () => {
      const { el } = setup({ location: { latitude: 41.6539, longitude: -83.5379 } });
      const cards = [...el.querySelectorAll<HTMLElement>('.status-card:not(.weather-card)')];

      expect(cards.length).toBeGreaterThan(0);
      for (const card of cards) {
        expect(card.getAttribute('role')).toBe('button');
        expect(card.getAttribute('tabindex')).toBe('0');
      }
    });

    it('opens the areas page on Enter and on Space', () => {
      const { el } = setup();
      const navigate = jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      const card = el.querySelector<HTMLElement>('.areas-card')!;

      card.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      card.dispatchEvent(new KeyboardEvent('keydown', { key: ' ' }));

      expect(navigate).toHaveBeenCalledTimes(2);
      expect(navigate).toHaveBeenCalledWith(['/areas']);
    });

    it('leaves the weather card out of the tab order, since it does nothing', () => {
      const weather = { boostedTypes: [], conditionName: 'Clear', hasWarning: false, icon: 'wb_sunny' } as unknown as WeatherData;
      const { el } = setup({ location: { latitude: 41.6539, longitude: -83.5379 }, weather });

      expect(el.querySelector('.weather-card')!.hasAttribute('tabindex')).toBe(false);
    });
  });

  describe('quick actions (U1)', () => {
    it('asks the list to open its add dialog for the three Add actions', () => {
      const { el } = setup();
      const navigate = jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      const buttons = [...el.querySelectorAll<HTMLButtonElement>('.quick-actions .action-btn')];

      buttons.forEach(b => b.click());

      expect(navigate).toHaveBeenCalledWith(['/pokemon'], { queryParams: { [ADD_QUERY_PARAM]: 1 } });
      expect(navigate).toHaveBeenCalledWith(['/raids'], { queryParams: { [ADD_QUERY_PARAM]: 1 } });
      expect(navigate).toHaveBeenCalledWith(['/quests'], { queryParams: { [ADD_QUERY_PARAM]: 1 } });
    });

    it('leaves Manage Areas and Cleaning as plain navigation', () => {
      const { el } = setup();
      const navigate = jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      el.querySelector<HTMLButtonElement>('.quick-actions .action-areas')!.click();
      el.querySelector<HTMLButtonElement>('.quick-actions .action-cleaning')!.click();

      expect(navigate).toHaveBeenCalledWith(['/areas']);
      expect(navigate).toHaveBeenCalledWith(['/cleaning']);
    });
  });
});
