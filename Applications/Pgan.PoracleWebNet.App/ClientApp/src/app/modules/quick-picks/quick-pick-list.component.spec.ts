import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';

import { QuickPickListComponent } from './quick-pick-list.component';
import { QuickPickSummary } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { QuickPickService } from '../../core/services/quick-pick.service';

describe('QuickPickListComponent', () => {
  let component: QuickPickListComponent;
  const API = 'http://test-api';

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
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
      imports: [QuickPickListComponent, TranslateModule.forRoot()],
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
});
