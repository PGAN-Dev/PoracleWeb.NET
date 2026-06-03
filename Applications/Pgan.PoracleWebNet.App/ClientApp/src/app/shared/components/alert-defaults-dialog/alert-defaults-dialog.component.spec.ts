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

  it('clamps an out-of-range distance before persisting', () => {
    const component = create();
    component.mode.set('distance');
    component.distanceKm = 99999;
    component.save();

    expect(localStorage.getItem('poracle-default-alert-distance-km')).toBe(String(component.maxKm));
  });
});
