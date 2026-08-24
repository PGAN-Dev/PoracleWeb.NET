import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { RaidEditDialogComponent, RaidEditDialogData } from './raid-edit-dialog.component';
import { Egg, EggUpdate, Raid, RaidUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { EggService } from '../../core/services/egg.service';
import { I18nService } from '../../core/services/i18n.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { RaidService } from '../../core/services/raid.service';

/** Costume is offered only where it can mean something: a raid rule with a specific boss. See #804. */
describe('RaidEditDialogComponent', () => {
  let component: RaidEditDialogComponent;
  let eggService: { update: jest.Mock };
  let raidService: { update: jest.Mock };

  const BASE_RAID: Raid = {
    id: 'u1',
    uid: 7,
    clean: 0,
    costume: 9000,
    distance: 1000,
    evolution: 9000,
    exclusive: 0,
    form: 0,
    gymId: '',
    level: 9000,
    move: 9000,
    pokemonId: 150,
    profileNo: 1,
    rsvpChanges: 0,
    team: 4,
    template: null,
  };

  function setup(data: Partial<RaidEditDialogData> & { item: Egg | Raid }) {
    raidService = { update: jest.fn().mockReturnValue(of({})) };
    eggService = { update: jest.fn().mockReturnValue(of({})) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: { type: 'raid', ...data } },
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
        { provide: AuthService, useValue: { isImpersonating: () => false } },
      ],
      imports: [RaidEditDialogComponent],
    });

    TestBed.overrideComponent(RaidEditDialogComponent, {
      add: { providers: [{ provide: MatSnackBar, useValue: { open: jest.fn() } }] },
    });

    component = TestBed.createComponent(RaidEditDialogComponent).componentInstance;
  }

  function sentRaid(): RaidUpdate {
    return raidService.update.mock.calls[0][1] as RaidUpdate;
  }

  it('offers the costume filter on a rule with a specific boss', () => {
    setup({ item: { ...BASE_RAID, costume: 85 } });

    expect(component.showCostume()).toBe(true);
    expect(component.form.controls.costume.value).toBe(85);

    component.save();

    expect(sentRaid().costume).toBe(85);
  });

  it('saves a changed costume, including "no costume"', () => {
    setup({ item: { ...BASE_RAID, costume: 85 } });
    component.form.controls.costume.setValue(0);

    component.save();

    expect(sentRaid().costume).toBe(0);
  });

  // A level rule has no boss. Sending a costume it never chose would narrow it; omitting the key
  // lets the backend's null-skip merge leave whatever is stored alone.
  it('omits costume entirely from a level rule', () => {
    setup({ item: { ...BASE_RAID, level: 5, pokemonId: 9000 } });

    expect(component.showCostume()).toBe(false);

    component.save();

    expect(sentRaid()).not.toHaveProperty('costume');
  });

  it('never puts a costume on an egg', () => {
    const egg: Egg = {
      id: 'u1',
      uid: 9,
      clean: 0,
      distance: 1000,
      exclusive: 0,
      gymId: '',
      level: 5,
      profileNo: 1,
      rsvpChanges: 0,
      team: 4,
      template: null,
    };
    setup({ item: egg, type: 'egg' });

    expect(component.showCostume()).toBe(false);

    component.save();

    expect(eggService.update.mock.calls[0][1] as EggUpdate).not.toHaveProperty('costume');
  });

  // A raid stored before PoracleNG grew the column reads back undefined; it must widen to "any".
  it('widens a costume-less raid rule to "any" rather than "none"', () => {
    const legacy = { ...BASE_RAID } as Partial<Raid>;
    delete legacy.costume;
    setup({ item: legacy as Raid });

    expect(component.form.controls.costume.value).toBe(9000);

    component.save();

    expect(sentRaid().costume).toBe(9000);
  });
});
