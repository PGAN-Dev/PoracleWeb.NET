import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { NO_ERRORS_SCHEMA, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { PokemonEditDialogComponent } from './pokemon-edit-dialog.component';
import { Monster, MonsterUpdate, PlayerAccount } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { I18nService } from '../../core/services/i18n.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MonsterService } from '../../core/services/monster.service';
import { PlayerAccountService } from '../../core/services/player-account.service';
import { PoracleConfigService } from '../../core/services/poracle-config.service';
import { SettingsService } from '../../core/services/settings.service';
import { PersonalFilterPanelComponent } from '../../shared/components/personal-filter-panel/personal-filter-panel.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

/**
 * The edit dialog is where filters go to die: it is written second, drifts from the add dialog, and a
 * field it does not offer is a field the next save quietly clears. These cover the two filters
 * PoracleNG 5.1.0 added, including a time value the bot can set and the presets do not contain.
 */
describe('PokemonEditDialogComponent', () => {
  let component: PokemonEditDialogComponent;
  let monsterService: { editSupported: jest.Mock; update: jest.Mock };

  let accountsSignal: ReturnType<typeof signal<null | PlayerAccount[]>>;
  let playerAccounts: { accounts: typeof accountsSignal; load: jest.Mock };

  function setup(monster: Partial<Monster>, costumeSupported = true, personalSupported = false, accounts: null | PlayerAccount[] = []) {
    accountsSignal = signal<null | PlayerAccount[]>(accounts);
    playerAccounts = { accounts: accountsSignal, load: jest.fn().mockReturnValue(of(accounts)) };
    monsterService = { editSupported: jest.fn().mockReturnValue(true), update: jest.fn().mockReturnValue(of({})) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
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
          provide: SettingsService,
          useValue: { siteSettings: () => ({}), supportsCostume: () => costumeSupported, supportUrl: () => null },
        },
        {
          provide: PoracleConfigService,
          useValue: { load: () => of({ defaultPvpCap: 0 }), serverConfig: () => ({ personalSupported, pvpCaps: [] }) },
        },
        { provide: PlayerAccountService, useValue: playerAccounts },
        { provide: AuthService, useValue: { isAdmin: () => false, isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
      ],
      imports: [PokemonEditDialogComponent],
    });

    TestBed.overrideComponent(PokemonEditDialogComponent, {
      add: { providers: [{ provide: MatSnackBar, useValue: { open: jest.fn() } }], schemas: [NO_ERRORS_SCHEMA] },
      // Rendering the Filters tab must not drag in the pickers, which have services of their own.
      remove: { imports: [ScopePickerComponent, TemplateSelectorComponent] },
    });

    component = TestBed.createComponent(PokemonEditDialogComponent).componentInstance;
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

  describe('personal shiny toggles (personal bitmask)', () => {
    function render(): HTMLElement {
      const fixture = TestBed.createComponent(PokemonEditDialogComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    it('hides the personal toggles without server support but keeps the controls', () => {
      setup({ personal: 3 }, true, false);
      const el = render();

      expect(el.textContent).not.toContain('POKEMON.PERSONAL_SHINY');
      expect(component.form.controls.personalShiny.value).toBe(true);
      expect(component.form.controls.personalBackground.value).toBe(true);
      // A hidden toggle still round-trips what the rule holds.
      component.save();
      expect(sent().personal).toBe(3);
    });

    it('shows the personal toggles when the server supports them', () => {
      setup({ personal: 0 }, true, true);
      const el = render();

      expect(el.textContent).toContain('POKEMON.PERSONAL_SHINY');
      expect(el.textContent).toContain('POKEMON.PERSONAL_BACKGROUND');
    });

    it('seeds the toggles from the stored bits', () => {
      setup({ personal: 2 }, true, true);

      expect(component.form.controls.personalShiny.value).toBe(false);
      expect(component.form.controls.personalBackground.value).toBe(true);
    });

    it('treats a rule without the field as no personal bits', () => {
      setup({}, true, true);
      component.save();

      expect(sent().personal).toBe(0);
    });

    it('preserves reserved personal bits on save', () => {
      // personal 5 = shiny (1) + reserved (4). An untouched save must send 5 back.
      setup({ personal: 5 }, true, true);
      component.save();
      expect(sent().personal).toBe(5);
    });

    it('turning shiny off keeps the reserved bit (5 -> 4)', () => {
      setup({ personal: 5 }, true, true);
      component.form.controls.personalShiny.setValue(false);
      component.save();

      expect(sent().personal).toBe(4);
    });

    it('turning the background on adds only bit 2 (5 -> 7)', () => {
      setup({ personal: 5 }, true, true);
      component.form.controls.personalBackground.setValue(true);
      component.save();

      expect(sent().personal).toBe(7);
    });

    it('puts the personal panel first in the Filters tab, bound to the controls the dialog owns', () => {
      // The panel owns the account status; the dialog owns the controls, so a stored bit round-trips.
      setup({ personal: 1 }, true, true, []);
      const fixture = TestBed.createComponent(PokemonEditDialogComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
      const panel = fixture.debugElement.query(By.directive(PersonalFilterPanelComponent));

      expect(panel).not.toBeNull();
      expect(panel.componentInstance.shiny()).toBe(component.form.controls.personalShiny);
      expect(panel.componentInstance.background()).toBe(component.form.controls.personalBackground);
      expect((panel.nativeElement as HTMLElement).closest('.tab-content')?.firstElementChild).toBe(panel.nativeElement);
    });
  });
});
