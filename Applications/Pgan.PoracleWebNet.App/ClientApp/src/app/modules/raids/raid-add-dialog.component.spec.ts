import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { RaidAddDialogComponent } from './raid-add-dialog.component';
import { EggCreate, Raid, RaidCreate } from '../../core/models';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { EggService } from '../../core/services/egg.service';
import { I18nService } from '../../core/services/i18n.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { RaidService } from '../../core/services/raid.service';
import { SettingsService } from '../../core/services/settings.service';

/**
 * The costume filter belongs to the By Boss tab only. A level rule matches whatever hatches, but
 * PoracleNG stores costume on it all the same -- so if the by-level path ever stopped sending the
 * 9000 wildcard, every level alarm would quietly become "plain bosses only". See #804.
 */
describe('RaidAddDialogComponent', () => {
  let component: RaidAddDialogComponent;
  let eggService: { create: jest.Mock };
  let raidService: { create: jest.Mock };

  function setup(costumeSupported = true) {
    let nextUid = 100;
    raidService = { create: jest.fn().mockImplementation(() => of({ uid: nextUid++ } as Raid)) };
    eggService = { create: jest.fn().mockImplementation(() => of({ uid: nextUid++ })) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: RaidService, useValue: raidService },
        { provide: EggService, useValue: eggService },
        {
          provide: MasterDataService,
          useValue: {
            costumesAvailable: () => true,
            getCostumes: () => [{ id: 85, name: 'Halloween 2025' }],
          },
        },
        { provide: I18nService, useValue: { instant: (k: string) => k } },
        { provide: SettingsService, useValue: { supportsCostume: () => costumeSupported } },
        {
          provide: AlertDefaultsService,
          useValue: { defaultDistanceKm: () => 1, defaultMode: () => 'areas', defaultPlaceLabel: () => '' },
        },
        { provide: AuthService, useValue: { isImpersonating: () => false } },
      ],
      imports: [RaidAddDialogComponent],
    });

    TestBed.overrideComponent(RaidAddDialogComponent, {
      add: { providers: [{ provide: MatSnackBar, useValue: { open: jest.fn() } }] },
    });

    component = TestBed.createComponent(RaidAddDialogComponent).componentInstance;
  }

  beforeEach(() => setup());

  it('defaults the costume filter to "any", not "none"', () => {
    expect(component.commonForm.controls.costume.value).toBe(9000);
  });

  it('sends the chosen costume on a By Boss rule', () => {
    component.tabIndex = 1;
    component.selectedPokemonIds.set([150]);
    component.commonForm.controls.costume.setValue(85);
    component.save();

    expect((raidService.create.mock.calls[0][0] as RaidCreate).costume).toBe(85);
  });

  it('sends "any costume" on a By Level rule even when the boss tab holds a costume', () => {
    // The control is not rendered on this tab, but its value survives a tab switch. A level rule
    // must not inherit it.
    component.commonForm.controls.costume.setValue(85);
    component.tabIndex = 0;
    component.selectedRaidLevels.set([5]);
    component.save();

    expect((raidService.create.mock.calls[0][0] as RaidCreate).costume).toBe(9000);
  });

  // The egg table has no costume column at all, so the payload must not grow one.
  it('never puts a costume on an egg', () => {
    component.tabIndex = 0;
    component.selectedEggLevels.set([5]);
    component.save();

    expect(eggService.create.mock.calls[0][0] as EggCreate).not.toHaveProperty('costume');
  });

  it('names the hint after the current selection', () => {
    expect(component.costumeHint()).toBe('POKEMON.COSTUME_HINT_ANY');
    component.commonForm.controls.costume.setValue(0);
    expect(component.costumeHint()).toBe('POKEMON.COSTUME_HINT_NONE');
    component.commonForm.controls.costume.setValue(85);
    expect(component.costumeHint()).toBe('POKEMON.COSTUME_HINT_SPECIFIC');
  });

  /**
   * The gate. A Poracle without the raid.costume column takes the field, answers 200 and drops it, so an
   * offered control produces a rule that reads "Halloween 2025" and matches every spawn. The rest of
   * the dialog is untouched -- only this one control goes.
   */
  describe('server capability', () => {
    it('offers the costume filter when Poracle has the column', () => {
      expect(component.showCostume()).toBe(true);
    });

    it('hides the costume filter when Poracle does not', () => {
      setup(false);

      expect(component.showCostume()).toBe(false);
      // The wildcard still goes out, which is what an old Poracle stores for an absent key anyway.
      component.tabIndex = 1;
      component.selectedPokemonIds.set([150]);
      component.save();
      expect((raidService.create.mock.calls[0][0] as RaidCreate).costume).toBe(9000);
    });
  });
});
