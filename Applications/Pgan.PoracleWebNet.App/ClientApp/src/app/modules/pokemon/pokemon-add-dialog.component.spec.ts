import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { PokemonAddDialogComponent } from './pokemon-add-dialog.component';
import { Monster, MonsterCreate } from '../../core/models';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { I18nService } from '../../core/services/i18n.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MonsterService } from '../../core/services/monster.service';
import { PoracleConfigService } from '../../core/services/poracle-config.service';

describe('PokemonAddDialogComponent', () => {
  let component: PokemonAddDialogComponent;
  let dialogRef: { close: jest.Mock };
  let monsterService: { create: jest.Mock };
  let snackBar: { open: jest.Mock };
  let masterData: { getFormsForPokemon: jest.Mock };

  /** Meowth (52) with two non-Normal forms: Alolan + Galarian. */
  const MEOWTH = 52;
  const ALOLAN = 78;
  const GALARIAN = 79;

  function setup() {
    dialogRef = { close: jest.fn() };
    // A create answers 200 with uid 0 when the submission duplicates an alarm the user already has, so
    // the uid is what says whether anything was made. See #495.
    let nextUid = 100;
    monsterService = { create: jest.fn().mockImplementation(() => of({ uid: nextUid++ } as Monster)) };
    snackBar = { open: jest.fn() };
    masterData = {
      getFormsForPokemon: jest.fn().mockReturnValue([
        { id: ALOLAN, name: 'Alolan' },
        { id: GALARIAN, name: 'Galarian' },
      ]),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MonsterService, useValue: monsterService },
        { provide: MasterDataService, useValue: masterData },
        { provide: I18nService, useValue: { instant: (k: string) => k } },
        { provide: AlertDefaultsService, useValue: { defaultDistanceKm: () => 1, defaultMode: () => 'areas' } },
        {
          provide: PoracleConfigService,
          useValue: { load: () => of({ defaultPvpCap: 0 }), serverConfig: () => ({ pvpCaps: [] }) },
        },
        { provide: AuthService, useValue: { isImpersonating: () => false } },
      ],
      imports: [PokemonAddDialogComponent],
    });

    // MatSnackBar is providedIn MatSnackBarModule, which the standalone component imports, so
    // it resolves from the component's element injector and shadows an environment-level
    // useValue. Override at the component level to inject our mock.
    TestBed.overrideComponent(PokemonAddDialogComponent, {
      add: { providers: [{ provide: MatSnackBar, useValue: snackBar }] },
    });

    // No detectChanges(): we exercise save() logic directly and skip rendering the heavy
    // app-pokemon-selector child. Computed signals (availableForms) evaluate lazily on read.
    const fixture = TestBed.createComponent(PokemonAddDialogComponent);
    component = fixture.componentInstance;
  }

  function createdForms(): number[] {
    return monsterService.create.mock.calls.map(call => (call[0] as MonsterCreate).form);
  }

  beforeEach(() => setup());

  it('defaults the multi-select forms control to empty', () => {
    expect(component.filtersForm.controls.forms.value).toEqual([]);
  });

  it('creates one alarm per selected form (multi-select fan-out)', () => {
    component.selectedPokemonIds.set([MEOWTH]);
    component.filtersForm.controls.forms.setValue([ALOLAN, GALARIAN]);
    component.save();

    expect(monsterService.create).toHaveBeenCalledTimes(2);
    expect(createdForms()).toEqual([ALOLAN, GALARIAN]);
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('treats an empty form selection as all forms (form 0)', () => {
    component.selectedPokemonIds.set([MEOWTH]);
    component.filtersForm.controls.forms.setValue([]);
    component.save();

    expect(monsterService.create).toHaveBeenCalledTimes(1);
    expect(createdForms()).toEqual([0]);
  });

  it('fans out the cartesian product of pokemon x forms', () => {
    component.selectedPokemonIds.set([MEOWTH, MEOWTH + 1]);
    // Two pokemon selected => no specific forms list is available, so the multi-select
    // is hidden and an empty selection means "all forms" for each pokemon.
    component.save();

    expect(monsterService.create).toHaveBeenCalledTimes(2);
    expect(createdForms()).toEqual([0, 0]);
  });

  it('falls back to the manual numeric form id when no form list is available', () => {
    // Two pokemon => availableForms() is empty => the numeric `form` control is used.
    component.selectedPokemonIds.set([MEOWTH, MEOWTH + 1]);
    component.filtersForm.controls.form.setValue(42);
    component.save();

    expect(createdForms()).toEqual([42, 42]);
  });

  it('reports how many alarms were actually created', () => {
    component.selectedPokemonIds.set([MEOWTH]);
    component.filtersForm.controls.forms.setValue([ALOLAN, GALARIAN]);
    component.save();

    expect(snackBar.open).toHaveBeenCalledWith('POKEMON.SNACK_CREATED', 'COMMON.OK', expect.objectContaining({ duration: 4000 }));
  });

  // Counting the submissions claimed every selected Pokemon was added while the list grew by fewer, or
  // by none at all. See #495.
  it('says how many submissions were already tracked', () => {
    monsterService.create.mockReturnValueOnce(of({ uid: 0 } as Monster));
    component.selectedPokemonIds.set([MEOWTH]);
    component.filtersForm.controls.forms.setValue([ALOLAN, GALARIAN]);
    component.save();

    expect(snackBar.open).toHaveBeenCalledWith(
      'POKEMON.SNACK_CREATED_WITH_DUPLICATES',
      'COMMON.OK',
      expect.objectContaining({ duration: 4000 }),
    );
  });

  it('does nothing when no pokemon are selected', () => {
    component.filtersForm.controls.forms.setValue([ALOLAN]);
    component.save();
    expect(monsterService.create).not.toHaveBeenCalled();
  });
});
