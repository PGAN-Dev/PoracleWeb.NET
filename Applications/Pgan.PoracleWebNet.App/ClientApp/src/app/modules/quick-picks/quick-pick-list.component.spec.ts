import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { QuickPickListComponent } from './quick-pick-list.component';
import { QuickPickSummary } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { QuickPickService } from '../../core/services/quick-pick.service';
import { SettingsService } from '../../core/services/settings.service';

describe('QuickPickListComponent', () => {
  let component: QuickPickListComponent;
  const API = 'http://test-api';

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        {
          provide: AuthService,
          useValue: {
            currentUser: () => null,
            isAdmin: () => false,
          },
        },
        QuickPickService,
      ],
      imports: [QuickPickListComponent],
    });

    const fixture = TestBed.createComponent(QuickPickListComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should initialize with loading true and empty picks', () => {
    expect(component.loading()).toBe(true);
    expect(component.picks()).toEqual([]);
  });

  it('should compute categories from picks', () => {
    const picks: QuickPickSummary[] = [
      {
        appliedState: null,
        definition: {
          id: '1',
          name: 'A',
          alarmType: 'monster',
          category: 'PvP',
          description: '',
          enabled: true,
          filters: {},
          icon: '',
          scope: 'global',
          sortOrder: 1,
        },
      },
      {
        appliedState: null,
        definition: {
          id: '2',
          name: 'B',
          alarmType: 'raid',
          category: 'Raids',
          description: '',
          enabled: true,
          filters: {},
          icon: '',
          scope: 'global',
          sortOrder: 2,
        },
      },
      {
        appliedState: null,
        definition: {
          id: '3',
          name: 'C',
          alarmType: 'monster',
          category: 'PvP',
          description: '',
          enabled: true,
          filters: {},
          icon: '',
          scope: 'global',
          sortOrder: 3,
        },
      },
    ];

    component.picks.set(picks);

    const cats = component.categories();
    expect(cats[0]).toBe('All');
    expect(cats).toContain('PvP');
    expect(cats).toContain('Raids');
    // 'All' + 2 unique categories
    expect(cats).toHaveLength(3);
  });

  it('should filter picks by selected category', () => {
    const picks: QuickPickSummary[] = [
      {
        appliedState: null,
        definition: {
          id: '1',
          name: 'A',
          alarmType: 'monster',
          category: 'PvP',
          description: '',
          enabled: true,
          filters: {},
          icon: '',
          scope: 'global',
          sortOrder: 1,
        },
      },
      {
        appliedState: null,
        definition: {
          id: '2',
          name: 'B',
          alarmType: 'raid',
          category: 'Raids',
          description: '',
          enabled: true,
          filters: {},
          icon: '',
          scope: 'global',
          sortOrder: 2,
        },
      },
    ];

    component.picks.set(picks);
    component.selectedCategory.set('PvP');

    expect(component.filteredPicks()).toHaveLength(1);
    expect(component.filteredPicks()[0].definition.name).toBe('A');
  });

  it('should return all picks when category is null', () => {
    const picks: QuickPickSummary[] = [
      {
        appliedState: null,
        definition: {
          id: '1',
          name: 'A',
          alarmType: 'monster',
          category: 'PvP',
          description: '',
          enabled: true,
          filters: {},
          icon: '',
          scope: 'global',
          sortOrder: 1,
        },
      },
      {
        appliedState: null,
        definition: {
          id: '2',
          name: 'B',
          alarmType: 'raid',
          category: 'Raids',
          description: '',
          enabled: true,
          filters: {},
          icon: '',
          scope: 'global',
          sortOrder: 2,
        },
      },
    ];

    component.picks.set(picks);
    component.selectedCategory.set(null);

    expect(component.filteredPicks()).toHaveLength(2);
  });

  it('should set selectedCategory to null when selecting All', () => {
    component.selectCategory('All');
    expect(component.selectedCategory()).toBeNull();
  });

  it('should set selectedCategory when selecting a specific category', () => {
    component.selectCategory('PvP');
    expect(component.selectedCategory()).toBe('PvP');
  });

  describe('auto-seeding the built-in presets', () => {
    // The guard exists so an admin who deletes the presets on purpose keeps an empty list (#634). It
    // moved from a localStorage flag to a site setting (#662), and the local signal is only refreshed
    // at app init -- so without updating it after a seed, deleting the last pick in the same session
    // restored all thirty. See #666.
    function setupAdmin(siteSettings: Record<string, string>, picks: QuickPickSummary[]) {
      const seed = jest.fn(() => of(undefined));
      const settings = { siteSettings: signal(siteSettings) };

      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          provideTranslateService(),
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: ConfigService, useValue: { apiHost: API } },
          { provide: AuthService, useValue: { currentUser: () => null, isAdmin: () => true } },
          { provide: SettingsService, useValue: settings },
          { provide: QuickPickService, useValue: { getAll: jest.fn(() => of(picks)), seed } },
        ],
        imports: [QuickPickListComponent],
      });

      const fixture = TestBed.createComponent(QuickPickListComponent);
      return { component: fixture.componentInstance, seed, settings };
    }

    it('seeds when the list is empty and the installation has never been seeded', () => {
      const { component: sut, seed } = setupAdmin({}, []);

      sut.loadPicks();

      expect(seed).toHaveBeenCalled();
    });

    it('does not seed again after seeding once in the same session', () => {
      const { component: sut, seed } = setupAdmin({}, []);

      sut.loadPicks();
      sut.loadPicks();

      expect(seed).toHaveBeenCalledTimes(1);
    });

    it('does not seed an installation that has already been seeded', () => {
      const { component: sut, seed } = setupAdmin({ quick_picks_seeded: 'true' }, []);

      sut.loadPicks();

      expect(seed).not.toHaveBeenCalled();
    });
  });
});
