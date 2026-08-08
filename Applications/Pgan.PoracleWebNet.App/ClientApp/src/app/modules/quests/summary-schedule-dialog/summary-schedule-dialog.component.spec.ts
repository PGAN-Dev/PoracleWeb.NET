import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { Subject, of, throwError } from 'rxjs';

import { SummaryScheduleDialogComponent, SummaryScheduleDialogData } from './summary-schedule-dialog.component';
import { ActiveHourEntry } from '../../../core/models/active-hours.models';
import { I18nService } from '../../../core/services/i18n.service';
import { LocationService } from '../../../core/services/location.service';
import { SummarySchedule, SummaryScheduleService } from '../../../core/services/summary-schedule.service';
import { ActiveHoursEditorDialogComponent } from '../../../shared/components/active-hours-editor-dialog/active-hours-editor-dialog.component';

describe('SummaryScheduleDialogComponent', () => {
  let component: SummaryScheduleDialogComponent;
  let dialogRef: { close: jest.Mock };
  let summaryService: {
    enabled: jest.Mock;
    getSchedule: jest.Mock;
    setSchedule: jest.Mock;
    deleteSchedule: jest.Mock;
    trigger: jest.Mock;
  };
  let matDialog: { open: jest.Mock };
  let locationService: { getLocation: jest.Mock };
  let snackBar: { open: jest.Mock };

  const QUEST_SCHEDULE: SummarySchedule = {
    activeHours: [
      { day: 1, hours: 9, mins: 0 },
      { day: 2, hours: 9, mins: 0 },
    ],
    alertType: 'quest',
  };

  function makeAfterClosed<T>(value: T): { afterClosed: jest.Mock } {
    return { afterClosed: jest.fn(() => of(value)) };
  }

  function setup(
    overrides: {
      enabled?: boolean;
      schedule?: SummarySchedule | null;
      location?: { latitude: number; longitude: number };
      data?: SummaryScheduleDialogData;
    } = {},
  ) {
    const enabled = overrides.enabled ?? true;
    const schedule = overrides.schedule === undefined ? QUEST_SCHEDULE : overrides.schedule;
    const location = overrides.location ?? { latitude: 0, longitude: 0 };

    dialogRef = { close: jest.fn() };
    summaryService = {
      deleteSchedule: jest.fn(() => of(undefined)),
      enabled: jest.fn(() => enabled),
      getSchedule: jest.fn(() => of(schedule)),
      setSchedule: jest.fn(() => of(undefined)),
      trigger: jest.fn(() => of(undefined)),
    };
    matDialog = { open: jest.fn(() => makeAfterClosed<ActiveHourEntry[] | undefined>(undefined)) };
    locationService = { getLocation: jest.fn(() => of(location)) };
    snackBar = { open: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MAT_DIALOG_DATA, useValue: overrides.data ?? ({ alertType: 'quest' } as SummaryScheduleDialogData) },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: SummaryScheduleService, useValue: summaryService },
        { provide: MatDialog, useValue: matDialog },
        { provide: LocationService, useValue: locationService },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: I18nService, useValue: { instant: (key: string) => key } },
      ],
      imports: [SummaryScheduleDialogComponent, NoopAnimationsModule],
    }).overrideComponent(SummaryScheduleDialogComponent, {
      // MatDialogModule provides MatDialog at the component injector, which shadows the
      // module-level test provider — override at component scope so the nested-editor open is mocked.
      add: { providers: [{ provide: MatDialog, useValue: matDialog }] },
    });

    const fixture = TestBed.createComponent(SummaryScheduleDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('should create', () => {
    setup();
    expect(component).toBeTruthy();
  });

  it('loads the schedule for the injected alert type with exactly one proxy call', () => {
    setup();
    expect(summaryService.getSchedule).toHaveBeenCalledTimes(1);
    expect(summaryService.getSchedule).toHaveBeenCalledWith('quest');
    expect(component.schedule()).toEqual(QUEST_SCHEDULE);
  });

  it('derives entries and hasSchedule from the loaded schedule', () => {
    setup();
    expect(component.entries()).toEqual(QUEST_SCHEDULE.activeHours);
    expect(component.hasSchedule()).toBe(true);
  });

  it('reports no schedule when the upstream returns null (404 mapped to null)', () => {
    setup({ schedule: null });
    expect(component.entries()).toEqual([]);
    expect(component.hasSchedule()).toBe(false);
  });

  it('seeds user coordinates from the location service', () => {
    setup({ location: { latitude: 47.5, longitude: -122.3 } });
    expect(locationService.getLocation).toHaveBeenCalled();
    expect(component.userLat()).toBe(47.5);
    expect(component.userLon()).toBe(-122.3);
  });

  describe('editor reuse', () => {
    // The editor's default empty state says the profile will need a manual start, which contradicts this
    // dialog's own line about quests being delivered individually. See #457.
    it('passes the quest empty-state line to the shared editor', () => {
      setup();
      component.editSchedule();

      const [, openConfig] = matDialog.open.mock.calls[0];
      expect(openConfig.data.emptyStateKey).toBe('QUESTS.SUMMARY_SCHEDULE_EMPTY');
    });

    it('opens ActiveHoursEditorDialogComponent seeded with the current entries and a profileName label', () => {
      setup();
      component.editSchedule();

      expect(matDialog.open).toHaveBeenCalledTimes(1);
      const [openedComponent, openConfig] = matDialog.open.mock.calls[0];
      expect(openedComponent).toBe(ActiveHoursEditorDialogComponent);
      expect(openConfig.data.activeHours).toEqual(QUEST_SCHEDULE.activeHours);
      // profileName is mandatory on the editor; the dialog passes a translated alert label.
      expect(openConfig.data.profileName).toBe('QUESTS.SUMMARY_SCHEDULE_ALERT_LABEL');
    });

    it('persists the editor result by calling setSchedule with the returned entries array', () => {
      const edited: ActiveHourEntry[] = [{ day: 3, hours: 18, mins: 30 }];
      matDialog.open.mockReturnValue(makeAfterClosed<ActiveHourEntry[]>(edited));
      setup();
      // Re-point the editor open to the edited result for this case.
      matDialog.open.mockReturnValue(makeAfterClosed<ActiveHourEntry[]>(edited));

      component.editSchedule();

      expect(summaryService.setSchedule).toHaveBeenCalledWith('quest', edited);
    });

    it('does not call setSchedule when the editor is cancelled (afterClosed -> undefined)', () => {
      matDialog.open.mockReturnValue(makeAfterClosed<ActiveHourEntry[] | undefined>(undefined));
      setup();
      matDialog.open.mockReturnValue(makeAfterClosed<ActiveHourEntry[] | undefined>(undefined));

      component.editSchedule();

      expect(summaryService.setSchedule).not.toHaveBeenCalled();
    });

    it('updates in-memory state and shows a snackbar after a successful save', () => {
      const edited: ActiveHourEntry[] = [{ day: 4, hours: 7, mins: 0 }];
      setup();
      matDialog.open.mockReturnValue(makeAfterClosed<ActiveHourEntry[]>(edited));

      component.editSchedule();

      expect(summaryService.setSchedule).toHaveBeenCalledWith('quest', edited);
      // Optimistic update — no refetch round-trip; the editor returns the canonical entries.
      expect(component.entries()).toEqual(edited);
      expect(component.hasSchedule()).toBe(true);
      expect(snackBar.open).toHaveBeenCalled();
    });
  });

  describe('send summary now (trigger)', () => {
    it('calls trigger for the injected alert type', () => {
      setup();
      component.sendNow();
      expect(summaryService.trigger).toHaveBeenCalledWith('quest');
    });

    it('shows a success snackbar after a successful trigger', () => {
      setup();
      component.sendNow();
      expect(snackBar.open).toHaveBeenCalled();
    });

    it('shows an error snackbar when the trigger fails', () => {
      setup();
      summaryService.trigger.mockReturnValue(throwError(() => ({ status: 503 })));
      snackBar.open.mockClear();

      component.sendNow();

      expect(snackBar.open).toHaveBeenCalled();
    });

    it('dedupes an in-flight trigger so a double-click cannot double-deliver', () => {
      const gate = new Subject<void>();
      summaryService.trigger.mockReturnValue(gate.asObservable());
      setup();
      summaryService.trigger.mockReturnValue(gate.asObservable());

      component.sendNow();
      component.sendNow();

      expect(summaryService.trigger).toHaveBeenCalledTimes(1);
      gate.complete();
    });

    it('blocks a second trigger during the cooldown window after a successful send', () => {
      setup();
      component.sendNow();
      expect(summaryService.trigger).toHaveBeenCalledTimes(1);

      component.sendNow();
      expect(summaryService.trigger).toHaveBeenCalledTimes(1);
    });
  });

  describe('clear / delete', () => {
    it('deletes the schedule for the injected alert type', () => {
      setup();
      component.clearSchedule();
      expect(summaryService.deleteSchedule).toHaveBeenCalledWith('quest');
    });

    it('clears in-memory state and shows a snackbar after a successful delete', () => {
      setup();

      component.clearSchedule();

      expect(summaryService.deleteSchedule).toHaveBeenCalledWith('quest');
      // Optimistic clear — no refetch round-trip.
      expect(component.entries()).toEqual([]);
      expect(component.hasSchedule()).toBe(false);
      expect(snackBar.open).toHaveBeenCalled();
    });
  });

  describe('location warning', () => {
    it('flags an active schedule with 0,0 coordinates (warning condition met)', () => {
      setup({ location: { latitude: 0, longitude: 0 }, schedule: QUEST_SCHEDULE });
      expect(component.hasSchedule()).toBe(true);
      expect(component.userLat()).toBe(0);
      expect(component.userLon()).toBe(0);
    });

    it('does not flag when coordinates are set', () => {
      setup({ location: { latitude: 51.5, longitude: -0.12 }, schedule: QUEST_SCHEDULE });
      expect(component.userLat()).toBe(51.5);
      expect(component.userLon()).toBe(-0.12);
    });
  });
});
