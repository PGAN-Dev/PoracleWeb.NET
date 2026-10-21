import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { PokemonEditDialogComponent } from './pokemon-edit-dialog.component';
import { Monster, MonsterUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { I18nService } from '../../core/services/i18n.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MonsterService } from '../../core/services/monster.service';
import { PoracleConfigService } from '../../core/services/poracle-config.service';

/**
 * The edit dialog is where filters go to die: it is written second, drifts from the add dialog, and a
 * field it does not offer is a field the next save quietly clears. These cover the two filters
 * PoracleNG 5.1.0 added, including a time value the bot can set and the presets do not contain.
 */
describe('PokemonEditDialogComponent', () => {
  let webhookSession = false;
  let component: PokemonEditDialogComponent;
  let monsterService: { editSupported: jest.Mock; update: jest.Mock };

  let fixture: ComponentFixture<PokemonEditDialogComponent>;

  function setup(monster: Partial<Monster>) {
    monsterService = { editSupported: jest.fn().mockReturnValue(true), update: jest.fn().mockReturnValue(of({})) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: { uid: 7, maxIv: 100, minIv: 0, pokemonId: 52, ...monster } },
        { provide: MonsterService, useValue: monsterService },
        {
          provide: MasterDataService,
          useValue: {
            costumesAvailable: () => true,
            getCostumeName: () => 'Halloween 2025',
            getCostumes: () => [{ id: 85, name: 'Halloween 2025' }],
            getFormsForPokemon: () => [],
            getPokemonName: () => 'Meowth',
          },
        },
        { provide: I18nService, useValue: { instant: (k: string) => k } },
        {
          provide: PoracleConfigService,
          useValue: { load: () => of({ defaultPvpCap: 0 }), serverConfig: () => ({ pvpCaps: [] }) },
        },
        { provide: AuthService, useValue: { isImpersonating: () => webhookSession, user: () => ({ type: 'discord:user' }) } },
      ],
      imports: [PokemonEditDialogComponent],
    });

    TestBed.overrideComponent(PokemonEditDialogComponent, {
      add: { providers: [{ provide: MatSnackBar, useValue: { open: jest.fn() } }] },
    });

    fixture = TestBed.createComponent(PokemonEditDialogComponent);
    component = fixture.componentInstance;
  }

  function sent(): MonsterUpdate {
    return monsterService.update.mock.calls[0][1] as MonsterUpdate;
  }

  it('shows a min IV -1 alarm as the include-unscanned switch and keeps it', () => {
    setup({ minIv: -1 });
    component.ngOnInit();

    expect(component.form.controls.includeUnscanned.value).toBe(true);
    expect(component.form.controls.minIv.value).toBe(0);
    expect(component.form.controls.minIv.disabled).toBe(true);
    component.save();
    expect(sent().minIv).toBe(-1);
  });

  it('turning the switch off saves the Min IV box again', () => {
    setup({ minIv: -1 });
    component.ngOnInit();
    component.form.controls.includeUnscanned.setValue(false);
    component.form.controls.minIv.setValue(80);
    component.save();

    expect(sent().minIv).toBe(80);
  });

  it('notes that IV, CP and level filters will not match Minior', () => {
    setup({ pokemonId: 774 });

    expect(component.showMiniorNote).toBe(true);
  });

  it('has no Minior note for other Pokemon', () => {
    setup({ pokemonId: 52 });

    expect(component.showMiniorNote).toBe(false);
  });

  it('seeds the time filter from the alarm', () => {
    setup({ minTime: 300 });

    expect(component.form.controls.minTime.value).toBe(300);
  });

  it('keeps a time the bot set that is not one of the presets', () => {
    // A select that does not offer the stored value renders blank and clears it on the next save.
    setup({ minTime: 137 });

    expect(component.minTimeChoices()).toContain(137);

    component.save();

    expect(sent().minTime).toBe(137);
  });

  it('saves a changed time filter', () => {
    setup({ minTime: 300 });
    component.form.controls.minTime.setValue(600);

    component.save();

    expect(sent().minTime).toBe(600);
  });

  it('leaves an alarm with no time filter without one', () => {
    setup({});
    component.save();

    expect(sent().minTime).toBe(0);
  });

  // A rule stored before PoracleNG had costume columns reads back undefined. Seeding the control
  // with 0 instead of 9000 would rewrite it as "no costume" on the very next save. See #804.
  it('widens a costume-less rule to "any" rather than "none"', () => {
    setup({});

    expect(component.form.controls.costume.value).toBe(9000);

    component.save();

    expect(sent().costume).toBe(9000);
  });

  it('round-trips a costume it did not change', () => {
    setup({ costume: 85 });

    expect(component.form.controls.costume.value).toBe(85);

    component.save();

    expect(sent().costume).toBe(85);
  });

  it('saves a changed costume, including "no costume"', () => {
    setup({ costume: 85 });
    component.form.controls.costume.setValue(0);

    component.save();

    expect(sent().costume).toBe(0);
  });

  // PoracleNG 5.4.0 stores a costume on every rule, so the control renders with the standard mocks and
  // nothing telling the dialog the server supports it.
  it('renders the costume filter without any server capability', () => {
    setup({ costume: 85 });
    component.ngOnInit();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('POKEMON.LABEL_COSTUME');
  });

  it('keeps the mega mode of a PVP rule it did not change', () => {
    setup({ pvpRankingEvolution: 2, pvpRankingLeague: 1500 });
    component.save();

    expect(sent().pvpRankingEvolution).toBe(2);
  });

  // jfberry/PoracleNG#246: edit-in-place for pokemon, mirroring the lure dialog's bit-2 handling.
  describe('edit-in-place (clean bit 2)', () => {
    it('initializes edit-in-place on from bit 2 when clean=2', () => {
      setup({ clean: 2 });
      expect(component.form.controls.clean.value).toBe(false);
      expect(component.form.controls.editInPlace.value).toBe(true);
    });

    it('combines auto-delete + edit (clean 0 -> 3)', () => {
      setup({ clean: 0 });
      component.form.controls.clean.setValue(true);
      component.form.controls.editInPlace.setValue(true);
      component.save();
      expect(sent().clean).toBe(3);
    });

    it('preserves an unsurfaced summary bit when editing (clean 5 -> 7)', () => {
      // clean=5 => auto-delete (1) + summary (4); turning edit-in-place on must keep bit 4.
      setup({ clean: 5 });
      component.form.controls.editInPlace.setValue(true);
      component.save();
      expect(sent().clean).toBe(7);
    });

    it('clears bit 2 when toggled off (clean 3 -> 1)', () => {
      setup({ clean: 3 });
      component.form.controls.editInPlace.setValue(false);
      component.save();
      expect(sent().clean).toBe(1);
    });
  });

  describe('Mention field', () => {
    // The dialog's only condition for the Mention field is the impersonation snapshot: PoracleNG 5.4.0
    // always stores a ping, so there is no server-config dependency left to consult.
    it('is offered on a webhook alarm', () => {
      webhookSession = true;
      setup({ minIv: -1 });
      expect(TestBed.createComponent(PokemonEditDialogComponent).componentInstance.isWebhook).toBe(true);
    });

    it('is hidden on a personal alarm', () => {
      webhookSession = false;
      setup({ minIv: -1 });
      expect(TestBed.createComponent(PokemonEditDialogComponent).componentInstance.isWebhook).toBe(false);
    });
  });
});
