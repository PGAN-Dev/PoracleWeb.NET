import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { ProfileOverviewComponent } from './profile-overview.component';
import { MasterDataService } from '../../core/services/masterdata.service';
import { ProfileOverviewService } from '../../core/services/profile-overview.service';
import { ProfileService } from '../../core/services/profile.service';

/**
 * The claim in #831 is about two chips on a screen and a tag on a card, so it has to be checked on the
 * rendered page. The class-level spec next to this one can pass while `type.key` is out of scope in the
 * template, or while the summary chip reads a different signal from the filter chip.
 */
describe('ProfileOverviewComponent rendered duplicates', () => {
  let fixture: ComponentFixture<ProfileOverviewComponent>;

  const HOME = { id: 'u', name: 'Home', profile_no: 1 };
  const WORK = { id: 'u', name: 'Work', profile_no: 2 };

  const monster = (uid: number, profileNo: number, pokemonId: number) => ({
    pokemon_id: pokemonId,
    uid,
    clean: 0,
    distance: 1000,
    form: 0,
    max_iv: 100,
    min_iv: 0,
    profile_no: profileNo,
  });

  const raid = (uid: number, profileNo: number, level = 5) => ({
    pokemon_id: 9000,
    uid,
    clean: 0,
    distance: 1000,
    form: 0,
    level,
    profile_no: profileNo,
    team: 4,
  });

  /** Renders the page the way `ngOnInit` does: mocked wire responses in, real signals and template out. */
  const render = async (overview: Record<string, unknown>) => {
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
            getFormName: () => '',
            getItemName: (id: number) => `Item ${id}`,
            getPokemonName: (id: number) => `Pokemon ${id}`,
            loadData: () => of(null),
          },
        },
        { provide: ProfileOverviewService, useValue: { getOverview: () => of(overview) } },
        {
          provide: ProfileService,
          useValue: {
            getAll: () =>
              of([
                { name: 'Home', active: true, activeHours: null, latitude: 0, longitude: 0, profileNo: 1 },
                { name: 'Work', active: false, activeHours: null, latitude: 0, longitude: 0, profileNo: 2 },
              ]),
          },
        },
      ],
      imports: [ProfileOverviewComponent, NoopAnimationsModule],
    }).compileComponents();

    fixture = TestBed.createComponent(ProfileOverviewComponent);
    fixture.detectChanges();
    // Panels start collapsed; open them so the assertions read a page a user could actually be looking at.
    fixture.componentInstance.expandedProfiles.set(new Set([1, 2]));
    fixture.detectChanges();
    return fixture;
  };

  const text = (el: Element | null | undefined) => el?.textContent?.trim() ?? '';

  /** The duplicates pill in the stats summary bar. */
  const summaryChip = (): HTMLElement | null => fixture.nativeElement.querySelector('.stat-duplicates');

  /** The duplicates pill in the filter bar, found the way a user finds it: by its icon. */
  const filterChip = (): HTMLElement | undefined =>
    (Array.from(fixture.nativeElement.querySelectorAll('.type-filters .type-chip')) as HTMLElement[]).find(
      chip => text(chip.querySelector('mat-icon')) === 'content_copy',
    );

  /** Every alarm card on the page, with the profile and the type section it sits under. */
  const cards = () => {
    const out: { duplicate: boolean; profile: string; title: string; type: string }[] = [];
    for (const panel of Array.from(fixture.nativeElement.querySelectorAll('mat-expansion-panel')) as HTMLElement[]) {
      const profile = text(panel.querySelector('.profile-name'));
      for (const section of Array.from(panel.querySelectorAll('.type-section')) as HTMLElement[]) {
        const type = text(section.querySelector('.type-section-header span'));
        for (const card of Array.from(section.querySelectorAll('.alarm-card')) as HTMLElement[]) {
          out.push({
            duplicate: !!card.querySelector('.duplicate-tag'),
            profile,
            title: text(card.querySelector('.alarm-info h3')),
            type,
          });
        }
      }
    }
    return out;
  };

  /**
   * Pikachu and Mewtwo tracked on both profiles, Chansey on one. A raid rule carries uid 207, the same
   * number as one of the duplicated Pikachu rules, because Poracle numbers `monsters` and `raid`
   * separately and their ranges overlap on live data.
   */
  const collidingUids = {
    raid: [raid(207, 2)],
    pokemon: [monster(207, 1, 25), monster(208, 2, 25), monster(300, 1, 150), monster(301, 2, 150), monster(400, 1, 113)],
    profile: [HOME, WORK],
  };

  it('shows the same number on the summary chip and the filter chip', async () => {
    await render(collidingUids);

    expect(text(summaryChip()?.querySelector('.stat-chip-value'))).toBe('4');
    expect(text(filterChip()?.querySelector('.type-count'))).toBe('4');
    // Both pills say the same word, which is what made two numbers a bug rather than two facts.
    expect(text(summaryChip()?.querySelector('.stat-chip-label'))).toBe('PROFILES.DUPLICATES');
    expect(text(filterChip()).replace(/\s+/g, ' ')).toContain('PROFILES.DUPLICATES');
  });

  it('tags exactly the four duplicated Pokemon cards and leaves the uid-sharing raid alone', async () => {
    await render(collidingUids);

    const tagged = cards()
      .filter(c => c.duplicate)
      .map(c => `${c.profile}/${c.type}/${c.title}`)
      .sort();

    expect(tagged).toEqual([
      'Home/PROFILES.TYPE_POKEMON/Pokemon 150',
      'Home/PROFILES.TYPE_POKEMON/Pokemon 25',
      'Work/PROFILES.TYPE_POKEMON/Pokemon 150',
      'Work/PROFILES.TYPE_POKEMON/Pokemon 25',
    ]);

    const raidCard = cards().find(c => c.type === 'PROFILES.TYPE_RAIDS');
    expect(raidCard).toBeDefined();
    expect(raidCard?.duplicate).toBe(false);
  });

  it('renders exactly the rows the filter chip counted when it is clicked', async () => {
    await render(collidingUids);
    expect(cards()).toHaveLength(6);

    filterChip()?.click();
    fixture.componentInstance.expandedProfiles.set(new Set([1, 2]));
    fixture.detectChanges();

    const shown = cards();
    expect(shown).toHaveLength(Number(text(filterChip()?.querySelector('.type-count'))));
    expect(shown.every(c => c.duplicate)).toBe(true);
    expect(shown.some(c => c.type === 'PROFILES.TYPE_RAIDS')).toBe(false);
  });

  /**
   * The mirror image, so the tag cannot be passing by virtue of `type.key` always meaning `pokemon`:
   * here the raids are the duplicated pair and the Pokemon rule is the innocent uid-sharer.
   */
  it('reads type.key per card, so a duplicated raid tags raids and not the Pokemon sharing its uid', async () => {
    await render({
      raid: [raid(500, 1), raid(501, 2)],
      pokemon: [monster(500, 1, 25)],
      profile: [HOME, WORK],
    });

    const tagged = cards()
      .filter(c => c.duplicate)
      .map(c => c.type)
      .sort();

    expect(tagged).toEqual(['PROFILES.TYPE_RAIDS', 'PROFILES.TYPE_RAIDS']);
    expect(cards().find(c => c.type === 'PROFILES.TYPE_POKEMON')?.duplicate).toBe(false);
    expect(text(filterChip()?.querySelector('.type-count'))).toBe('2');
  });

  it('draws no duplicates chip at all when nothing is duplicated', async () => {
    await render({ pokemon: [monster(1, 1, 25), monster(2, 2, 150)], profile: [HOME, WORK] });

    expect(summaryChip()).toBeNull();
    expect(filterChip()).toBeUndefined();
    expect(cards().every(c => !c.duplicate)).toBe(true);
  });
});
