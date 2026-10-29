import { readFileSync } from 'node:fs';
import { join } from 'node:path';

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

/**
 * Invasion and Pokéstop event rules are numbered separately, and this page lists both when the events page is off.
 * A delete that carries only the uid can reach the other kind's rule, so every delete names the kind of its card.
 * See #1062.
 */
describe('InvasionListComponent deletes', () => {
  let component: InvasionListComponent;
  let distanceChoice = 500;
  let dialog: { open: jest.Mock };
  let invasionService: { delete: jest.Mock; getAll: jest.Mock; updateBulkDistance: jest.Mock };
  let moves: { moveMany: jest.Mock };

  const grunt = { uid: 7, gruntType: 'water' } as Invasion;
  const event = { uid: 7, gruntType: 'kecleon' } as Invasion;

  function setup(items: Invasion[]): void {
    dialog = { open: jest.fn().mockReturnValue({ afterClosed: () => of(true) }) };
    distanceChoice = 500;
    invasionService = {
      delete: jest.fn().mockReturnValue(of(void 0)),
      getAll: jest.fn().mockReturnValue(of(items)),
      updateBulkDistance: jest.fn().mockReturnValue(of({ updated: 1 })),
    };
    moves = { moveMany: jest.fn().mockReturnValue(of({ results: [] })) };

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
        { provide: AlarmMoveService, useValue: moves },
        { provide: LinkedAccountService, useValue: { canChoose: signal(false) } },
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
    setup([grunt, event, { uid: 9, gruntType: 'fire' } as Invasion]);
    component.selectAll();

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

  describe('selection', () => {
    it('marks only the card that was toggled when an invasion and an event share a uid', () => {
      setup([grunt, event]);
      component.toggleSelect(event);
      expect(component.isSelected(event)).toBe(true);
      expect(component.isSelected(grunt)).toBe(false);
      expect(component.actionCards().map(c => c.uid)).toEqual([7]);
    });

    it('bulk delete removes only the selected card', async () => {
      setup([grunt, event]);
      component.toggleSelect(event);

      await component.bulkDelete();

      expect(invasionService.delete.mock.calls).toEqual([[7, 'event']]);
    });

    it('select all selects both cards', () => {
      setup([grunt, event]);
      component.selectAll();
      expect(component.isSelected(grunt)).toBe(true);
      expect(component.isSelected(event)).toBe(true);
      expect(component.actionIds()).toHaveLength(2);
    });

    it('leaves the Pok�mon list keyed by uid', () => {
      const read = (f: string): string => readFileSync(join(__dirname, f), 'utf8');
      expect(read('../pokemon/pokemon-list.component.html')).toContain('selectedIds().has(');
      expect(read('../pokemon/pokemon-list.component.html')).not.toContain('cardKey');
    });
  });

  describe('bulk Deliver to and bulk radius', () => {
    function pickDialogAnswer(): void {
      dialog.open.mockReturnValue({ afterClosed: () => of(distanceChoice) });
    }

    it('moves only the event when only the event is selected', async () => {
      setup([grunt, event]);
      component.toggleSelect(event);

      await component.bulkMove('telegram');

      expect(moves.moveMany.mock.calls).toEqual([['invasion', [7], 'telegram', 'event']]);
    });

    it('moves a mixed selection once per kind', async () => {
      setup([grunt, event]);
      component.selectAll();

      await component.bulkMove('telegram');

      expect(moves.moveMany.mock.calls).toEqual(
        expect.arrayContaining([
          ['invasion', [7], 'telegram', 'event'],
          ['invasion', [7], 'telegram', 'invasion'],
        ]),
      );
      expect(moves.moveMany).toHaveBeenCalledTimes(2);
    });

    it('changes the radius of only the invasion when only the invasion is selected', async () => {
      setup([grunt, event]);
      component.toggleSelect(grunt);
      pickDialogAnswer();

      await component.bulkUpdateDistance();

      expect(invasionService.updateBulkDistance.mock.calls).toEqual([[[7], 500, 'invasion']]);
    });

    it('changes the radius once per kind for a mixed selection', async () => {
      setup([grunt, event]);
      component.selectAll();
      pickDialogAnswer();

      await component.bulkUpdateDistance();

      expect(invasionService.updateBulkDistance.mock.calls).toEqual(
        expect.arrayContaining([
          [[7], 500, 'event'],
          [[7], 500, 'invasion'],
        ]),
      );
      expect(invasionService.updateBulkDistance).toHaveBeenCalledTimes(2);
    });
  });
});
