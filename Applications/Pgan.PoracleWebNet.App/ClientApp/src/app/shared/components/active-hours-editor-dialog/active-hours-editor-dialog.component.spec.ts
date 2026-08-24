import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';

import { ActiveHoursEditorDialogComponent, ActiveHoursEditorData } from './active-hours-editor-dialog.component';
import { ActiveHourEntry } from '../../../core/models/active-hours.models';

describe('ActiveHoursEditorDialogComponent', () => {
  let component: ActiveHoursEditorDialogComponent;
  let dialogRef: { close: jest.Mock };
  let fixture: ComponentFixture<ActiveHoursEditorDialogComponent>;

  function setup(data: ActiveHoursEditorData) {
    dialogRef = { close: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), { provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: dialogRef }],
      imports: [ActiveHoursEditorDialogComponent, NoopAnimationsModule],
    });

    fixture = TestBed.createComponent(ActiveHoursEditorDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('should create successfully', () => {
    setup({ activeHours: [], profileName: 'Default' });
    expect(component).toBeTruthy();
  });

  it('should initialize with existing rules from dialog data', () => {
    const activeHours: ActiveHourEntry[] = [
      { day: 1, hours: 9, mins: 0 },
      { day: 2, hours: 9, mins: 0 },
    ];
    setup({ activeHours, profileName: 'Default' });
    expect(component.entries()).toHaveLength(2);
    expect(component.groups()).toHaveLength(1);
    expect(component.groups()[0].days).toEqual([1, 2]);
  });

  it('should initialize empty when no active hours', () => {
    setup({ activeHours: [], profileName: 'Default' });
    expect(component.entries()).toHaveLength(0);
    expect(component.groups()).toHaveLength(0);
  });

  it('should close with undefined on cancel', () => {
    setup({ activeHours: [], profileName: 'Default' });
    component.cancel();
    expect(dialogRef.close).toHaveBeenCalledWith(undefined);
  });

  it('should close with empty array on save when no entries', () => {
    setup({ activeHours: [], profileName: 'Default' });
    component.save();
    expect(dialogRef.close).toHaveBeenCalledWith([]);
  });

  it('should close with entries on save', () => {
    const activeHours: ActiveHourEntry[] = [{ day: 1, hours: 9, mins: 0 }];
    setup({ activeHours, profileName: 'Default' });
    component.save();

    expect(dialogRef.close).toHaveBeenCalledWith(expect.arrayContaining([expect.objectContaining({ day: 1, hours: 9, mins: 0 })]));
  });

  it('should add entries for selected days and time', () => {
    setup({ activeHours: [], profileName: 'Default' });

    component.selectedDays.set(new Set([1, 2])); // Mon, Tue
    component.selectedHour.set(14);
    component.selectedMinute.set(30);
    component.addEntries();

    expect(component.entries()).toHaveLength(2);
    expect(component.groups()).toHaveLength(1);
    expect(component.groups()[0].days).toEqual([1, 2]);
    expect(component.groups()[0].hours).toBe(14);
    expect(component.groups()[0].mins).toBe(30);
  });

  it('should not add duplicate entries', () => {
    setup({ activeHours: [], profileName: 'Default' });

    component.selectedDays.set(new Set([1])); // Mon
    component.selectedHour.set(9);
    component.selectedMinute.set(0);
    component.addEntries();

    // Try to add the same entry again
    component.selectedDays.set(new Set([1])); // Mon
    component.selectedHour.set(9);
    component.selectedMinute.set(0);
    component.addEntries();

    expect(component.entries()).toHaveLength(1);
  });

  it('should not add entries when no days selected', () => {
    setup({ activeHours: [], profileName: 'Default' });

    component.selectedHour.set(9);
    component.selectedMinute.set(0);
    component.addEntries();

    expect(component.entries()).toHaveLength(0);
  });

  it('should remove a rule group', () => {
    const activeHours: ActiveHourEntry[] = [
      { day: 1, hours: 9, mins: 0 },
      { day: 2, hours: 9, mins: 0 },
      { day: 3, hours: 18, mins: 0 },
    ];
    setup({ activeHours, profileName: 'Default' });

    expect(component.groups()).toHaveLength(2);

    // Remove the 9:00 AM group by passing the group object
    component.removeGroup(component.groups()[0]);

    expect(component.entries()).toHaveLength(1);
    expect(component.groups()).toHaveLength(1);
    expect(component.groups()[0].hours).toBe(18);
  });

  it('should clear all rules', () => {
    const activeHours: ActiveHourEntry[] = [
      { day: 1, hours: 9, mins: 0 },
      { day: 2, hours: 18, mins: 0 },
    ];
    setup({ activeHours, profileName: 'Default' });

    component.clearAll();

    expect(component.entries()).toHaveLength(0);
    expect(component.groups()).toHaveLength(0);
  });

  it('should set preset days for weekdays', () => {
    setup({ activeHours: [], profileName: 'Default' });

    component.presetDays('weekdays');

    expect(component.selectedDays()).toEqual(new Set([1, 2, 3, 4, 5]));
  });

  describe('repeating ranges (#808)', () => {
    it('should write step and end time onto every selected day when repeat is on', () => {
      setup({ activeHours: [], profileName: 'Default' });

      component.presetDays('weekends');
      component.selectedHour.set(9);
      component.selectedMinute.set(0);
      component.repeatEnabled.set(true);
      component.selectedEndHour.set(17);
      component.selectedEndMinute.set(30);
      component.selectedStep.set(2);
      component.addEntries();

      expect(component.entries()).toEqual([
        { day: 6, endHours: 17, endMins: 30, hours: 9, mins: 0, step: 2 },
        { day: 7, endHours: 17, endMins: 30, hours: 9, mins: 0, step: 2 },
      ]);
    });

    it('should omit the range fields entirely when repeat is off', () => {
      setup({ activeHours: [], profileName: 'Default' });

      component.toggleDay(1);
      component.addEntries();

      expect(component.entries()).toEqual([{ day: 1, hours: 9, mins: 0 }]);
    });

    it('should refuse to add a range that ends at or before its start', () => {
      setup({ activeHours: [], profileName: 'Default' });

      component.toggleDay(1);
      component.repeatEnabled.set(true);
      component.selectedHour.set(17);
      component.selectedEndHour.set(9);

      expect(component.rangeInvalid()).toBe(true);
      component.addEntries();
      expect(component.entries()).toHaveLength(0);
    });

    it('should keep a range intact when a single fire sharing its start time is removed', () => {
      const activeHours: ActiveHourEntry[] = [
        { day: 1, hours: 9, mins: 0 },
        { day: 1, endHours: 17, endMins: 0, hours: 9, mins: 0, step: 2 },
      ];
      setup({ activeHours, profileName: 'Default' });
      const single = component.groups().find(g => g.step === 0)!;

      component.removeGroup(single);

      expect(component.entries()).toEqual([{ day: 1, endHours: 17, endMins: 0, hours: 9, mins: 0, step: 2 }]);
    });

    it('should give a range and a single fire on the same start distinct rule keys', () => {
      const activeHours: ActiveHourEntry[] = [
        { day: 1, hours: 9, mins: 0 },
        { day: 1, endHours: 17, endMins: 0, hours: 9, mins: 0, step: 2 },
      ];
      setup({ activeHours, profileName: 'Default' });
      const [a, b] = component.groups();

      expect(component.groups()).toHaveLength(2);
      expect(component.groupKey(a)).not.toBe(component.groupKey(b));
      expect(component.isRange(a) !== component.isRange(b)).toBe(true);
    });

    it('should drop the preview dots but keep the span for a range with too many fires to read', () => {
      const activeHours: ActiveHourEntry[] = [{ day: 1, endHours: 23, endMins: 0, hours: 0, mins: 0, step: 1 }];
      setup({ activeHours, profileName: 'Default' });

      const monday = component.previewData()[0];
      expect(monday.rules[0].isRange).toBe(true);
      expect(monday.rules[0].markers).toHaveLength(0);
      expect(monday.rules[0].end).toBe(23 * 60);
    });
  });

  /**
   * Turning on a repeat puts five controls in the time row, and `Until (hour)` and `Until (minute)`
   * were too long for a field that has to stay narrow enough for two to sit side by side on a phone --
   * both labels rendered clipped. The qualifier moved out of the field and onto the pair it describes,
   * so each field is labelled by the short word it asks for; screen readers still hear the whole
   * thing, from the field's own accessible name.
   */
  describe('time row labels', () => {
    function fieldLabels(): string[] {
      return Array.from(fixture.nativeElement.querySelectorAll('.time-picker mat-label')).map(el =>
        (el as HTMLElement).textContent!.trim(),
      );
    }

    function captions(): string[] {
      return Array.from(fixture.nativeElement.querySelectorAll('.time-picker .group-caption')).map(el =>
        (el as HTMLElement).textContent!.trim(),
      );
    }

    it('names the end fields by their group rather than repeating the qualifier in each label', () => {
      setup({ activeHours: [], profileName: 'Default' });
      component.repeatEnabled.set(true);
      fixture.detectChanges();

      expect(fieldLabels()).toEqual([
        'PROFILES.ACTIVE_HOURS_HOUR',
        'PROFILES.ACTIVE_HOURS_MINUTE',
        'PROFILES.ACTIVE_HOURS_HOUR',
        'PROFILES.ACTIVE_HOURS_MINUTE',
        'PROFILES.ACTIVE_HOURS_REPEAT_EVERY',
      ]);
      expect(captions()).toEqual(['PROFILES.ACTIVE_HOURS_STARTS_AT', 'PROFILES.ACTIVE_HOURS_UNTIL']);
    });

    it('keeps the end fields tellable apart by their accessible names', () => {
      setup({ activeHours: [], profileName: 'Default' });
      component.repeatEnabled.set(true);
      fixture.detectChanges();

      const labels = Array.from(fixture.nativeElement.querySelectorAll('.time-picker mat-select')).map(el =>
        (el as HTMLElement).getAttribute('aria-label'),
      );
      expect(labels).toEqual([null, null, 'PROFILES.ACTIVE_HOURS_END_HOUR', 'PROFILES.ACTIVE_HOURS_END_MINUTE', null]);
    });

    it('shows no group captions when there is only one time to give', () => {
      setup({ activeHours: [], profileName: 'Default' });

      expect(fieldLabels()).toEqual(['PROFILES.ACTIVE_HOURS_HOUR', 'PROFILES.ACTIVE_HOURS_MINUTE']);
      expect(captions()).toEqual([]);
    });
  });
});
