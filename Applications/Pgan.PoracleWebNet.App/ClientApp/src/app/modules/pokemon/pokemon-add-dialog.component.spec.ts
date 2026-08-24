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
  let masterData: { costumesAvailable: jest.Mock; getCostumeName: jest.Mock; getCostumes: jest.Mock; getFormsForPokemon: jest.Mock };

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
      costumesAvailable: jest.fn().mockReturnValue(true),
      getCostumeName: jest.fn().mockReturnValue('Halloween 2025'),
      getCostumes: jest.fn().mockReturnValue([{ id: 85, name: 'Halloween 2025' }]),
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
        {
          provide: AlertDefaultsService,
          useValue: { defaultDistanceKm: () => 1, defaultMode: () => 'areas', defaultPlaceLabel: () => '' },
        },
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

  // 9000 is "any costume". A control that defaulted to 0 would ship "no costume" on every new
  // alarm and quietly stop it firing on every costumed event. See #804.
  it('defaults the costume filter to "any", not "none"', () => {
    expect(component.filtersForm.controls.costume.value).toBe(9000);

    component.selectedPokemonIds.set([MEOWTH]);
    component.save();

    expect((monsterService.create.mock.calls[0][0] as MonsterCreate).costume).toBe(9000);
  });

  it('sends the chosen costume on every form the selection fans out to', () => {
    component.selectedPokemonIds.set([MEOWTH]);
    component.filtersForm.controls.forms.setValue([ALOLAN, GALARIAN]);
    component.filtersForm.controls.costume.setValue(85);
    component.save();

    expect(monsterService.create.mock.calls.map(c => (c[0] as MonsterCreate).costume)).toEqual([85, 85]);
  });

  it('sends "no costume" as 0 rather than dropping it', () => {
    component.selectedPokemonIds.set([MEOWTH]);
    component.filtersForm.controls.costume.setValue(0);
    component.save();

    expect((monsterService.create.mock.calls[0][0] as MonsterCreate).costume).toBe(0);
  });

  it('names the hint after the current selection', () => {
    expect(component.costumeHint()).toBe('POKEMON.COSTUME_HINT_ANY');
    component.filtersForm.controls.costume.setValue(0);
    expect(component.costumeHint()).toBe('POKEMON.COSTUME_HINT_NONE');
    component.filtersForm.controls.costume.setValue(85);
    expect(component.costumeHint()).toBe('POKEMON.COSTUME_HINT_SPECIFIC');
  });

  it('says so when the costume names failed to load', () => {
    masterData.costumesAvailable.mockReturnValue(false);

    expect(component.costumeHint()).toBe('POKEMON.COSTUME_HINT_UNAVAILABLE');
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

  it('sends the mega mode with a PVP rule', () => {
    component.selectedPokemonIds.set([MEOWTH]);
    component.pvpForm.controls.pvpRankingLeague.setValue(1500);
    component.pvpForm.controls.pvpRankingEvolution.setValue(2);

    component.save();

    expect((monsterService.create.mock.calls[0][0] as MonsterCreate).pvpRankingEvolution).toBe(2);
  });

  it('does not send a mega mode on a rule with no league', () => {
    // Mega mode only means something inside a PVP rule. Carrying it on a non-PVP alarm would be a
    // filter the user never asked for, on a field PoracleNG still reads.
    component.selectedPokemonIds.set([MEOWTH]);
    component.pvpForm.controls.pvpRankingEvolution.setValue(2);

    component.save();

    expect((monsterService.create.mock.calls[0][0] as MonsterCreate).pvpRankingEvolution).toBe(0);
  });

  it('sends the minimum time left with the rule', () => {
    component.selectedPokemonIds.set([MEOWTH]);
    component.filtersForm.controls.minTime.setValue(300);

    component.save();

    expect((monsterService.create.mock.calls[0][0] as MonsterCreate).minTime).toBe(300);
  });

  it('sends no time floor by default', () => {
    // The legitimate twin: an ordinary rule must not arrive with a filter nobody chose.
    component.selectedPokemonIds.set([MEOWTH]);

    component.save();

    expect((monsterService.create.mock.calls[0][0] as MonsterCreate).minTime).toBe(0);
  });

  it('offers the presets for the time left', () => {
    expect(component.minTimeChoices()).toEqual([0, 60, 120, 300, 600, 900, 1200]);
  });

  it('sends the scope fields for an alarm aimed at a saved place', () => {
    component.selectedPokemonIds.set([MEOWTH]);
    component.scope.set({ distanceKm: 2, mode: 'place', placeLabel: 'work' });

    component.save();

    const created = monsterService.create.mock.calls[0][0] as MonsterCreate;
    expect(created.overrideLocationLabel).toBe('work');
    expect(created.distance).toBe(2000);
    expect(created.overrideAreas).toEqual([]);
  });

  it('leaves the radius on the pin when no place is chosen', () => {
    // The legitimate-case half: "within 2 km of me" is the alarm most people make, and it must not
    // acquire a location override just because the field exists.
    component.selectedPokemonIds.set([MEOWTH]);
    component.scope.set({ distanceKm: 2, mode: 'profile' });

    component.save();

    const created = monsterService.create.mock.calls[0][0] as MonsterCreate;
    expect(created.overrideLocationLabel).toBe('');
    expect(created.distance).toBe(2000);
  });

  it('clears the radius and any place when the alarm inherits the profile', () => {
    component.selectedPokemonIds.set([MEOWTH]);
    component.scope.set({ mode: 'profile' });

    component.save();

    const created = monsterService.create.mock.calls[0][0] as MonsterCreate;
    expect(created.distance).toBe(0);
    expect(created.overrideLocationLabel).toBe('');
  });

  it('does nothing when no pokemon are selected', () => {
    component.filtersForm.controls.forms.setValue([ALOLAN]);
    component.save();
    expect(monsterService.create).not.toHaveBeenCalled();
  });
});
