import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
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

describe('QuestAddDialogComponent', () => {
  let component: QuestAddDialogComponent;
  let dialogRef: { close: jest.Mock };
  let questService: { create: jest.Mock };

  function setup() {
    dialogRef = { close: jest.fn() };
    questService = { create: jest.fn().mockReturnValue(of({} as Quest)) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        // delivery-preview links to /areas with routerLink, so a router is required.
        provideRouter([]),
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: dialogRef },
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

    const fixture = TestBed.createComponent(QuestAddDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function createdClean(): number {
    const create = questService.create.mock.calls[0][0] as QuestCreate;
    return create.clean;
  }

  beforeEach(() => setup());

  it('defaults the summary toggle to off', () => {
    expect(component.commonForm.controls.summary.value).toBe(false);
  });

  it('defaults the clean (auto-delete) toggle to off', () => {
    expect(component.commonForm.controls.clean.value).toBe(false);
  });

  it('composes clean=0 when neither toggle is set', () => {
    component.selectedPokemonIds.set([25]);
    component.save();
    expect(createdClean()).toBe(0);
  });

  it('composes bit 4 when only summary is on', () => {
    component.selectedPokemonIds.set([25]);
    component.commonForm.controls.summary.setValue(true);
    component.save();
    expect(createdClean()).toBe(4);
  });

  it('composes bit 1 when only auto-delete is on', () => {
    component.selectedPokemonIds.set([25]);
    component.commonForm.controls.clean.setValue(true);
    component.save();
    expect(createdClean()).toBe(1);
  });

  it('composes bits 1|4 = 5 when both toggles are on', () => {
    component.selectedPokemonIds.set([25]);
    component.commonForm.controls.clean.setValue(true);
    component.commonForm.controls.summary.setValue(true);
    component.save();
    expect(createdClean()).toBe(5);
  });

  it('applies the same composed clean to every selected pokemon reward', () => {
    component.selectedPokemonIds.set([25, 133, 1]);
    component.commonForm.controls.summary.setValue(true);
    component.save();
    expect(questService.create).toHaveBeenCalledTimes(3);
    for (const call of questService.create.mock.calls) {
      expect((call[0] as QuestCreate).clean).toBe(4);
    }
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('creates a stardust rule from the amount alone', () => {
    // PoracleNG matches stardust on the reward column, not the amount one, so the floor travels there.
    component.tabIndex = 4;
    component.stardustForm.controls.reward.setValue(1500);

    component.save();

    const created = questService.create.mock.calls[0][0] as QuestCreate;
    expect(created.rewardType).toBe(3);
    expect(created.reward).toBe(1500);
  });

  it('treats a stardust rule with no floor as every stardust quest', () => {
    component.tabIndex = 4;

    component.save();

    expect((questService.create.mock.calls[0][0] as QuestCreate).reward).toBe(0);
  });

  it('sends the minimum amount with an item rule', () => {
    component.tabIndex = 1;
    component.itemForm.controls.reward.setValue(1301);
    component.itemForm.controls.amount.setValue(3);

    component.save();

    const created = questService.create.mock.calls[0][0] as QuestCreate;
    expect(created.rewardType).toBe(2);
    expect(created.amount).toBe(3);
  });

  it('sends the minimum amount with a mega energy rule, on every selected pokemon', () => {
    component.tabIndex = 2;
    component.selectedMegaPokemonIds.set([6, 9]);
    component.megaForm.controls.amount.setValue(50);

    component.save();

    expect(questService.create).toHaveBeenCalledTimes(2);
    for (const call of questService.create.mock.calls) {
      expect((call[0] as QuestCreate).amount).toBe(50);
    }
  });

  it('sends the minimum amount with a candy rule', () => {
    component.tabIndex = 3;
    component.selectedCandyPokemonIds.set([133]);
    component.candyForm.controls.amount.setValue(5);

    component.save();

    expect((questService.create.mock.calls[0][0] as QuestCreate).amount).toBe(5);
  });

  it('asks for no minimum on a pokemon encounter, which has no quantity', () => {
    // The legitimate twin: an encounter rule carrying an amount would be a filter PoracleNG reads for
    // other reward types and nobody chose here.
    component.selectedPokemonIds.set([25]);

    component.save();

    expect((questService.create.mock.calls[0][0] as QuestCreate).amount).toBe(0);
  });

  it('does nothing when no rewards are selected', () => {
    component.commonForm.controls.summary.setValue(true);
    component.save();
    expect(questService.create).not.toHaveBeenCalled();
  });
});
