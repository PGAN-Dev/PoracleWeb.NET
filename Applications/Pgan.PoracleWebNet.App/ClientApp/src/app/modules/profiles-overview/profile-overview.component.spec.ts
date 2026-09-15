import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
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
 * The Profiles page reported two different duplicate counts under the same word: the summary chip
 * counted groups of duplicated rules and the filter chip counted the rules themselves. Chasing that
 * turned up the larger defect underneath — the filter keyed on `uid` alone, and Poracle numbers each
 * tracking table separately, so uids collide across types (production carries 46 shared between
 * monsters and quest, and 14 between monsters and raid).
 */
describe('ProfileOverviewComponent duplicate detection', () => {
  let component: ProfileOverviewComponent;

  /** Two profiles, and whatever alarms a test puts on them. */
  const overview = (alarms: Partial<Record<string, unknown[]>>) =>
    ({
      profile: [
        { name: 'Home', profile_no: 1 },
        { name: 'Work', profile_no: 2 },
      ],
      ...alarms,
    }) as never;

  const monster = (uid: number, profileNo: number, pokemonId = 25) => ({
    pokemon_id: pokemonId,
    uid,
    distance: 1000,
    form: 0,
    profile_no: profileNo,
  });

  const raid = (uid: number, profileNo: number, level = 5) => ({
    pokemon_id: 9000,
    uid,
    distance: 1000,
    level,
    profile_no: profileNo,
    team: 4,
  });

  beforeEach(async () => {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        { provide: MasterDataService, useValue: { getMonsterName: () => 'Pikachu', loadData: () => of(null) } },
        { provide: ProfileOverviewService, useValue: { getOverview: () => of(null) } },
        { provide: ProfileService, useValue: { getProfiles: () => of([]) } },
      ],
      imports: [ProfileOverviewComponent, NoopAnimationsModule],
    }).compileComponents();

    component = TestBed.createComponent(ProfileOverviewComponent).componentInstance;
  });

  it('counts a rule that appears on two profiles as duplicated on both', () => {
    component.overview.set(overview({ pokemon: [monster(1, 1), monster(2, 2)] }));

    expect(component.duplicateKeys().size).toBe(2);
    expect(component.isDuplicate(monster(1, 1) as never, 'pokemon')).toBe(true);
  });

  it('leaves a rule that appears once alone', () => {
    component.overview.set(overview({ pokemon: [monster(1, 1), monster(2, 2, 150)] }));

    expect(component.duplicateKeys().size).toBe(0);
    expect(component.isDuplicate(monster(1, 1) as never, 'pokemon')).toBe(false);
  });

  /**
   * The one that matters. A duplicated Pokemon rule and an unrelated raid rule that happens to carry
   * the same uid: keyed on uid alone the raid rule reads as duplicated, gets the tag, and shows up
   * under a filter it has no business being in.
   */
  it('does not treat a raid rule as duplicated because a Pokemon rule shares its uid', () => {
    component.overview.set(
      overview({
        raid: [raid(207, 1)],
        pokemon: [monster(207, 1), monster(208, 2)],
      }),
    );

    expect(component.isDuplicate(raid(207, 1) as never, 'raid')).toBe(false);
    expect(component.isDuplicate(monster(207, 1) as never, 'pokemon')).toBe(true);
    expect(component.duplicateKeys().size).toBe(2);
  });

  /**
   * The reported symptom. Both chips read the same signal now, so they cannot disagree — the summary
   * chip and the filter chip are the same number by construction rather than by coincidence.
   */
  it('shows one duplicate count, not two', () => {
    component.overview.set(
      overview({
        raid: [raid(10, 1)],
        pokemon: [monster(1, 1), monster(2, 2), monster(3, 1, 150), monster(4, 2, 150)],
      }),
    );

    expect(component.stats().duplicateCount).toBe(component.duplicateKeys().size);
    expect(component.stats().duplicateCount).toBe(4);
  });

  it('filters to exactly the rules it counted', () => {
    component.overview.set(
      overview({
        raid: [raid(207, 1)],
        pokemon: [monster(207, 1), monster(208, 2)],
      }),
    );

    component.showDuplicatesOnly.set(true);

    const shown = component.filteredProfiles().reduce((total, group) => total + group.totalAlarms, 0);

    expect(shown).toBe(component.duplicateKeys().size);
  });
});
