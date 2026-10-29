import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { DashboardComponent } from './dashboard.component';
import { DashboardCounts, GeofenceData, Profile, WeatherData } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { AuthService } from '../../core/services/auth.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { I18nService } from '../../core/services/i18n.service';
import { LocationService } from '../../core/services/location.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { Mute, MuteService } from '../../core/services/mute.service';
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

/**
 * The dashboard answers "what will reach me right now": one status strip (profile, alerts on or
 * paused, anything quiet, the pin, the areas), the alarm types that have alarms as one row, and the
 * rest folded into a single "Not tracking" line. The first screen on a phone is the strip and a
 * "New alarm" button, not four equal cards and a wall of greyed zeros.
 */
describe('DashboardComponent status strip and alarm summary', () => {
  interface Options {
    areas?: string[];
    /** Receives the area weather request, to see which point stands for each area. */
    areaWeather?: jest.Mock;
    counts?: Partial<DashboardCounts>;
    disabled?: string[];
    enabled?: boolean;
    geofences?: GeofenceData[];
    location?: { latitude: number; longitude: number };
    mapUrl?: string;
    mutes?: Mute[];
    profiles?: Partial<Profile>[];
    typeLabels?: Record<string, string>;
    weather?: WeatherData;
  }

  const setup = (opts: Options = {}) => {
    localStorage.setItem('poracle-onboarding-complete', 'true');
    sessionStorage.removeItem('dismissed-tips');
    const disabled = new Set(opts.disabled ?? []);
    const mutes = signal<Mute[]>(opts.mutes ?? []);
    const muteService = {
      countdownLabel: (secs: number) => `${Math.floor(secs / 60)}m`,
      hasMutes: computed(() => mutes().length > 0),
      mutes,
      refresh: jest.fn(),
      remainingFor: (scope: string, value: string | null) => {
        const m = mutes().find(x => x.scope === scope && x.value?.toLowerCase() === value?.toLowerCase());
        return m ? m.remainingSecs : null;
      },
      soonestExpiry: computed(() => (mutes().length ? Math.min(...mutes().map(m => m.expiresAt)) : null)),
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
          useValue: { isDisabled: (key: string) => disabled.has(key), isForcedByPoracle: () => false, siteSettings: signal({}) },
        },
        { provide: DashboardService, useValue: { getCounts: () => of({ ...NO_ALARMS, ...opts.counts }) } },
        {
          provide: AuthService,
          useValue: {
            isAdmin: () => false,
            profileResynced: () => false,
            user: signal({ username: 'someone', enabled: opts.enabled ?? true, profileName: null, profileNo: 1 }),
          },
        },
        {
          provide: AreaService,
          useValue: { getGeofencePolygons: () => of(opts.geofences ?? []), getSelected: () => of(opts.areas ?? []) },
        },
        { provide: ProfileService, useValue: { getAll: () => of(opts.profiles ?? []) } },
        {
          provide: LocationService,
          useValue: {
            getAreaWeather: opts.areaWeather ?? (() => of([])),
            getLocation: () => of(opts.location ?? { latitude: 0, longitude: 0 }),
            getStaticMapUrl: () => of(opts.mapUrl ? { url: opts.mapUrl } : null),
            getWeather: () => of(opts.weather ?? null),
            reverseGeocode: () => of(null),
          },
        },
        { provide: MuteService, useValue: muteService },
        { provide: MasterDataService, useValue: { getTypeLabel: (type: string) => opts.typeLabels?.[type] ?? type } },
      ],
    });

    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const navigate = jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    return { component: fixture.componentInstance, el, fixture, navigate };
  };

  afterEach(() => {
    jest.restoreAllMocks();
    document.querySelector('.cdk-overlay-container')?.replaceChildren();
  });

  describe('the strip', () => {
    it('is one labelled region holding the pin, the areas and the alert state', () => {
      const { el } = setup();
      const strip = el.querySelector('.status-strip')!;

      expect(strip.getAttribute('aria-label')).toBe('DASHBOARD.STATUS_LABEL');
      expect(strip.querySelector('.strip-pin')).not.toBeNull();
      expect(strip.querySelector('.strip-areas')).not.toBeNull();
      expect(strip.querySelector('.strip-alerts')).not.toBeNull();
      // The four equal cards are gone.
      expect(el.querySelector('.status-card')).toBeNull();
    });

    it('says alerts are on, or paused when the account is paused', () => {
      expect(setup().el.querySelector('.strip-alerts')!.textContent).toContain('DASHBOARD.ALERTS_ON');
      expect(setup({ enabled: false }).el.querySelector('.strip-alerts')!.textContent).toContain('DASHBOARD.ALERTS_PAUSED');
    });

    it('names the first two areas and counts the rest', () => {
      const { el } = setup({ areas: ['aberdeen', 'toledo', 'perrysburg', 'maumee'] });
      const text = el.querySelector('.strip-areas')!.textContent!;

      expect(text).toContain('aberdeen, toledo');
      expect(text).toContain('DASHBOARD.MORE');
    });

    it('asks for areas when none are chosen', () => {
      expect(setup().el.querySelector('.strip-areas')!.textContent).toContain('DASHBOARD.AREAS_CHOOSE');
    });

    it('shows the quiet summary only while something is quiet, and opens the list', () => {
      expect(setup().el.querySelector('.strip-quiet')).toBeNull();

      const now = Math.floor(Date.now() / 1000);
      const { component, el } = setup({ mutes: [{ expiresAt: now + 600, remainingSecs: 600, scope: 'area', value: 'Aberdeen' }] });
      const open = jest.spyOn(component, 'openQuietList').mockImplementation(() => undefined);
      const quiet = el.querySelector<HTMLButtonElement>('.strip-quiet')!;
      quiet.click();

      expect(quiet.tagName).toBe('BUTTON');
      expect(open).toHaveBeenCalled();
    });

    it('drops each item whose feature is switched off', () => {
      const { el } = setup({ disabled: ['disable_location', 'disable_areas', 'disable_profiles'] });

      expect(el.querySelector('.strip-pin')).toBeNull();
      expect(el.querySelector('.strip-areas')).toBeNull();
      expect(el.querySelector('.strip-profile')).toBeNull();
      // What is left still answers the question.
      expect(el.querySelector('.strip-alerts')).not.toBeNull();
    });

    it('offers the profile switcher only when there is more than one profile', () => {
      const one = setup({ profiles: [{ name: 'Default', active: true, profileNo: 1 }] });
      expect(one.el.querySelector('.strip-profile')!.getAttribute('aria-haspopup')).toBeNull();

      const two = setup({
        profiles: [
          { name: 'Default', active: true, profileNo: 1 },
          { name: 'Weekend', active: false, profileNo: 2 },
        ],
      });
      expect(two.el.querySelector('.strip-profile')!.getAttribute('aria-haspopup')).toBe('menu');
    });
  });

  describe('area chips', () => {
    // The coverage map is deferred until it scrolls into view; jsdom has no IntersectionObserver.
    beforeAll(() => {
      (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver = class {
        disconnect(): void {}
        observe(): void {}
        unobserve(): void {}
      };
    });

    afterAll(() => delete (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver);

    const fence = (name: string): GeofenceData =>
      ({
        name,
        path: [
          [0, 0],
          [0, 1],
          [1, 1],
        ],
      }) as GeofenceData;

    it('marks a quiet area on its chip', () => {
      const now = Math.floor(Date.now() / 1000);
      const { el } = setup({
        areas: ['aberdeen', 'toledo'],
        geofences: [fence('aberdeen'), fence('toledo')],
        mutes: [{ expiresAt: now + 600, remainingSecs: 600, scope: 'area', value: 'Aberdeen' }],
      });
      const chips = [...el.querySelectorAll('.area-chip')];

      expect(chips).toHaveLength(2);
      expect(chips[0].querySelector('.area-quiet')?.textContent).toContain('10m');
      expect(chips[1].querySelector('.area-quiet')).toBeNull();
    });

    // Stored names are lowercase because Poracle matches case-sensitively; the fence keeps its own casing,
    // which is what the Areas page shows.
    it('shows each area with its fence casing, as the Areas page does', () => {
      const { el } = setup({
        areas: ['aberdeen', 'toledo', 'perrysburg'],
        geofences: [fence('Aberdeen'), fence('Toledo'), fence('Perrysburg')],
      });

      expect([...el.querySelectorAll('.area-chip > span:first-child')].map(c => c.textContent!.trim())).toEqual([
        'Aberdeen',
        'Toledo',
        'Perrysburg',
      ]);
      expect(el.querySelector('.strip-areas')!.textContent).toContain('Aberdeen, Toledo');
    });

    it('keeps the stored name for an area with no fence to read the casing from', () => {
      const { el } = setup({ areas: ['my drawn fence'], geofences: [] });

      expect(el.querySelector('.area-chip')!.textContent).toContain('my drawn fence');
    });

    it('still finds a quiet area by its stored name when it is shown with the fence casing', () => {
      const now = Math.floor(Date.now() / 1000);
      const { el } = setup({
        areas: ['aberdeen'],
        geofences: [fence('Aberdeen')],
        mutes: [{ expiresAt: now + 600, remainingSecs: 600, scope: 'area', value: 'Aberdeen' }],
      });

      expect(el.querySelector('.area-quiet')?.textContent).toContain('10m');
    });

    // A mock that answered the geofence list in the wrong shape once hid the map on desktop; this pins
    // that polygons in the shape AreaService returns put the map on the page, linked to Areas.
    it('draws the coverage map, linked to the Areas page, once there are polygons', () => {
      const { el } = setup({ areas: ['aberdeen'], geofences: [fence('Aberdeen')] });
      const link = el.querySelector('a.coverage-map');

      expect(link?.getAttribute('href')).toBe('/areas');
    });

    it('falls back to the pin map, opening the pin dialog, when no areas are chosen', () => {
      const { component, el } = setup({ location: { latitude: 41.6, longitude: -83.5 }, mapUrl: 'https://maps.example/pin.png' });
      const open = jest.spyOn(component, 'openLocationDialog').mockImplementation(() => undefined);
      const pin = el.querySelector<HTMLButtonElement>('.coverage-pin')!;

      expect(el.querySelector('a.coverage-map')).toBeNull();
      expect(pin.querySelector('img')!.getAttribute('src')).toBe('https://maps.example/pin.png');
      pin.click();
      expect(open).toHaveBeenCalled();
    });
  });

  describe('area weather', () => {
    it('asks for the weather at the larger part of a multi-part area (#1117)', () => {
      const areaWeather = jest.fn().mockReturnValue(of([]));
      const small: [number, number][] = [
        [0, 0],
        [0, 0.1],
        [0.1, 0.1],
        [0.1, 0],
      ];
      const large: [number, number][] = [
        [40, -80],
        [40, -78],
        [42, -78],
        [42, -80],
      ];

      setup({ areas: ['islands'], areaWeather, geofences: [{ id: 1, name: 'Islands', multipath: [small, large] }] });

      expect(areaWeather).toHaveBeenCalledWith([{ name: 'islands', lat: 41, lon: -79 }]);
    });

    it('still asks for the weather at the centre of an ordinary area', () => {
      const areaWeather = jest.fn().mockReturnValue(of([]));
      const square: [number, number][] = [
        [40, -80],
        [40, -78],
        [42, -78],
        [42, -80],
      ];

      setup({ areas: ['aberdeen'], areaWeather, geofences: [{ id: 1, name: 'Aberdeen', path: square }] });

      expect(areaWeather).toHaveBeenCalledWith([{ name: 'aberdeen', lat: 41, lon: -79 }]);
    });
  });

  describe('area names', () => {
    it('shows the name the user gave their own drawn fence, not its stored key', () => {
      const { component } = setup({
        areas: ['lake eola loop 2', 'downtown orlando'],
        geofences: [
          { id: 7, name: 'lake eola loop 2', displayName: 'Lake Eola Loop', path: [] },
          { id: 1, name: 'Downtown Orlando', path: [] },
        ],
        location: { latitude: 41.6, longitude: -83.5 },
      });

      expect(component.areaLabel('lake eola loop 2')).toBe('Lake Eola Loop');
      expect(component.areaLabel('downtown orlando')).toBe('Downtown Orlando');
    });
  });

  describe('weather', () => {
    /** The boosted types come from the server in English; the chips show the display language. */
    it('names the boosted types in the display language', () => {
      const weather = {
        boostedTypes: ['Grass', 'Fire'],
        conditionName: 'Clear',
        hasWarning: false,
        icon: 'wb_sunny',
      } as unknown as WeatherData;
      const { el } = setup({ location: { latitude: 41.6, longitude: -83.5 }, typeLabels: { Fire: 'Feu', Grass: 'Plante' }, weather });

      const chips = Array.from(el.querySelectorAll('.type-chip')).map(c => c.textContent?.trim());
      expect(chips).toEqual(['Plante', 'Feu']);
    });

    /** The condition comes from the server in English too ("Windy"); the strip and tooltip translate it by its number. */
    it('names the condition in the display language', () => {
      const weather = { boostedTypes: [], condition: 5, conditionName: 'Windy', hasWarning: false, icon: 'air' } as unknown as WeatherData;
      const { component, el } = setup({ location: { latitude: 41.6, longitude: -83.5 }, weather });

      expect(el.querySelector('.strip-weather .strip-value')?.textContent?.trim()).toBe('DASHBOARD.WEATHER_CONDITION_5');
      expect(component.weatherConditionLabel(weather)).toBe('DASHBOARD.WEATHER_CONDITION_5');
    });

    it('keeps the server’s name for a condition it has no translation for', () => {
      const weather = { boostedTypes: [], condition: 42, conditionName: 'Hail', hasWarning: false, icon: 'help' } as unknown as WeatherData;
      const { component } = setup({ location: { latitude: 41.6, longitude: -83.5 }, weather });

      expect(component.weatherConditionLabel(weather)).toBe('Hail');
    });

    it('names them the same way in the area weather tooltip', () => {
      const { component } = setup({ typeLabels: { Fire: 'Feu', Grass: 'Plante' } });

      expect(component.boostedTypeLabels(['Grass', 'Fire'])).toBe('Plante, Feu');
    });
  });

  describe('accessibility', () => {
    it('announces a severe weather warning', () => {
      const weather = { boostedTypes: [], conditionName: 'Storm', hasWarning: true, icon: 'thunderstorm' } as unknown as WeatherData;
      const { el } = setup({ location: { latitude: 41.6, longitude: -83.5 }, weather });
      const badge = el.querySelector('.weather-warn-badge')!;

      expect(badge.getAttribute('aria-hidden')).toBe('false');
      expect(badge.getAttribute('aria-label')).toBe('DASHBOARD.WEATHER_WARNING');
    });

    // WCAG 2.5.3: the accessible name has to contain the visible "+5", or "click plus 5" finds nothing.
    it('names the +N button with its visible text', () => {
      const { el } = setup({ counts: { pokemon: 4 } });
      const more = el.querySelector<HTMLButtonElement>('.not-tracking-more')!;

      expect(more.hasAttribute('aria-label')).toBe(false);
      expect(more.textContent).toContain('DASHBOARD.MORE');
      expect(more.textContent).toContain('DASHBOARD.SHOW_ALL_TYPES');
    });
  });

  describe('alarm summary', () => {
    it('lists only the types that have alarms, with their counts', () => {
      const { el } = setup({ counts: { raids: 2, pokemon: 4, quests: 2 } });
      const rows = [...el.querySelectorAll('.type-count')].map(
        r => `${r.querySelector('.type-label')!.textContent!.trim()} ${r.querySelector('.type-number')!.textContent!.trim()}`,
      );

      expect(rows).toEqual(['DASHBOARD.CARD_POKEMON 4', 'DASHBOARD.CARD_RAIDS 2', 'DASHBOARD.CARD_QUESTS 2']);
    });

    it('opens the list for a type from its count', () => {
      const { el } = setup({ counts: { raids: 2 } });

      expect(el.querySelector('.type-count')!.getAttribute('href')).toBe('/raids');
    });

    it('folds every zero type into one line that can be expanded', () => {
      const { el, fixture } = setup({ counts: { pokemon: 4 } });
      const line = () => el.querySelector('.not-tracking')!;

      expect(el.querySelectorAll('.type-count')).toHaveLength(1);
      expect(line().textContent).toContain('DASHBOARD.NOT_TRACKING');
      const more = line().querySelector<HTMLButtonElement>('.not-tracking-more')!;
      expect(more.getAttribute('aria-expanded')).toBe('false');

      more.click();
      fixture.detectChanges();

      expect(line().querySelector('.not-tracking-more')!.getAttribute('aria-expanded')).toBe('true');
    });

    it('names every zero type once expanded, and none that has alarms', () => {
      const { component } = setup({ counts: { pokemon: 4 } });
      component.showAllIdle.set(true);

      const idle = component.idlePreview().map(c => c.key);
      expect(idle).not.toContain('pokemon');
      expect(idle).toHaveLength(10);
    });

    it('renames the "Not tracking" types when the display language changes', () => {
      const { component } = setup({ counts: { pokemon: 4 } });
      const i18n = TestBed.inject(I18nService);
      jest.spyOn(i18n, 'instant').mockImplementation((key: string) => `de:${key}`);
      // Still the names from the first render: nothing told the line the language moved.
      expect(component.idleNames()).not.toContain('de:');

      i18n.currentLang.set('de');

      expect(component.idleNames()).toContain('de:DASHBOARD.CARD_RAIDS');
    });

    // The row leaves out a switched-off type, so the total must too, or it reads more than the pills add up to.
    it('counts only the types it shows in the total', () => {
      const { component } = setup({ counts: { lures: 3, pokemon: 4 }, disabled: ['disable_lures'] });

      expect(component.totalAlarms()).toBe(4);
    });

    it('has no "Not tracking" line when every type has alarms', () => {
      const all = Object.fromEntries(Object.keys(NO_ALARMS).map(k => [k, 1])) as Partial<DashboardCounts>;

      expect(setup({ counts: all }).el.querySelector('.not-tracking')).toBeNull();
    });

    it('leaves a disabled type out of both the row and the line', () => {
      const { component } = setup({ counts: { lures: 3 }, disabled: ['disable_lures'] });
      component.showAllIdle.set(true);

      expect(component.activeTypes().map(t => t.card.key)).not.toContain('lures');
      expect(component.idlePreview().map(c => c.key)).not.toContain('lures');
    });
  });

  describe('New alarm', () => {
    it('is one primary menu listing each enabled type, raids and eggs once', () => {
      const { component } = setup({ disabled: ['disable_quests'] });
      const keys = component.newAlarmTypes().map(c => c.key);

      expect(keys).toContain('pokemon');
      expect(keys).toContain('raids');
      expect(keys).not.toContain('eggs');
      expect(keys).not.toContain('quests');
    });

    // Same order as the sidebar (app.ts NAV_ITEMS): max battles second-to-raids, not last.
    it('lists the types in the sidebar’s order', () => {
      const { component } = setup();

      expect(component.newAlarmTypes().map(c => c.route)).toEqual([
        '/pokemon',
        '/raids',
        '/max-battles',
        '/quests',
        '/invasions',
        '/pokestop-events',
        '/lures',
        '/nests',
        '/gyms',
        '/fort-changes',
      ]);
    });

    it('opens the chosen list with its add dialog', () => {
      const { el, fixture, navigate } = setup();
      el.querySelector<HTMLButtonElement>('.new-alarm-btn')!.click();
      fixture.detectChanges();

      document.querySelector<HTMLButtonElement>('.new-alarm-item.new-alarm-raids')!.click();

      expect(navigate).toHaveBeenCalledWith(['/raids'], { queryParams: { [ADD_QUERY_PARAM]: 1 } });
    });

    it('is absent when every alarm type is switched off', () => {
      const probe = setup();
      const everything = probe.component.cards.map(c => c.disableKey);

      expect(setup({ disabled: everything }).el.querySelector('.new-alarm-btn')).toBeNull();
    });
  });
});
