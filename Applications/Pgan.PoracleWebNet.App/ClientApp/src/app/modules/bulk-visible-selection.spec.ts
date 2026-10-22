import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Type, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { GymListComponent } from './gyms/gym-list.component';
import { InvasionListComponent } from './invasions/invasion-list.component';
import { LureListComponent } from './lures/lure-list.component';
import { RaidListComponent } from './raids/raid-list.component';
import { AreaService } from '../core/services/area.service';
import { AuthService } from '../core/services/auth.service';
import { ConfigService } from '../core/services/config.service';
import { EggService } from '../core/services/egg.service';
import { GymService } from '../core/services/gym.service';
import { I18nService } from '../core/services/i18n.service';
import { InvasionService } from '../core/services/invasion.service';
import { LureService } from '../core/services/lure.service';
import { RaidService } from '../core/services/raid.service';
import { ScannerService } from '../core/services/scanner.service';

/**
 * A search can hide selected cards, and a raid-list tab hides the other tab's. Bulk Delete and Change
 * radius act only on the selected cards on screen, and the selection itself survives, so the hidden
 * ticks come back with the cards. Driven through the components, not read from their source.
 */
const common = { id: '1', overrideAreas: null, overrideLocationLabel: null, clean: 0, distance: 0, ping: '', profileNo: 1, template: '1' };

function alarmService(rows: unknown[]) {
  return {
    delete: jest.fn().mockReturnValue(of(void 0)),
    deleteAll: jest.fn().mockReturnValue(of(void 0)),
    getAll: jest.fn().mockReturnValue(of(rows)),
    update: jest.fn().mockReturnValue(of(void 0)),
    updateBulkDistance: jest.fn().mockReturnValue(of({ skippedAreaScoped: [], skippedConflict: [], skippedPlaceScoped: [], updated: 1 })),
  };
}

interface Harness<T> {
  component: T;
  dialog: { open: jest.Mock };
  fixture: ComponentFixture<T>;
}

function mount<T>(type: Type<T>, services: [unknown, unknown][]): Harness<T> {
  const dialog = { open: jest.fn().mockReturnValue({ afterClosed: () => of(undefined) }) };
  const snackBar = { open: jest.fn() };
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideTranslateService(),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
      { provide: I18nService, useValue: { currentLang: signal('en'), instant: (k: string) => k, plural: (k: string) => k } },
      { provide: AreaService, useValue: { getSelected: () => of([]) } },
      { provide: ScannerService, useValue: { getGymById: () => of(null) } },
      { provide: AuthService, useValue: { isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
      ...services.map(([provide, useValue]) => ({ provide, useValue })),
    ],
    imports: [type],
  });
  TestBed.overrideComponent(type, {
    add: {
      providers: [
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    },
  });
  const fixture = TestBed.createComponent(type);
  fixture.detectChanges();
  return { component: fixture.componentInstance, dialog, fixture };
}

function dialogReturns(h: Harness<unknown>, value: unknown): void {
  h.dialog.open.mockReturnValue({ afterClosed: () => of(value) });
}

/** The bulk bar's Delete button, which is disabled when nothing on screen is selected. */
function deleteButton(fixture: ComponentFixture<unknown>): HTMLButtonElement {
  const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.bulk-toolbar button')) as HTMLButtonElement[];
  return buttons[buttons.length - 1];
}

