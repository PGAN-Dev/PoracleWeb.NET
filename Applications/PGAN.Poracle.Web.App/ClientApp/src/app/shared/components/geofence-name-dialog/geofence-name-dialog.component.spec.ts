import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { GeofenceNameDialogComponent, GeofenceNameDialogData, GeofenceNameDialogResult } from './geofence-name-dialog.component';
import { GeofenceRegion } from '../../../core/models';

describe('GeofenceNameDialogComponent', () => {
  let component: GeofenceNameDialogComponent;
  let dialogRef: { close: jest.Mock };

  const regions: GeofenceRegion[] = [
    { id: 1, name: 'downtown', displayName: 'Downtown' },
    { id: 2, name: 'suburbs', displayName: 'Suburbs' },
  ];

  function setup(data: GeofenceNameDialogData) {
    dialogRef = { close: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
      imports: [GeofenceNameDialogComponent],
    });

    const fixture = TestBed.createComponent(GeofenceNameDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  describe('with detected region', () => {
    beforeEach(() => {
      setup({
        detectedRegion: { id: 1, name: 'downtown', displayName: 'Downtown' },
        regions,
      });
    });

    it('should create successfully', () => {
      expect(component).toBeTruthy();
    });

    it('should not show manual select when region is detected', () => {
      expect(component.manualSelect()).toBe(false);
    });

    it('should pre-select the detected region id', () => {
      expect(component.selectedRegionId).toBe(1);
    });

    it('should have save button disabled when name is empty', () => {
      expect(component.isValid).toBe(false);
    });

    it('should have save button enabled when name is provided', () => {
      component.displayName = 'My Park';
      expect(component.isValid).toBe(true);
    });

    it('should return correct result on save', () => {
      component.displayName = 'My Custom Fence';
      component.save();

      expect(dialogRef.close).toHaveBeenCalledWith({
        displayName: 'My Custom Fence',
        groupName: 'Downtown',
        parentId: 1,
      } as GeofenceNameDialogResult);
    });

    it('should trim whitespace from display name on save', () => {
      component.displayName = '  Padded Name  ';
      component.save();

      expect(dialogRef.close).toHaveBeenCalledWith(expect.objectContaining({ displayName: 'Padded Name' }));
    });

    it('should switch to manual select when onChangeRegion is called', () => {
      component.onChangeRegion();
      expect(component.manualSelect()).toBe(true);
    });

    it('should not save when name is only whitespace', () => {
      component.displayName = '   ';
      expect(component.isValid).toBe(false);

      component.save();
      expect(dialogRef.close).not.toHaveBeenCalled();
    });
  });

  describe('without detected region', () => {
    beforeEach(() => {
      setup({
        detectedRegion: null,
        regions,
      });
    });

    it('should show manual select when no detected region', () => {
      expect(component.manualSelect()).toBe(true);
    });

    it('should have no region pre-selected', () => {
      expect(component.selectedRegionId).toBeNull();
    });

    it('should be invalid when no region is selected even with a name', () => {
      component.displayName = 'My Fence';
      expect(component.isValid).toBe(false);
    });

    it('should be valid when both name and region are set', () => {
      component.displayName = 'My Fence';
      component.selectedRegionId = 2;
      expect(component.isValid).toBe(true);
    });

    it('should return correct result when region is manually selected', () => {
      component.displayName = 'Suburb Fence';
      component.selectedRegionId = 2;
      component.save();

      expect(dialogRef.close).toHaveBeenCalledWith({
        displayName: 'Suburb Fence',
        groupName: 'Suburbs',
        parentId: 2,
      } as GeofenceNameDialogResult);
    });

    it('should not save when selected region is not found in regions list', () => {
      component.displayName = 'Test';
      component.selectedRegionId = 999;
      component.save();

      expect(dialogRef.close).not.toHaveBeenCalled();
    });
  });

  describe('invalid characters', () => {
    beforeEach(() => {
      setup({
        detectedRegion: { id: 1, name: 'downtown', displayName: 'Downtown' },
        regions,
      });
    });

    it('should flag invalid characters in the name', () => {
      component.displayName = 'Fence<>!';
      expect(component.hasInvalidChars).toBe(true);
    });

    it('should not flag valid characters', () => {
      component.displayName = "My Fence - North (A&B's)";
      expect(component.hasInvalidChars).toBe(false);
    });

    it('should be invalid when name has invalid characters', () => {
      component.displayName = 'Bad@Name#';
      expect(component.isValid).toBe(false);
    });

    it('should not flag empty name as having invalid chars', () => {
      component.displayName = '';
      expect(component.hasInvalidChars).toBe(false);
    });

    it('should not flag whitespace-only name as having invalid chars', () => {
      component.displayName = '   ';
      expect(component.hasInvalidChars).toBe(false);
    });

    it('should be invalid when name exceeds 50 characters', () => {
      component.displayName = 'A'.repeat(51);
      expect(component.isValid).toBe(false);
    });

    it('should be valid when name is exactly 50 characters', () => {
      component.displayName = 'A'.repeat(50);
      expect(component.isValid).toBe(true);
    });
  });

  describe('onRegionPicked', () => {
    beforeEach(() => {
      setup({
        detectedRegion: null,
        regions,
      });
    });

    it('should set selectedRegionId from picked option', () => {
      component.onRegionPicked({ id: 2, label: 'Suburbs' });
      expect(component.selectedRegionId).toBe(2);
    });

    it('should set selectedRegionId to null when option has no id', () => {
      component.onRegionPicked({ label: '' });
      expect(component.selectedRegionId).toBeNull();
    });
  });

  describe('regionOptions', () => {
    beforeEach(() => {
      setup({
        detectedRegion: null,
        regions,
      });
    });

    it('should map regions to RegionOption format', () => {
      expect(component.regionOptions).toHaveLength(2);
      expect(component.regionOptions[0]).toEqual({ id: 1, label: 'Downtown', shortLabel: 'Downtown' });
      expect(component.regionOptions[1]).toEqual({ id: 2, label: 'Suburbs', shortLabel: 'Suburbs' });
    });
  });
});
