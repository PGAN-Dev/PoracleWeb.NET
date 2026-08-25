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

/**
 * Pokecoin quests are offered only when the PoracleNG behind this install can store them.
 *
 * PoracleNG below 5.2.0 answers 400 "Unrecognised reward_type value" -- confirmed by POSTing to a live
 * 5.1.0 -- so the reward type is absent rather than present-and-failing. The pairing matters more than
 * either half: a test that only proved it hides on an old server would pass just as well if it never
 * rendered at all.
 */
describe('QuestAddDialogComponent — pokecoins capability', () => {
  let component: QuestAddDialogComponent;
  let fixture: ComponentFixture<QuestAddDialogComponent>;
  let questService: { create: jest.Mock; loadPokecoinCapability: jest.Mock; pokecoinsSupported: () => boolean };

  function setup(supported: boolean) {
    questService = {
      create: jest.fn().mockReturnValue(of({} as Quest)),
      loadPokecoinCapability: jest.fn(),
      pokecoinsSupported: () => supported,
    };

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

    fixture = TestBed.createComponent(QuestAddDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  /** The reward types offered, as `value` numbers. `save()` switches on them, so they are load-bearing. */
  function rewardKindValues(): number[] {
    return component.rewardKinds.filter(kind => kind.value !== 5 || component.supportsPokecoins()).map(kind => kind.value);
  }

  /** The reward types as they render, in order. */
  function renderedRewardOptions(): string[] {
    const trigger = fixture.nativeElement.querySelector('.reward-kind-field mat-select') as HTMLElement;
    trigger.querySelector('.mat-mdc-select-trigger')!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    const options = Array.from(document.querySelectorAll('mat-option')).map(el => (el as HTMLElement).textContent!.trim());
    (document.querySelector('.cdk-overlay-backdrop') as HTMLElement | null)?.click();
    fixture.detectChanges();
    return options;
  }

  it('offers no pokecoins reward against a PoracleNG that would refuse it', () => {
    setup(false);

    expect(component.supportsPokecoins()).toBe(false);
    expect(renderedRewardOptions()).toHaveLength(5);
  });

  it('offers a pokecoins reward against a 5.2.0 or newer PoracleNG', () => {
    setup(true);

    expect(component.supportsPokecoins()).toBe(true);
    expect(renderedRewardOptions()).toHaveLength(6);
  });

  /**
   * `save()` switches on `rewardKind`, so the numbers are the contract. They are declared per reward
   * type rather than taken from render order, which is what makes the pokecoins one safe to hide.
   */
  it('leaves the existing reward type values untouched when pokecoins appears', () => {
    setup(false);
    const withoutPokecoins = rewardKindValues();

    setup(true);
    const withPokecoins = rewardKindValues();

    expect(withoutPokecoins).toEqual([0, 1, 2, 3, 4]);
    expect(withPokecoins).toEqual([0, 1, 2, 3, 4, 5]);
  });

  /**
   * The six reward types are chosen from one control, not a strip that runs out of room. A nested tab
   * strip clipped the sixth label to "Pok" and hid it behind a pagination arrow at dialog width -- and
   * would do the same to the fifth in the several locales whose words are longer than English's.
   */
  it('puts every reward type in one control rather than a strip that can overflow', () => {
    setup(true);

    expect(fixture.nativeElement.querySelectorAll('.reward-kind-field mat-select')).toHaveLength(1);
    expect(fixture.nativeElement.querySelectorAll('.reward-tabs')).toHaveLength(0);
  });

  it('creates a pokecoin quest with the minimum in the reward slot', () => {
    setup(true);

    component.rewardKind = 5;
    component.pokecoinsForm.controls.reward.setValue(50);
    component.save();

    const create = questService.create.mock.calls[0][0] as QuestCreate;
    expect(create.rewardType).toBe(8);
    // PoracleNG matches pokecoins on the amount alone and reads it from `reward`, as it does stardust.
    expect(create.reward).toBe(50);
    expect(create.amount).toBe(0);
  });

  it('treats a minimum of 0 as every pokecoin quest, not as an incomplete form', () => {
    setup(true);

    component.rewardKind = 5;

    expect(component.canSave()).toBe(true);
  });

  /** The stardust tab keeps working on a server that has no pokecoin support at all. */
  it('still creates a stardust quest against an older PoracleNG', () => {
    setup(false);

    component.rewardKind = 4;
    component.stardustForm.controls.reward.setValue(1000);
    component.save();

    const create = questService.create.mock.calls[0][0] as QuestCreate;
    expect(create.rewardType).toBe(3);
    expect(create.reward).toBe(1000);
  });
});