describe('bulk actions act only on the selected cards on screen', () => {
  describe('lures', () => {
    const rows = [
      { ...common, uid: 1, lureId: 501 },
      { ...common, uid: 2, lureId: 502 },
      { ...common, uid: 3, lureId: 504 },
    ];

    it('select all, search, delete: only the visible uid is deleted', async () => {
      const service = alarmService(rows);
      const h = mount(LureListComponent, [[LureService, service]]);
      h.component.selectAll();
      h.component.search.set('glacial');
      dialogReturns(h, true);

      await h.component.bulkDelete();

      expect(service.delete.mock.calls.map(c => c[0])).toEqual([2]);
    });

    it('select all, search, change radius: only the visible uid, with the dialog naming that count', async () => {
      const service = alarmService(rows);
      const h = mount(LureListComponent, [[LureService, service]]);
      h.component.selectAll();
      h.component.search.set('magnetic');
      dialogReturns(h, 500);

      await h.component.bulkUpdateDistance();

      expect(h.dialog.open.mock.calls[0][1].data).toEqual({ count: 1 });
      expect(service.updateBulkDistance).toHaveBeenCalledWith([3], 500);
    });

    it('keeps hidden ticks while searching and counts them again when the search is cleared', () => {
      const h = mount(LureListComponent, [[LureService, alarmService(rows)]]);
      h.component.selectAll();
      h.component.search.set('glacial');
      expect(h.component.actionIds()).toEqual([2]);

      h.component.search.set('');
      expect([...h.component.actionIds()].sort()).toEqual([1, 2, 3]);
    });
    it('select all adds the cards on screen to the selection, keeping a tick the search hides', () => {
      const h = mount(LureListComponent, [[LureService, alarmService(rows)]]);
      h.component.toggleSelect(1);
      h.component.search.set('glacial');
      h.component.selectAll();

      h.component.search.set('');
      expect([...h.component.actionIds()].sort()).toEqual([1, 2]);
    });
  });

  describe('gyms', () => {
    const rows = [
      { ...common, uid: 11, battleChanges: 0, gymId: null, slotChanges: 0, team: 1 },
      { ...common, uid: 12, battleChanges: 0, gymId: null, slotChanges: 0, team: 2 },
      { ...common, uid: 13, battleChanges: 0, gymId: null, slotChanges: 0, team: 3 },
    ];

    it('select all, search, delete: only the visible uid is deleted', async () => {
      const service = alarmService(rows);
      const h = mount(GymListComponent, [[GymService, service]]);
      h.component.selectAll();
      h.component.search.set('valor');
      expect(h.component.visibleGyms().map(g => g.uid)).toEqual([12]);
      dialogReturns(h, true);

      await h.component.bulkDelete();

      expect(service.delete.mock.calls.map(c => c[0])).toEqual([12]);
    });
  });

  describe('invasions', () => {
    const rows = [
      { ...common, uid: 21, gender: 0, gruntType: 'dragon' },
      { ...common, uid: 22, gender: 0, gruntType: 'water' },
      { ...common, uid: 23, gender: 0, gruntType: 'fire' },
    ];

    it('select all, search, delete: only the visible uid is deleted', async () => {
      const service = alarmService(rows);
      const h = mount(InvasionListComponent, [[InvasionService, service]]);
      h.component.selectAll();
      h.component.search.set('water');
      expect(h.component.visibleInvasions().map(i => i.uid)).toEqual([22]);
      dialogReturns(h, true);

      await h.component.bulkDelete();

      expect(service.delete.mock.calls.map(c => c[0])).toEqual([22]);
    });
  });

  describe('raids and eggs', () => {
    const raidRows = [
      {
        ...common,
        uid: 7,
        costume: 9000,
        evolution: 0,
        exclusive: 0,
        form: 0,
        gymId: null,
        level: 5,
        move: 9000,
        pokemonId: 9000,
        rsvpChanges: 0,
        team: 4,
      },
    ];
    // Raid and egg uids come from separate sequences and collide; 7 is both here on purpose.
    const eggRows = [
      { ...common, uid: 7, exclusive: 0, gymId: null, level: 5, rsvpChanges: 0, team: 4 },
      { ...common, uid: 8, exclusive: 0, gymId: null, level: 1, rsvpChanges: 0, team: 4 },
    ];

    function mountRaids() {
      const raids = alarmService(raidRows);
      const eggs = alarmService(eggRows);
      const h = mount(RaidListComponent, [
        [RaidService, raids],
        [EggService, eggs],
      ]);
      return { raids, eggs, h };
    }

    it('a raid selected on the Raids tab is not counted or acted on from the Eggs tab', async () => {
      const { raids, eggs, h } = mountRaids();
      h.component.toggleSelectMode();
      h.component.toggleSelect(h.component.selectionKey('raid', 7));
      h.fixture.detectChanges();
      expect(h.component.actionIds()).toEqual(['raid:7']);
      expect(deleteButton(h.fixture).disabled).toBe(false);

      h.component.activeTab.set(1);
      h.fixture.detectChanges();

      expect(h.component.actionIds()).toEqual([]);
      expect(deleteButton(h.fixture).disabled).toBe(true);

      dialogReturns(h, true);
      await h.component.bulkDelete();
      dialogReturns(h, 1000);
      await h.component.bulkUpdateDistance();

      expect(raids.delete).not.toHaveBeenCalled();
      expect(eggs.delete).not.toHaveBeenCalled();
      expect(raids.updateBulkDistance).not.toHaveBeenCalled();
      expect(eggs.updateBulkDistance).not.toHaveBeenCalled();
    });

    it('keeps the raid selection across a tab switch and acts on it again back on the Raids tab', async () => {
      const { raids, eggs, h } = mountRaids();
      h.component.toggleSelect(h.component.selectionKey('raid', 7));
      h.component.activeTab.set(1);
      h.component.activeTab.set(0);
      dialogReturns(h, true);

      await h.component.bulkDelete();

      expect(raids.delete.mock.calls.map(c => c[0])).toEqual([7]);
      expect(eggs.delete).not.toHaveBeenCalled();
    });

    it('on the Eggs tab acts on the eggs selected there only, with the dialog naming that count', async () => {
      const { raids, eggs, h } = mountRaids();
      h.component.toggleSelect(h.component.selectionKey('raid', 7));
      h.component.activeTab.set(1);
      h.component.selectAll();
      dialogReturns(h, 1000);

      await h.component.bulkUpdateDistance();

      expect(h.dialog.open.mock.calls[0][1].data).toEqual({ count: 2 });
      // Screen order: eggs sort by tier, so the level 1 egg (uid 8) comes first.
      expect(eggs.updateBulkDistance).toHaveBeenCalledWith([8, 7], 1000);
      expect(raids.updateBulkDistance).not.toHaveBeenCalled();
    });

    it('select all on the Eggs tab keeps the raid ticked on the Raids tab', () => {
      const { h } = mountRaids();
      h.component.toggleSelect(h.component.selectionKey('raid', 7));
      h.component.activeTab.set(1);
      h.component.selectAll();
      h.component.activeTab.set(0);

      expect(h.component.actionIds()).toEqual(['raid:7']);
    });
  });
});

/** The other lists, by their source: Select all must add to the selection, never replace it. */
describe('select all keeps the ticks it cannot see', () => {
  const lists = [
    'pokemon/pokemon',
    'raids/raid',
    'quests/quest',
    'invasions/invasion',
    'lures/lure',
    'nests/nest',
    'gyms/gym',
    'fort-changes/fort-change',
    'pokestop-events/pokestop-event',
    'max-battles/max-battle',
  ];

  it.each(lists)('%s unions the visible cards into the current selection', list => {
    const source = readFileSync(join(__dirname, `${list}-list.component.ts`), 'utf8');
    const start = source.indexOf('  selectAll(): void {');
    const body = source.slice(start, source.indexOf('\n  }\n', start));

    expect(body).toContain('this.selectedIds()');
  });
});
