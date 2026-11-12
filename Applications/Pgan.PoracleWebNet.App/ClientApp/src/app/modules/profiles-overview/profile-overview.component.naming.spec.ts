import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { ProfileOverviewComponent } from './profile-overview.component';
import en from '../../../assets/i18n/en.json';
import { MasterDataService } from '../../core/services/masterdata.service';
import { ProfileOverviewService } from '../../core/services/profile-overview.service';
import { ProfileService } from '../../core/services/profile.service';

/**
 * The Profiles overview named the same rule differently from its own list page: "All level 5 raids" against
 * "All Legendary raids", "Level 3 egg" against "3 star Egg". It kept a private copy of every title, and the
 * copies drifted. These pin the overview to the words the list pages print, using the real English strings.
 */
describe('ProfileOverviewComponent names rules the way the lists do', () => {
  let component: ProfileOverviewComponent;

  const describe_ = (type: string, alarm: Record<string, unknown>) => component.getAlarmDescription({ uid: 1, ...alarm } as never, type);

  beforeEach(async () => {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        {
          provide: MasterDataService,
          useValue: {
            getItemName: (id: number) => `Item ${id}`,
            getPokemonName: (id: number) => `Mon ${id}`,
            loadData: () => of(null),
          },
        },
        { provide: ProfileOverviewService, useValue: { getOverview: () => of(null) } },
        { provide: ProfileService, useValue: { getAll: () => of([]) } },
      ],
      imports: [ProfileOverviewComponent, NoopAnimationsModule],
    }).compileComponents();

    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', en);
    translate.use('en');
    component = TestBed.createComponent(ProfileOverviewComponent).componentInstance;
  });

  it('names a level raid by its tier, as the Raids page does', () => {
    expect(describe_('raid', { pokemon_id: 9000, level: 5 })).toBe('All Legendary raids');
    expect(describe_('raid', { pokemon_id: 9000, level: 6 })).toBe('All Mega raids');
    expect(describe_('raid', { pokemon_id: 9000, level: 3 })).toBe('All 3 star raids');
  });

  it('still says "All raids" for the every-tier wildcard and names a boss', () => {
    expect(describe_('raid', { pokemon_id: 9000, level: 9000 })).toBe('All raids');
    expect(describe_('raid', { pokemon_id: 150, level: 5 })).toBe('Mon 150');
  });

  it('names an egg by its tier plus "Egg", as the Raids page does', () => {
    expect(describe_('egg', { level: 3 })).toBe('3 star Egg');
    expect(describe_('egg', { level: 5 })).toBe('Legendary Egg');
    expect(describe_('egg', { level: 9000 })).toBe('Any Egg');
  });

  it('names a lure with the "Lure" suffix the Lures page uses', () => {
    expect(describe_('lure', { lure_id: 501 })).toBe('Normal Lure');
    expect(describe_('lure', { lure_id: 506 })).toBe('Golden Lure');
  });

  it('names a gym rule by its team, neutral included', () => {
    expect(describe_('gym', { team: 0 })).toBe('Neutral');
    expect(describe_('gym', { team: 1 })).toBe('Mystic');
    expect(describe_('gym', { team: 4 })).toBe('Any team');
  });

  it('names a Max battle by its Pokémon, with "Any Pokémon" for the wildcard', () => {
    expect(describe_('maxbattle', { pokemon_id: 9000, level: 3 })).toBe('Any Pokémon');
    expect(describe_('maxbattle', { pokemon_id: 25, level: 3 })).toBe('Mon 25');
  });

  it('names a fort change by the Fort changes page wording', () => {
    expect(describe_('fort', { fort_type: 'pokestop' })).toBe('Pokéstop');
    expect(describe_('fort', { fort_type: null })).toBe(en.FORT_CHANGES.FORT_EVERYTHING);
  });

  it('words quest rewards as the Quests page does', () => {
    expect(describe_('quest', { amount: 0, reward: 1500, reward_type: 3 })).toBe('1500 Stardust');
    expect(describe_('quest', { amount: 3, reward: 0, reward_type: 2 })).toBe('3× Any item');
    expect(describe_('quest', { amount: 0, reward: 0, reward_type: 7 })).toBe('Any Pokémon encounter');
    expect(describe_('quest', { pokemon_id: 25, amount: 0, reward: 25, reward_type: 12 })).toBe('Mon 25 Mega Energy');
  });
});
