import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';

import { DistanceDialogComponent } from './distance-dialog.component';
import { ConfigService } from '../../../core/services/config.service';

describe('DistanceDialogComponent', () => {
  let component: DistanceDialogComponent;
  let dialogRef: { close: jest.Mock };
  let fixture: ComponentFixture<DistanceDialogComponent>;

  function setup(count = 3): void {
    dialogRef = { close: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        // delivery-preview links to /areas with routerLink.
        provideRouter([]),
        provideTranslateService(),
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MAT_DIALOG_DATA, useValue: { count } },
        { provide: ConfigService, useValue: { apiHost: 'http://test' } },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
      imports: [DistanceDialogComponent],
    });

    fixture = TestBed.createComponent(DistanceDialogComponent);
    component = fixture.componentInstance;
  }

  beforeEach(() => setup());

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

  /**
   * The dialog said "Update All Distance", "all alarms of this type" and "Update All" while acting on the
   * two alarms someone had selected. It names the selection now, in the scope picker's words.
   */
  describe('what it says', () => {
    function text(selector: string): string {
      return ((fixture.nativeElement as HTMLElement).querySelector(selector)?.textContent ?? '').trim();
    }

    beforeEach(() => {
      TestBed.inject(TranslateService).setTranslation('en', {
        DIALOG: {
          DISTANCE_APPLY_ONE: 'Update {{count}} alarm',
          DISTANCE_APPLY_OTHER: 'Update {{count}} alarms',
          DISTANCE_TITLE_ONE: 'Change the radius of {{count}} alarm',
          DISTANCE_TITLE_OTHER: 'Change the radius of {{count}} alarms',
        },
      });
      TestBed.inject(TranslateService).use('en');
    });

    it('counts the selected alarms in the title and on the button', () => {
      fixture.detectChanges();

      expect(text('h2')).toBe('Change the radius of 3 alarms');
      expect(text('mat-dialog-actions button:last-child')).toBe('Update 3 alarms');
    });

    it('says one alarm for one', () => {
      setup(1);
      TestBed.inject(TranslateService).setTranslation('en', {
        DIALOG: { DISTANCE_APPLY_ONE: 'Update {{count}} alarm', DISTANCE_TITLE_ONE: 'Change the radius of {{count}} alarm' },
      });
      TestBed.inject(TranslateService).use('en');
      fixture.detectChanges();

      expect(text('h2')).toBe('Change the radius of 1 alarm');
      expect(text('mat-dialog-actions button:last-child')).toBe('Update 1 alarm');
    });
  });

  /** The same range the alarm dialogs offer, so a bulk change can't store what a single edit refuses. */
  describe('the radius it accepts', () => {
    it('refuses zero, past 100 km and an empty field', () => {
      component.mode.set('distance');

      for (const km of [0, -5, 100.5, 500, null]) {
        component.distanceKm = km;
        expect(component.isValid).toBe(false);
        component.apply();
      }

      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('accepts both ends of the range', () => {
      component.mode.set('distance');

      component.distanceKm = 0.1;
      expect(component.isValid).toBe(true);
      component.distanceKm = 100;
      expect(component.isValid).toBe(true);
      component.apply();

      expect(dialogRef.close).toHaveBeenCalledWith(100000);
    });

    it('ignores the field when switching the alarms to their areas', () => {
      component.distanceKm = 500;
      component.mode.set('areas');

      expect(component.isValid).toBe(true);
    });
  });
});
