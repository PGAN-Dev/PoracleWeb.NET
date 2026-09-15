import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { Observable, of, throwError } from 'rxjs';

import { PokestopEventListComponent } from './pokestop-event-list.component';
import { PokestopEvent } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { I18nService } from '../../core/services/i18n.service';
import { PokestopEventService } from '../../core/services/pokestop-event.service';

/**
 * The list is the only surface that names an event, so an event id this build has no entry for must
 * still draw a card — the row exists upstream and a blank card cannot be deleted by its owner.
 */
describe('PokestopEventListComponent', () => {
  let component: PokestopEventListComponent;
  let dialog: { open: jest.Mock };
  let fixture: ComponentFixture<PokestopEventListComponent>;
  let pokestopEventService: {
    delete: jest.Mock;
    deleteAll: jest.Mock;
    getAll: jest.Mock;
    update: jest.Mock;
    updateAllDistance: jest.Mock;
    updateBulkDistance: jest.Mock;
  };
  let snackBar: { open: jest.Mock };

  const SHOWCASE = 9;
  const KECLEON = 8;
  const GOLD_STOP = 7;

  const base: PokestopEvent = {
    id: 'u1',
    overrideAreas: null,
    overrideLocationLabel: null,
    uid: 1,
    clean: 0,
    displayType: SHOWCASE,
    distance: 0,
    eventName: 'showcase',
    profileNo: 0,
    template: null,
  };

  /** Whatever the dialog under test is told to return from `afterClosed()`. */
  function dialogReturns(value: unknown): void {
    dialog.open.mockReturnValue({ afterClosed: () => of(value) });
  }

  function setup(items: PokestopEvent[] | Observable<PokestopEvent[]>, areas: string[] = []): void {
    dialog = { open: jest.fn().mockReturnValue({ afterClosed: () => of(undefined) }) };
    snackBar = { open: jest.fn() };
    pokestopEventService = {
      delete: jest.fn().mockReturnValue(of(void 0)),
      deleteAll: jest.fn().mockReturnValue(of(void 0)),
      getAll: jest.fn().mockImplementation(() => (Array.isArray(items) ? of(items) : items)),
      update: jest.fn().mockReturnValue(of(void 0)),
      updateAllDistance: jest.fn().mockReturnValue(of(void 0)),
      updateBulkDistance: jest.fn().mockReturnValue(of(void 0)),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: I18nService, useValue: { instant: (k: string) => k } },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: AreaService, useValue: { getSelected: () => of(areas) } },
        { provide: PokestopEventService, useValue: pokestopEventService },
        { provide: AuthService, useValue: { isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
      ],
      imports: [PokestopEventListComponent],
    });

    // MatDialogModule and MatSnackBarModule are imported by the component, so their real providers
    // sit in its own injector and win over the root ones. The doubles have to be added there.
    TestBed.overrideComponent(PokestopEventListComponent, {
      add: {
        providers: [
          { provide: MatDialog, useValue: dialog },
          { provide: MatSnackBar, useValue: snackBar },
        ],
      },
    });

    fixture = TestBed.createComponent(PokestopEventListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function cardTitles(): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.alarm-card .item-info h3')).map(h => (h as HTMLElement).textContent!.trim());
  }

  describe('rendering', () => {
    it('draws one card per rule, named for its event', () => {
      setup([
        { ...base, uid: 1, displayType: SHOWCASE },
        { ...base, uid: 2, displayType: KECLEON, eventName: 'kecleon' },
        { ...base, uid: 3, displayType: GOLD_STOP, eventName: 'gold-stop' },
      ]);

      // Ordered by display type rather than by the order PoracleNG returned them in.
      expect(cardTitles()).toEqual(['INVASIONS.EVENT_TYPES.GOLD_STOP', 'INVASIONS.EVENT_TYPES.KECLEON', 'INVASIONS.EVENT_TYPES.SHOWCASE']);
    });

    it('keeps a rule where it was after an edit rotated its uid', () => {
      // This type has only ever had a v2 write surface, and a v2 replace is delete-then-insert, so the
      // edited rule comes back under the highest uid in the list. Rendering PoracleNG's own order threw
      // the card the user had just saved to the end of the grid.
      setup([
        // PoracleNG's own order: by uid, with the just-edited Kecleon rule re-keyed to the highest.
        { ...base, uid: 1, displayType: GOLD_STOP, eventName: 'gold-stop' },
        { ...base, uid: 3, displayType: SHOWCASE },
        { ...base, uid: 99, displayType: KECLEON, eventName: 'kecleon' },
      ]);

      expect(cardTitles()).toEqual(['INVASIONS.EVENT_TYPES.GOLD_STOP', 'INVASIONS.EVENT_TYPES.KECLEON', 'INVASIONS.EVENT_TYPES.SHOWCASE']);
    });

    it('shows the empty state, and no cards, when the profile tracks nothing', () => {
      setup([]);

      expect(fixture.nativeElement.querySelectorAll('.alarm-card')).toHaveLength(0);
      expect(fixture.nativeElement.querySelector('.empty-state')).toBeTruthy();
    });

    it('hides the empty state once a rule exists', () => {
      setup([base]);

      expect(fixture.nativeElement.querySelector('.empty-state')).toBeNull();
      expect(fixture.nativeElement.querySelectorAll('.alarm-card')).toHaveLength(1);
    });

    it('draws the spinner instead of the grid while loading', () => {
      // getAll never emits, so the component stays in its initial loading state.
      setup(new Observable<PokestopEvent[]>(() => undefined));

      expect(component.loading()).toBe(true);
      expect(fixture.nativeElement.querySelector('mat-spinner')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('.alarm-grid')).toBeNull();
    });

    it('stops loading when the list cannot be fetched', () => {
      setup(throwError(() => new Error('down')));

      expect(component.loading()).toBe(false);
      expect(component.events()).toEqual([]);
    });

    it('badges only the rules whose auto-delete bit is set', () => {
      setup([
        { ...base, uid: 1, clean: 0 },
        { ...base, uid: 2, clean: 1, displayType: KECLEON },
      ]);

      expect(fixture.nativeElement.querySelectorAll('.event-tag')).toHaveLength(1);
    });
  });

  describe('event identity', () => {
    it('names an event this build has no entry for by what PoracleNG stored', () => {
      setup([{ ...base, displayType: 99, eventName: 'something-new' }]);

      expect(cardTitles()).toEqual(['something-new']);
    });

    it('falls back to the raw id when even the stored name is missing', () => {
      setup([{ ...base, displayType: 99, eventName: null }]);

      expect(cardTitles()).toEqual(['99']);
    });

    it('gives an unknown event the fallback colour and icon rather than nothing', () => {
      setup([]);
      const unknown = { ...base, displayType: 99, eventName: null };

      expect(component.eventColor(unknown)).toBe('#03aeb6');
      expect(component.eventIcon(unknown)).toBe('celebration');
      expect(component.eventImage(unknown)).toBe('');
    });

    it('uses each known event’s own colour', () => {
      setup([]);

      expect(component.eventColor({ ...base, displayType: SHOWCASE })).toBe('#03AEB6');
      expect(component.eventColor({ ...base, displayType: KECLEON })).toBe('#B3CA78');
      expect(component.eventColor({ ...base, displayType: GOLD_STOP })).toBe('#F9E418');
    });
  });

  describe('clean bitmask', () => {
    it('reads bit 1 only, ignoring the edit and summary bits', () => {
      setup([]);

      expect(component.isAutoDelete(0)).toBe(false);
      expect(component.isAutoDelete(1)).toBe(true);
      expect(component.isAutoDelete(2)).toBe(false);
      expect(component.isAutoDelete(4)).toBe(false);
      expect(component.isAutoDelete(6)).toBe(false);
      expect(component.isAutoDelete(7)).toBe(true);
    });
  });

  describe('selection', () => {
    beforeEach(() =>
      setup([
        { ...base, uid: 1 },
        { ...base, uid: 2, displayType: KECLEON },
      ]),
    );

    it('toggles one uid on and off again', () => {
      component.toggleSelect(2);
      expect([...component.selectedIds()]).toEqual([2]);

      component.toggleSelect(2);
      expect(component.selectedIds().size).toBe(0);
    });

    it('selects every loaded rule', () => {
      component.selectAll();

      expect([...component.selectedIds()].sort()).toEqual([1, 2]);
    });

    it('clears the selection when select mode is turned back off', () => {
      component.toggleSelectMode();
      component.selectAll();
      component.toggleSelectMode();

      expect(component.selectMode()).toBe(false);
      expect(component.selectedIds().size).toBe(0);
    });
  });

  describe('bulk delete', () => {
    it('deletes every selected uid and reloads once', () => {
      setup([
        { ...base, uid: 1 },
        { ...base, uid: 2, displayType: KECLEON },
      ]);
      dialogReturns(true);
      component.selectAll();

      return component.bulkDelete().then(() => {
        expect(pokestopEventService.delete.mock.calls.map(c => c[0]).sort()).toEqual([1, 2]);
        expect(component.selectedIds().size).toBe(0);
        expect(pokestopEventService.getAll).toHaveBeenCalledTimes(2);
      });
    });

    it('keeps going past a uid that is already gone, and reports only what it deleted (#603)', async () => {
      setup([
        { ...base, uid: 1 },
        { ...base, uid: 2, displayType: KECLEON },
      ]);
      dialogReturns(true);
      pokestopEventService.delete.mockImplementation((uid: number) => (uid === 1 ? throwError(() => new Error('gone')) : of(void 0)));
      component.selectAll();

      await component.bulkDelete();

      expect(pokestopEventService.delete).toHaveBeenCalledTimes(2);
      expect(snackBar.open).toHaveBeenCalledWith('POKESTOP_EVENTS.SNACK_BULK_DELETED', 'COMMON.OK', expect.anything());
    });

    it('deletes nothing when the confirmation is dismissed', async () => {
      setup([base]);
      dialogReturns(false);
      component.selectAll();

      await component.bulkDelete();

      expect(pokestopEventService.delete).not.toHaveBeenCalled();
    });
  });

  describe('bulk distance', () => {
    it('sends the selected uids with the chosen radius', async () => {
      setup([
        { ...base, uid: 1 },
        { ...base, uid: 2, displayType: KECLEON },
      ]);
      dialogReturns(2000);
      component.selectAll();

      await component.bulkUpdateDistance();

      expect(pokestopEventService.updateBulkDistance).toHaveBeenCalledWith(expect.arrayContaining([1, 2]), 2000);
      expect(component.selectedIds().size).toBe(0);
    });

    it('surfaces the server’s own refusal and keeps the selection (#641)', async () => {
      setup([base]);
      dialogReturns(2000);
      pokestopEventService.updateBulkDistance.mockReturnValue(throwError(() => ({ error: { error: 'Distance too large' } })));
      component.selectAll();

      await component.bulkUpdateDistance();

      expect(snackBar.open).toHaveBeenCalledWith('Distance too large', 'TOAST.OK', expect.anything());
      expect(component.selectedIds().size).toBe(1);
      // No reload: nothing changed, so the list must not claim otherwise.
      expect(pokestopEventService.getAll).toHaveBeenCalledTimes(1);
    });

    it('does nothing when the radius dialog is cancelled', async () => {
      setup([base]);
      dialogReturns(undefined);
      component.selectAll();

      await component.bulkUpdateDistance();

      expect(pokestopEventService.updateBulkDistance).not.toHaveBeenCalled();
    });

    it('accepts zero as a radius rather than reading it as a cancel', async () => {
      setup([base]);
      dialogReturns(0);
      component.selectAll();

      await component.bulkUpdateDistance();

      expect(pokestopEventService.updateBulkDistance).toHaveBeenCalledWith([1], 0);
    });
  });

  describe('single-rule actions', () => {
    it('deletes the rule it was given, then reloads', () => {
      setup([base]);
      dialogReturns(true);

      component.deleteItem(base);

      expect(pokestopEventService.delete).toHaveBeenCalledWith(1);
      expect(pokestopEventService.getAll).toHaveBeenCalledTimes(2);
    });

    it('deletes nothing when the confirmation is dismissed', () => {
      setup([base]);
      dialogReturns(false);

      component.deleteItem(base);

      expect(pokestopEventService.delete).not.toHaveBeenCalled();
    });

    it('deletes them all when confirmed', () => {
      setup([base]);
      dialogReturns(true);

      component.deleteAll();

      expect(pokestopEventService.deleteAll).toHaveBeenCalled();
    });

    it('reloads after an edit that was saved, and not after one that was cancelled', () => {
      setup([base]);

      dialogReturns(true);
      component.editItem(base);
      expect(pokestopEventService.getAll).toHaveBeenCalledTimes(2);

      dialogReturns(false);
      component.editItem(base);
      expect(pokestopEventService.getAll).toHaveBeenCalledTimes(2);
    });

    it('reloads after the add dialog reports a write, and hands it the rules already tracked', () => {
      setup([base]);
      dialogReturns(true);

      component.openAddDialog();

      expect(dialog.open).toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ data: [base] }));
      expect(pokestopEventService.getAll).toHaveBeenCalledTimes(2);
    });
  });

  describe('scope editing from the card', () => {
    it('opens the sheet on the rule’s own scope, with the profile areas for wording', () => {
      setup([base], ['terrigal']);
      const areaRule = { ...base, overrideAreas: ['gosford'], distance: 0 };

      component.editScope(areaRule);

      expect(dialog.open).toHaveBeenCalledWith(
        expect.anything(),
        expect.objectContaining({ data: { profileAreas: ['terrigal'], scope: { areas: ['gosford'], mode: 'areas' } } }),
      );
    });

    it('writes the chosen scope back as the three stored fields', () => {
      setup([base]);
      dialogReturns({ distanceKm: 2, mode: 'place', placeLabel: 'work' });

      component.editScope(base);

      expect(pokestopEventService.update).toHaveBeenCalledWith(1, {
        overrideAreas: [],
        overrideLocationLabel: 'work',
        distance: 2000,
      });
    });

    it('writes nothing when the sheet is dismissed', () => {
      setup([base]);
      dialogReturns(undefined);

      component.editScope(base);

      expect(pokestopEventService.update).not.toHaveBeenCalled();
    });
  });
});
