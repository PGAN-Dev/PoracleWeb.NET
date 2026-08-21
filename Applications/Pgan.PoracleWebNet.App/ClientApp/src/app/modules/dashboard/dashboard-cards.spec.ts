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
 * A disabled alarm type is gone everywhere: the sidebar item, the dashboard card, the route and the
 * API all refuse it (#792). The card list is the last of those with any conditional logic, so this
 * pins that it drops a disabled type regardless of what is stored on it — a card linking to a page
 * that does not answer would be worse than no card.
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

  /** Alarms stored on a disabled type do not bring its card back — the page they link to is gated. */
  it('drops a disabled type even when it still has alarms', () => {
    const component = setup(['disable_lures'], { lures: 3 });

    expect(keys(component)).not.toContain('lures');
  });

  /** Eggs share the raid key, so disabling raids takes the egg card with it. */
  it('treats eggs as raids', () => {
    expect(keys(setup(['disable_raids'], {}))).not.toContain('eggs');
    expect(keys(setup(['disable_raids'], { eggs: 2 }))).not.toContain('eggs');
  });
});
