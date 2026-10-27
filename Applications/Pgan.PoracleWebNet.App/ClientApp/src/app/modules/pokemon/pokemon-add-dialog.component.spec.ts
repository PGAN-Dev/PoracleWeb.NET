import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

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
  let monsterService: { create: jest.Mock; createBatch: jest.Mock; editSupported: jest.Mock };
  let snackBar: { open: jest.Mock };
  let masterData: {
    costumesAvailable: jest.Mock;
    getAllPokemon$: jest.Mock;
    getAllTypes: jest.Mock;
    getTypeLabel: jest.Mock;
    getCostumeName: jest.Mock;
    getCostumes: jest.Mock;
    getFormsForPokemon: jest.Mock;
  };

  /** Meowth (52) with two non-Normal forms: Alolan + Galarian. */
  const MEOWTH = 52;
  const ALOLAN = 78;
  const GALARIAN = 79;

  let fixture: ComponentFixture<PokemonAddDialogComponent>;

  function setup(data: unknown = null) {
    dialogRef = { close: jest.fn() };
    // A create answers 200 with uid 0 when the submission duplicates an alarm the user already has, so
    // the uid is what says whether anything was made. See #495.
    let nextUid = 100;
    monsterService = {
      create: jest.fn().mockImplementation(() => of({ uid: nextUid++ } as Monster)),
      // Several rows go as one request (#1090), answered row for row.
      createBatch: jest
        .fn()
        .mockImplementation((rows: MonsterCreate[]) =>
          of({ alarms: rows.map(() => ({ uid: nextUid++ }) as Monster), takeoversSkipped: 0 }),
        ),
      editSupported: jest.fn().mockReturnValue(true),
    };
    snackBar = { open: jest.fn() };
    masterData = {
      costumesAvailable: jest.fn().mockReturnValue(true),
      getAllPokemon$: jest.fn().mockReturnValue(of([])),
      getAllTypes: jest.fn().mockReturnValue([]),
      getCostumeName: jest.fn().mockReturnValue('Halloween 2025'),
      getCostumes: jest.fn().mockReturnValue([{ id: 85, name: 'Halloween 2025' }]),
      getFormsForPokemon: jest.fn().mockReturnValue([
        { id: ALOLAN, name: 'Alolan' },
        { id: GALARIAN, name: 'Galarian' },
      ]),
      getTypeLabel: jest.fn().mockImplementation((t: string) => t),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MonsterService, useValue: monsterService },
        { provide: MasterDataService, useValue: masterData },
        { provide: I18nService, useValue: { instant: (k: string) => k, plural: (k: string) => k } },
        {
          provide: AlertDefaultsService,
          useValue: { defaultDistanceKm: () => 1, defaultMode: () => 'areas', defaultPlaceLabel: () => '' },
        },
        {
          provide: PoracleConfigService,
          useValue: { load: () => of({ defaultPvpCap: 0 }), serverConfig: () => ({ pvpCaps: [] }) },
        },
        { provide: AuthService, useValue: { isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
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
    fixture = TestBed.createComponent(PokemonAddDialogComponent);
    component = fixture.componentInstance;
  }

  /** Every row sent, whether alone or in a batch. */
  function sentRows(): MonsterCreate[] {
    return [
      ...monsterService.create.mock.calls.map(call => call[0] as MonsterCreate),
      ...monsterService.createBatch.mock.calls.flatMap(call => call[0] as MonsterCreate[]),
    ];
  }

  function createdForms(): number[] {
    return sentRows().map(row => row.form);
  }

  beforeEach(() => setup());

  // PoracleNG 5.4.0 stores a costume on every rule, so the control renders with the standard mocks and
  // nothing telling the dialog the server supports it.
  it('renders the costume filter on the Filters tab without any server capability', async () => {
    fixture.detectChanges();
    (fixture.nativeElement.querySelectorAll('.mat-mdc-tab')[1] as HTMLElement).click();
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 400));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('POKEMON.LABEL_COSTUME');
  });

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

    expect(sentRows().map(row => row.costume)).toEqual([85, 85]);
  });

  it('sends "no costume" as 0 rather than dropping it', () => {
    component.selectedPokemonIds.set([MEOWTH]);
    component.filtersForm.controls.costume.setValue(0);
    component.save();

    expect((monsterService.create.mock.calls[0][0] as MonsterCreate).costume).toBe(0);
  });

  describe('include Pokemon without stats', () => {
    const sentMinIv = (): number => (monsterService.create.mock.calls[0][0] as MonsterCreate).minIv;

    it('is off by default and the rule keeps its Min IV', () => {
      component.ngOnInit();
      component.selectedPokemonIds.set([MEOWTH]);
      component.filtersForm.controls.minIv.setValue(90);
      component.save();

      expect(component.filtersForm.controls.includeUnscanned.value).toBe(false);
      expect(sentMinIv()).toBe(90);
    });

    it('saves min IV -1 and disables the Min IV box while it is on', () => {
      component.ngOnInit();
      component.selectedPokemonIds.set([MEOWTH]);
      component.filtersForm.controls.minIv.setValue(90);
      component.filtersForm.controls.includeUnscanned.setValue(true);

      expect(component.filtersForm.controls.minIv.disabled).toBe(true);
      component.save();
      expect(sentMinIv()).toBe(-1);
    });
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

    expect(monsterService.create).not.toHaveBeenCalled();
    expect(monsterService.createBatch).toHaveBeenCalledTimes(1);
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

    expect(monsterService.createBatch).toHaveBeenCalledTimes(1);
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

    expect(snackBar.open).toHaveBeenCalledWith('ALARM.SNACK_CREATED', 'COMMON.OK', expect.objectContaining({ duration: 4000 }));
  });

  // Counting the submissions claimed every selected Pokemon was added while the list grew by fewer, or
  // by none at all. See #495.
  it('says how many submissions were already tracked', () => {
    monsterService.createBatch.mockReturnValueOnce(of({ alarms: [{ uid: 0 }, { uid: 101 }], takeoversSkipped: 0 }));
    component.selectedPokemonIds.set([MEOWTH]);
    component.filtersForm.controls.forms.setValue([ALOLAN, GALARIAN]);
    component.save();

    expect(snackBar.open).toHaveBeenCalledWith(
      'ALARM.SNACK_CREATED_WITH_DUPLICATES',
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

  // jfberry/PoracleNG#246: edit-in-place for pokemon, composed onto clean bit 2.
  describe('edit-in-place (clean bit 2)', () => {
    it('sends clean=0 when neither toggle is set', () => {
      component.selectedPokemonIds.set([MEOWTH]);
      component.save();

      expect((monsterService.create.mock.calls[0][0] as MonsterCreate).clean).toBe(0);
    });

    it('sends clean=2 when only edit-in-place is on', () => {
      component.notifForm.controls.editInPlace.setValue(true);
      component.selectedPokemonIds.set([MEOWTH]);
      component.save();

      expect((monsterService.create.mock.calls[0][0] as MonsterCreate).clean).toBe(2);
    });

    it('combines auto-delete + edit-in-place (clean=3)', () => {
      component.notifForm.controls.clean.setValue(true);
      component.notifForm.controls.editInPlace.setValue(true);
      component.selectedPokemonIds.set([MEOWTH]);
      component.save();

      expect((monsterService.create.mock.calls[0][0] as MonsterCreate).clean).toBe(3);
    });
  });

  // A single Add PoracleNG refuses created nothing: the dialog keeps the user's work and says why inline.
  // A multi-select keeps its old behaviour, because whatever landed is real and the list must reload.
  describe('a refused Add', () => {
    const takeover = {
      error: { existingUid: 77, error: 'Another alarm of this type already uses those settings.', trackingType: 'pokemon' },
    };

    it('keeps a single Add open with the reason inline and the selection intact', () => {
      monsterService.create.mockReturnValue(throwError(() => takeover));
      component.selectedPokemonIds.set([MEOWTH]);
      component.filtersForm.controls.forms.setValue([ALOLAN]);
      component.filtersForm.controls.minIv.setValue(90);
      component.save();

      expect(dialogRef.close).not.toHaveBeenCalled();
      expect(snackBar.open).not.toHaveBeenCalled();
      expect(component.refusal()?.message).toBe('Another alarm of this type already uses those settings.');
      expect(component.saving()).toBe(false);
      expect(component.selectedPokemonIds()).toEqual([MEOWTH]);
      expect(component.filtersForm.controls.minIv.value).toBe(90);
    });

    it('names the clashing rule when the list can describe it, and Edit that one hands it back', () => {
      const lookup = jest.fn().mockReturnValue('Snorlax · Within 2 km of my pin');
      setup({ existingRule: lookup });
      monsterService.create.mockReturnValue(throwError(() => takeover));
      component.selectedPokemonIds.set([MEOWTH]);
      component.filtersForm.controls.forms.setValue([ALOLAN]);
      component.save();

      expect(lookup).toHaveBeenCalledWith(77, 'pokemon');
      const existing = component.refusal()!.existing!;
      expect(existing.summary).toBe('Snorlax · Within 2 km of my pin');

      component.editExisting(existing);
      expect(dialogRef.close).toHaveBeenCalledWith({ editExisting: { uid: 77, trackingType: 'pokemon' } });
    });

    it('renders the refusal above the actions', () => {
      setup({ existingRule: () => 'Snorlax · Within 2 km of my pin' });
      monsterService.create.mockReturnValue(throwError(() => takeover));
      component.selectedPokemonIds.set([MEOWTH]);
      component.filtersForm.controls.forms.setValue([ALOLAN]);
      component.save();
      fixture.detectChanges();

      const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
      expect(alert?.textContent).toContain('Another alarm of this type already uses those settings.');
      expect(alert?.textContent).toContain('ALARM.REFUSAL_COVERED_BY');
      expect(alert?.querySelector('button')?.textContent).toContain('ALARM.REFUSAL_EDIT_EXISTING');
    });

    it('closes on success after a refusal, and clears it', () => {
      monsterService.create.mockReturnValueOnce(throwError(() => takeover)).mockReturnValueOnce(of({ uid: 501 } as Monster));
      component.selectedPokemonIds.set([MEOWTH]);
      component.filtersForm.controls.forms.setValue([ALOLAN]);
      component.save();
      expect(dialogRef.close).not.toHaveBeenCalled();

      component.save();
      expect(component.refusal()).toBeNull();
      expect(dialogRef.close).toHaveBeenCalledWith(true);
      expect(snackBar.open).toHaveBeenCalledWith('ALARM.SNACK_CREATED', 'COMMON.OK', expect.anything());
    });

    it('still closes a multi-select batch with a row left out as a takeover, toasting the reason', () => {
      monsterService.createBatch.mockReturnValueOnce(
        of({ alarms: [{ uid: 501 }, { uid: 0 }], takeoverReason: takeover.error.error, takeoversSkipped: 1 }),
      );
      component.selectedPokemonIds.set([MEOWTH]);
      component.filtersForm.controls.forms.setValue([ALOLAN, GALARIAN]);
      component.save();

      expect(component.refusal()).toBeNull();
      expect(snackBar.open).toHaveBeenCalledWith('Another alarm of this type already uses those settings.', 'COMMON.OK', expect.anything());
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('keeps a multi-select open when the batch itself is refused, since nothing was written', () => {
      monsterService.createBatch.mockReturnValueOnce(
        throwError(() => ({ error: { error: 'Max IV must be at least Min IV.' }, status: 400 })),
      );
      component.selectedPokemonIds.set([MEOWTH]);
      component.filtersForm.controls.forms.setValue([ALOLAN, GALARIAN]);
      component.save();

      expect(dialogRef.close).not.toHaveBeenCalled();
      expect(component.refusal()?.message).toBe('Max IV must be at least Min IV.');
      expect(component.saving()).toBe(false);
    });

    it('says a single Add already tracked exactly was left as it is', () => {
      monsterService.create.mockReturnValue(of({ uid: 0 } as Monster));
      component.selectedPokemonIds.set([MEOWTH]);
      component.filtersForm.controls.forms.setValue([ALOLAN]);
      component.save();

      expect(snackBar.open).toHaveBeenCalledWith('ALARM.ALREADY_TRACKED_ONE', 'COMMON.OK', expect.anything());
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });
  });
});
