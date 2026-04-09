import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';

import { ActiveHoursEditorDialogComponent, ActiveHoursEditorData } from './active-hours-editor-dialog.component';
import { ActiveHourEntry } from '../../../core/models/active-hours.models';

describe('ActiveHoursEditorDialogComponent', () => {
  let component: ActiveHoursEditorDialogComponent;
  let dialogRef: { close: jest.Mock };

  function setup(data: ActiveHoursEditorData) {
    dialogRef = { close: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
      imports: [ActiveHoursEditorDialogComponent, NoopAnimationsModule],
    });

    const fixture = TestBed.createComponent(ActiveHoursEditorDialogComponent);
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
});
