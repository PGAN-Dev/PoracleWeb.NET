import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { TranslateModule } from '@ngx-translate/core';
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
      imports: [QuestAddDialogComponent, TranslateModule.forRoot()],
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

  it('does nothing when no rewards are selected', () => {
    component.commonForm.controls.summary.setValue(true);
    component.save();
    expect(questService.create).not.toHaveBeenCalled();
  });
});
