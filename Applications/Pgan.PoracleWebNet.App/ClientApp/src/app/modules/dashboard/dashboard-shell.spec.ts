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

/** The dashboard defects from the UI audit: resync wording, the 0,0 pin, the weather, keyboard reach. */
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

  describe('pin (flows D14)', () => {
    it('asks for a pin on a 0,0 profile instead of printing 0.0000', () => {
      const { el } = setup({ location: { latitude: 0, longitude: 0 } });
      const pin = el.querySelector('.strip-pin')!;

      expect(pin.textContent).toContain('DASHBOARD.LOCATION_NOT_SET');
      expect(pin.textContent).not.toContain('0.0000');
    });

    it('still shows coordinates for a real pin', () => {
      const { el } = setup({ location: { latitude: 41.6539, longitude: -83.5379 } });

      expect(el.querySelector('.strip-pin')!.textContent).toContain('41.6539, -83.5379');
    });
  });

  describe('weather (U9)', () => {
    // A skeleton that then vanished on "no weather" made the status row jump; nothing is drawn until
    // there is weather to draw.
    it('reserves no slot while the weather is loading', () => {
      const pending = new Subject<WeatherData | null>();
      const { el, fixture } = setup({ location: { latitude: 41.6539, longitude: -83.5379 }, weather: pending });

      expect(el.querySelector('.strip-weather')).toBeNull();

      pending.next({ boostedTypes: [], conditionName: 'Clear', hasWarning: false, icon: 'wb_sunny' } as unknown as WeatherData);
      fixture.detectChanges();

      expect(el.querySelector('.strip-weather')?.textContent).toContain('Clear');
    });

    it('is absent when the server has no weather for the pin', () => {
      const { el } = setup({ location: { latitude: 41.6539, longitude: -83.5379 }, weather: null });

      expect(el.querySelector('.strip-weather')).toBeNull();
    });

    it('is shown when there is weather', () => {
      const weather = { boostedTypes: [], conditionName: 'Clear', hasWarning: false, icon: 'wb_sunny' } as unknown as WeatherData;
      const { el } = setup({ location: { latitude: 41.6539, longitude: -83.5379 }, weather });

      expect(el.querySelector('.strip-weather')?.textContent).toContain('Clear');
    });
  });

  describe('keyboard reach (U12)', () => {
    // Native buttons take Enter and Space themselves; the old role="button" cards had to wire both.
    it('makes every strip item that does something a native button', () => {
      const { el } = setup({ location: { latitude: 41.6539, longitude: -83.5379 } });
      const items = [...el.querySelectorAll<HTMLElement>('.status-strip .strip-item:not(.strip-alerts):not(.strip-weather)')];

      expect(items.length).toBeGreaterThanOrEqual(3);
      for (const item of items) {
        expect(item.tagName).toBe('BUTTON');
      }
    });

    it('opens the areas page from the areas item', () => {
      const { el } = setup();
      const navigate = jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      el.querySelector<HTMLButtonElement>('.strip-areas')!.click();

      expect(navigate).toHaveBeenCalledWith(['/areas']);
    });

    it('leaves the weather and the alert state out of the tab order, since they do nothing', () => {
      const weather = { boostedTypes: [], conditionName: 'Clear', hasWarning: false, icon: 'wb_sunny' } as unknown as WeatherData;
      const { el } = setup({ location: { latitude: 41.6539, longitude: -83.5379 }, weather });

      for (const selector of ['.strip-weather', '.strip-alerts']) {
        const item = el.querySelector(selector)!;
        expect(item.tagName).toBe('SPAN');
        expect(item.hasAttribute('tabindex')).toBe(false);
      }
    });
  });

  describe('new alarm (U1)', () => {
    it('asks the list to open its add dialog', () => {
      const { fixture } = setup();
      const navigate = jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      const component = fixture.componentInstance;

      for (const key of ['pokemon', 'raids', 'quests'] as const) {
        component.startNewAlarm(component.cards.find(c => c.key === key)!);
      }

      expect(navigate).toHaveBeenCalledWith(['/pokemon'], { queryParams: { [ADD_QUERY_PARAM]: 1 } });
      expect(navigate).toHaveBeenCalledWith(['/raids'], { queryParams: { [ADD_QUERY_PARAM]: 1 } });
      expect(navigate).toHaveBeenCalledWith(['/quests'], { queryParams: { [ADD_QUERY_PARAM]: 1 } });
    });

    it('leaves Cleaning as plain navigation', () => {
      const { el } = setup();
      const navigate = jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      el.querySelector<HTMLButtonElement>('.action-cleaning')!.click();

      expect(navigate).toHaveBeenCalledWith(['/cleaning']);
    });
  });
});
