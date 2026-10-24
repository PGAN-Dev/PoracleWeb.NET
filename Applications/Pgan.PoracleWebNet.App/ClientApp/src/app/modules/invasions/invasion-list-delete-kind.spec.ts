import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { InvasionListComponent } from './invasion-list.component';
import { Invasion } from '../../core/models';
import { AlarmMoveService } from '../../core/services/alarm-move.service';
import { AreaNameService } from '../../core/services/area-name.service';
import { AreaService } from '../../core/services/area.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { InvasionService } from '../../core/services/invasion.service';
import { LinkedAccountService } from '../../core/services/linked-account.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { TestAlertService } from '../../core/services/test-alert.service';

/**
 * Invasion and Pokéstop event rules are numbered separately, and this page lists both when the events page is off.
 * A delete that carries only the uid can reach the other kind's rule, so every delete names the kind of its card.
 * See #1062.
 */
describe('InvasionListComponent deletes', () => {
  let component: InvasionListComponent;
  let dialog: { open: jest.Mock };
  let invasionService: { delete: jest.Mock; getAll: jest.Mock };

  const grunt = { gruntType: 'water', uid: 7 } as Invasion;
  const event = { gruntType: 'kecleon', uid: 7 } as Invasion;

  function setup(items: Invasion[]): void {
    dialog = { open: jest.fn().mockReturnValue({ afterClosed: () => of(true) }) };
    invasionService = { delete: jest.fn().mockReturnValue(of(void 0)), getAll: jest.fn().mockReturnValue(of(items)) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: I18nService, useValue: { instant: (k: string) => k, plural: (k: string) => k } },
        { provide: AreaService, useValue: { getSelected: () => of([]) } },
        { provide: AreaNameService, useValue: {} },
        { provide: IconService, useValue: {} },
        { provide: InvasionService, useValue: invasionService },
        {
          provide: MasterDataService,
          useValue: { getGruntName: () => '', isGruntNameUntranslated: () => false, loadData: () => of(void 0) },
        },
        { provide: AlarmMoveService, useValue: {} },
        { provide: LinkedAccountService, useValue: { canChoose: signal(false) } },
        { provide: TestAlertService, useValue: {} },
      ],
      imports: [InvasionListComponent],
    });
    TestBed.overrideComponent(InvasionListComponent, {
      remove: { imports: [MatDialogModule, MatSnackBarModule] },
    });
    TestBed.overrideComponent(InvasionListComponent, {
      set: {
        providers: [
          { provide: MatDialog, useValue: dialog },
          { provide: MatSnackBar, useValue: { open: jest.fn() } },
        ],
        template: '',
      },
    });

    const fixture = TestBed.createComponent(InvasionListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('sends "invasion" when a grunt card is deleted', () => {
    setup([grunt, event]);
    component.deleteInvasion(grunt);
    expect(invasionService.delete).toHaveBeenCalledWith(7, 'invasion');
  });

  it('sends "event" when a Pokéstop event card is deleted', () => {
    setup([grunt, event]);
    component.deleteInvasion(event);
    expect(invasionService.delete).toHaveBeenCalledWith(7, 'event');
  });

  it('deletes each selected card on the route of its own kind', async () => {
    setup([grunt, event, { gruntType: 'fire', uid: 9 } as Invasion]);
    component.selectedIds.set(new Set([7, 9]));

    await component.bulkDelete();

    expect(invasionService.delete.mock.calls).toEqual(
      expect.arrayContaining([
        [7, 'invasion'],
        [7, 'event'],
        [9, 'invasion'],
      ]),
    );
    expect(invasionService.delete).toHaveBeenCalledTimes(3);
  });
});
