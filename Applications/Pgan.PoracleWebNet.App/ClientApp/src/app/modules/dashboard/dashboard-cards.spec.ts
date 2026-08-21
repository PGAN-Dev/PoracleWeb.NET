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

/**
 * The dashboard card is the way into a disabled alarm type's page, now that the sidebar item is gone
 * (#792). It has to appear for exactly the people who need it — those still holding rules of that
 * type — and for nobody else, or it becomes the noise it was meant to remove.
 */
describe('DashboardComponent cards', () => {
  const EMPTY: DashboardCounts = {
    raids: 0,
    eggs: 0,
    fortChanges: 0,
    gyms: 0,
    invasions: 0,
    lures: 0,
    maxBattles: 0,
    nests: 0,
    pokemon: 0,
    quests: 0,
  };

  const setup = (disabled: string[], counts: Partial<DashboardCounts>) => {
    const siteSettings = signal<Record<string, string>>(Object.fromEntries(disabled.map(k => [k, 'true'])));

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        provideTranslateService(),
        {
          provide: SettingsService,
          useValue: {
            isDisabled: (key: string) => siteSettings()[key]?.toLowerCase() === 'true',
            isForcedByPoracle: () => false,
            siteSettings,
          },
        },
        { provide: DashboardService, useValue: { getCounts: () => of({ ...EMPTY, ...counts }) } },
        { provide: AuthService, useValue: { isAdmin: () => false, user: signal({ username: 'someone' }) } },
        { provide: AreaService, useValue: { getAreas: () => of([]), getGeofence: () => of([]) } },
        { provide: ProfileService, useValue: { getProfiles: () => of([]) } },
        { provide: LocationService, useValue: { getLocation: () => of({ latitude: 0, longitude: 0 }) } },
      ],
    });

    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.componentInstance.counts.set({ ...EMPTY, ...counts });
    return fixture.componentInstance;
  };

  const keys = (component: DashboardComponent) => component.visibleCards().map(c => c.key);

  it('shows every card while nothing is disabled', () => {
    const component = setup([], {});

    expect(keys(component)).toEqual([
      'pokemon',
      'raids',
      'eggs',
      'quests',
      'invasions',
      'lures',
      'nests',
      'gyms',
      'fortChanges',
      'maxBattles',
    ]);
  });

  it('drops a disabled type that has no alarms', () => {
    const component = setup(['disable_lures'], {});

    expect(keys(component)).not.toContain('lures');
  });

  /** The case the card exists for: without it the page has no entry point at all. */
  it('keeps a disabled type that still has alarms', () => {
    const component = setup(['disable_lures'], { lures: 3 });

    expect(keys(component)).toContain('lures');
  });

  it('marks that card as locked so it does not read as an invitation', () => {
    const component = setup(['disable_lures'], { lures: 3 });
    const lures = component.visibleCards().find(c => c.key === 'lures')!;

    expect(component.isLockedCard(lures)).toBe(true);
  });

  it('leaves enabled types unmarked even when they are empty', () => {
    const component = setup([], {});
    const lures = component.visibleCards().find(c => c.key === 'lures')!;

    expect(component.isLockedCard(lures)).toBe(false);
  });

  /** Eggs share the raid key, so disabling raids must not leave an egg card behind. */
  it('treats eggs as raids', () => {
    const withNone = setup(['disable_raids'], {});
    expect(keys(withNone)).not.toContain('eggs');

    const withEggs = setup(['disable_raids'], { eggs: 2 });
    expect(keys(withEggs)).toContain('eggs');
  });
});
