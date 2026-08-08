import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';

import { AlertDefaultsDialogComponent } from './alert-defaults-dialog.component';
import { ConfigService } from '../../../core/services/config.service';

describe('AlertDefaultsDialogComponent', () => {
  let dialogRef: { close: jest.Mock };

  function create(): AlertDefaultsDialogComponent {
    dialogRef = { close: jest.fn() };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: ConfigService, useValue: { apiHost: 'http://test' } },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
      imports: [AlertDefaultsDialogComponent],
    });
    return TestBed.createComponent(AlertDefaultsDialogComponent).componentInstance;
  }

  beforeEach(() => localStorage.clear());

  it('initializes from the stored defaults (areas, 1 km) when nothing is saved', () => {
    const component = create();
    expect(component.mode()).toBe('areas');
    expect(component.distanceKm).toBe(1);
  });

  it('initializes from a previously saved distance preference', () => {
    localStorage.setItem('poracle-default-alert-mode', 'distance');
    localStorage.setItem('poracle-default-alert-distance-km', '3');
    const component = create();
    expect(component.mode()).toBe('distance');
    expect(component.distanceKm).toBe(3);
  });

  it('saves the chosen defaults to localStorage and closes', () => {
    const component = create();
    component.mode.set('distance');
    component.distanceKm = 2.5;
    component.save();

    expect(localStorage.getItem('poracle-default-alert-mode')).toBe('distance');
    expect(localStorage.getItem('poracle-default-alert-distance-km')).toBe('2.5');
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  // This used to assert the silent clamp: the dialog accepted 200 km, echoed "200 km" in the live
  // preview, then stored 100. That WAS the bug -- the user was shown one number and given another.
  // Out-of-range input is now refused instead. See #426.
  it('refuses to save a distance above the maximum', () => {
    const component = create();
    component.mode.set('distance');
    component.distanceKm = 99999;

    expect(component.distanceError).toBe('ALERT_DEFAULTS.DISTANCE_TOO_LARGE');
    expect(component.canSave).toBe(false);

    component.save();

    expect(localStorage.getItem('poracle-default-alert-distance-km')).toBeNull();
  });

  it.each([0, -5, 0.05])('refuses %p, which used to be silently saved as 1', km => {
    const component = create();
    component.mode.set('distance');
    component.distanceKm = km;

    expect(component.distanceError).toBe('ALERT_DEFAULTS.DISTANCE_TOO_SMALL');
    expect(component.canSave).toBe(false);
  });

  it('saves a valid distance', () => {
    const component = create();
    component.mode.set('distance');
    component.distanceKm = 5;

    expect(component.canSave).toBe(true);
    component.save();

    expect(localStorage.getItem('poracle-default-alert-distance-km')).toBe('5');
  });

  it('does not block saving in areas mode', () => {
    const component = create();
    component.mode.set('areas');
    component.distanceKm = 99999;

    expect(component.canSave).toBe(true);
  });
});
