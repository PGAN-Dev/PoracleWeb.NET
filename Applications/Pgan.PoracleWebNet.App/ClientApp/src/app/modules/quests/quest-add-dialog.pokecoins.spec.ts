import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { QuestAddDialogComponent } from './quest-add-dialog.component';
import { Quest, QuestCreate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { PokemonAvailabilityService } from '../../core/services/pokemon-availability.service';
import { QuestService } from '../../core/services/quest.service';
import { SettingsService } from '../../core/services/settings.service';
import { PORACLE_CAPABILITIES } from '../../shared/utils/poracle-capabilities';

/**
 * Pokecoin quests are offered only when the PoracleNG behind this install can store them.
 *
 * PoracleNG below 5.2.0 answers 400 "Unrecognised reward_type value", so the tab is absent rather
 * than present-and-failing. The pairing matters more than either half: a test that only proved the
 * tab hides on an old server would pass just as well if it never rendered at all.
 */
describe('QuestAddDialogComponent — pokecoins capability', () => {
  let component: QuestAddDialogComponent;
  let fixture: ComponentFixture<QuestAddDialogComponent>;
  let questService: { create: jest.Mock };

  function setup(capabilities: string[]) {
    questService = { create: jest.fn().mockReturnValue(of({} as Quest)) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: QuestService, useValue: questService },
        { provide: AuthService, useValue: { isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
        {
          provide: MasterDataService,
          useValue: {
            getAllItems: () => [],
            getAllPokemon: () => [],
            getAllPokemon$: () => of([]),
            getAllTypes: () => [],
            getPokemonTypes: () => [],
            loadData: () => of(void 0),
          },
        },
        { provide: PokemonAvailabilityService, useValue: { enabled: () => false, isAvailable: () => true, load: () => undefined } },
        { provide: IconService, useValue: { getItemUrl: () => '' } },
      ],
      imports: [QuestAddDialogComponent],
    });

    // The real service, with the signal set directly: what matters is what the component reads, and
    // a hand-rolled stub would drift from the real shape the rest of the dialog tree depends on.
    TestBed.inject(SettingsService).poracleCapabilities.set(capabilities);

    fixture = TestBed.createComponent(QuestAddDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  /** Tab labels, in order. `tabIndex` is positional, so the order is load-bearing. */
  function rewardTabLabels(): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.reward-tabs .mat-mdc-tab .mdc-tab__text-label')).map(el =>
      (el as HTMLElement).textContent!.trim(),
    );
  }

  it('offers no pokecoins tab against a released PoracleNG', () => {
    setup([]);

    expect(component.supportsPokecoins()).toBe(false);
    expect(rewardTabLabels()).toHaveLength(5);
  });

  it('offers a pokecoins tab against a develop-line PoracleNG', () => {
    setup([PORACLE_CAPABILITIES.QUEST_POKECOINS]);

    expect(component.supportsPokecoins()).toBe(true);
    expect(rewardTabLabels()).toHaveLength(6);
  });

  /**
   * The five original tabs must keep their indices whether the sixth renders or not: `save()` reads
   * `tabIndex` positionally, so a tab inserted anywhere but the end would file every reward under the
   * wrong type.
   */
  it('leaves the existing tab indices untouched when the pokecoins tab appears', () => {
    setup([]);
    const withoutPokecoins = rewardTabLabels();

    setup([PORACLE_CAPABILITIES.QUEST_POKECOINS]);
    const withPokecoins = rewardTabLabels();

    expect(withPokecoins.slice(0, 5)).toEqual(withoutPokecoins);
  });

  it('creates a pokecoin quest with the minimum in the reward slot', () => {
    setup([PORACLE_CAPABILITIES.QUEST_POKECOINS]);

    component.tabIndex = 5;
    component.pokecoinsForm.controls.reward.setValue(50);
    component.save();

    const create = questService.create.mock.calls[0][0] as QuestCreate;
    expect(create.rewardType).toBe(8);
    // PoracleNG matches pokecoins on the amount alone and reads it from `reward`, as it does stardust.
    expect(create.reward).toBe(50);
    expect(create.amount).toBe(0);
  });

  it('treats a minimum of 0 as every pokecoin quest, not as an incomplete form', () => {
    setup([PORACLE_CAPABILITIES.QUEST_POKECOINS]);

    component.tabIndex = 5;

    expect(component.canSave()).toBe(true);
  });

  /** The stardust tab keeps working, on a server that has no pokecoin support at all. */
  it('still creates a stardust quest on a released PoracleNG', () => {
    setup([]);

    component.tabIndex = 4;
    component.stardustForm.controls.reward.setValue(1000);
    component.save();

    const create = questService.create.mock.calls[0][0] as QuestCreate;
    expect(create.rewardType).toBe(3);
    expect(create.reward).toBe(1000);
  });
});
