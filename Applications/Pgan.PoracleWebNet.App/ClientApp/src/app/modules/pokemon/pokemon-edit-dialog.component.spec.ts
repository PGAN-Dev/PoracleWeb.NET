import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
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
import { SettingsService } from '../../core/services/settings.service';

/**
 * The edit dialog is where filters go to die: it is written second, drifts from the add dialog, and a
 * field it does not offer is a field the next save quietly clears. These cover the two filters
 * PoracleNG 5.1.0 added, including a time value the bot can set and the presets do not contain.
 */
describe('PokemonEditDialogComponent', () => {
  let component: PokemonEditDialogComponent;
  let monsterService: { update: jest.Mock };

  function setup(monster: Partial<Monster>, costumeSupported = true) {
    monsterService = { update: jest.fn().mockReturnValue(of({})) };

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
        { provide: SettingsService, useValue: { supportsCostume: () => costumeSupported } },
        {
          provide: PoracleConfigService,
          useValue: { load: () => of({ defaultPvpCap: 0 }), serverConfig: () => ({ pvpCaps: [] }) },
        },
        { provide: AuthService, useValue: { isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
      ],
      imports: [PokemonEditDialogComponent],
    });

    TestBed.overrideComponent(PokemonEditDialogComponent, {
      add: { providers: [{ provide: MatSnackBar, useValue: { open: jest.fn() } }] },
    });

    component = TestBed.createComponent(PokemonEditDialogComponent).componentInstance;
  }

  function sent(): MonsterUpdate {
    return monsterService.update.mock.calls[0][1] as MonsterUpdate;
  }

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

  /**
   * The gate. A Poracle without monsters.costume takes the field, answers 200 and drops it, so an
   * offered control would leave a rule that reads "Halloween 2025" and matches every spawn. Everything
   * else about the dialog is unchanged, and the wildcard still goes out -- which is what an old
   * Poracle stores for an absent key anyway.
   */
  it('hides the costume filter when Poracle does not have the column', () => {
    setup({}, false);

    expect(component.showCostume()).toBe(false);

    component.save();

    expect(sent().costume).toBe(9000);
  });

  it('offers the costume filter when Poracle has the column', () => {
    setup({ costume: 85 });

    expect(component.showCostume()).toBe(true);
  });

  it('keeps the mega mode of a PVP rule it did not change', () => {
    setup({ pvpRankingEvolution: 2, pvpRankingLeague: 1500 });
    component.save();

    expect(sent().pvpRankingEvolution).toBe(2);
  });
});
