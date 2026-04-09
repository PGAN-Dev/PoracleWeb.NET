import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';

import { DistanceDialogComponent } from './distance-dialog.component';
import { ConfigService } from '../../../core/services/config.service';

describe('DistanceDialogComponent', () => {
  let component: DistanceDialogComponent;
  let dialogRef: { close: jest.Mock };

  beforeEach(() => {
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
      imports: [DistanceDialogComponent],
    });

    const fixture = TestBed.createComponent(DistanceDialogComponent);
    component = fixture.componentInstance;
  });

  it('should default to areas mode', () => {
    expect(component.mode()).toBe('areas');
  });

  it('should default distance to 1 km', () => {
    expect(component.distanceKm).toBe(1);
  });

  it('should close with 0 meters when in areas mode', () => {
    component.mode.set('areas');
    component.apply();

    expect(dialogRef.close).toHaveBeenCalledWith(0);
  });

  it('should close with meters when in distance mode', () => {
    component.mode.set('distance');
    component.distanceKm = 2.5;
    component.apply();

    expect(dialogRef.close).toHaveBeenCalledWith(2500);
  });

  it('should round meters to nearest integer', () => {
    component.mode.set('distance');
    component.distanceKm = 1.1;
    component.apply();

    expect(dialogRef.close).toHaveBeenCalledWith(1100);
  });

  it('should handle fractional km correctly', () => {
    component.mode.set('distance');
    component.distanceKm = 0.5;
    component.apply();

    expect(dialogRef.close).toHaveBeenCalledWith(500);
  });
});
