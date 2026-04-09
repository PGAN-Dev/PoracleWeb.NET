import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslateModule } from '@ngx-translate/core';

import { QuickPickApplyDialogComponent } from './quick-pick-apply-dialog.component';
import { QuickPickSummary } from '../../core/models';
import { ConfigService } from '../../core/services/config.service';

describe('QuickPickApplyDialogComponent', () => {
  let component: QuickPickApplyDialogComponent;
  let dialogRef: { close: jest.Mock };
  const API = 'http://test-api';

  const basePick: QuickPickSummary = {
    appliedState: null,
    definition: {
      id: 'test-1',
      name: 'Test Pick',
      alarmType: 'monster',
      category: 'PvP',
      description: 'A test quick pick',
      enabled: true,
      filters: {},
      icon: 'pokeball',
      scope: 'global',
      sortOrder: 1,
    },
  };

  function setup(data: QuickPickSummary) {
    dialogRef = { close: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
      imports: [QuickPickApplyDialogComponent, TranslateModule.forRoot()],
    });

    const fixture = TestBed.createComponent(QuickPickApplyDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  describe('new apply (no applied state)', () => {
    beforeEach(() => {
      setup(basePick);
    });

    it('should create with injected data', () => {
      expect(component).toBeTruthy();
      expect(component.data.definition.name).toBe('Test Pick');
      expect(component.data.definition.id).toBe('test-1');
    });

    it('should not be a reapply', () => {
      expect(component.isReapply).toBe(false);
    });

    it('should show exclusions for monster type with pokemonId=0 (undefined filters)', () => {
      expect(component.showExclusions).toBe(true);
    });

    it('should initialize with applying false', () => {
      expect(component.applying()).toBe(false);
    });

    it('should initialize with empty excluded pokemon ids', () => {
      expect(component.excludedPokemonIds()).toEqual([]);
    });

    it('should have a delivery form with default values', () => {
      const form = component.deliveryForm.getRawValue();
      expect(form.clean).toBe(false);
      expect(form.distanceKm).toBe(0);
      expect(form.distanceMode).toBe('areas');
      expect(form.template).toBe('');
    });
  });

  describe('reapply (with applied state)', () => {
    beforeEach(() => {
      setup({
        ...basePick,
        appliedState: {
          trackedUids: [100, 200],
          appliedAt: '2025-01-01',
          excludePokemonIds: [1, 4, 7],
          quickPickId: 'test-1',
        },
      });
    });

    it('should be a reapply', () => {
      expect(component.isReapply).toBe(true);
    });

    it('should initialize excluded pokemon from applied state', () => {
      expect(component.excludedPokemonIds()).toEqual([1, 4, 7]);
    });
  });

  describe('non-monster alarm type', () => {
    it('should not show exclusions for quest type', () => {
      setup({
        ...basePick,
        definition: { ...basePick.definition, alarmType: 'quest' },
      });

      expect(component.showExclusions).toBe(false);
    });

    it('should not show exclusions for raid type', () => {
      setup({
        ...basePick,
        definition: { ...basePick.definition, alarmType: 'raid' },
      });

      expect(component.showExclusions).toBe(false);
    });

    it('should not show exclusions for specific pokemonId', () => {
      setup({
        ...basePick,
        definition: {
          ...basePick.definition,
          alarmType: 'monster',
          filters: { pokemonId: 129 },
        },
      });

      expect(component.showExclusions).toBe(false);
    });
  });

  describe('onExcludedPokemonChange', () => {
    beforeEach(() => {
      setup(basePick);
    });

    it('should update excluded pokemon ids', () => {
      component.onExcludedPokemonChange([10, 20, 30]);
      expect(component.excludedPokemonIds()).toEqual([10, 20, 30]);
    });
  });

  describe('onDistanceModeChange', () => {
    beforeEach(() => {
      setup(basePick);
    });

    it('should reset distance to 0 when switching to areas mode', () => {
      component.deliveryForm.controls.distanceKm.setValue(5);
      component.deliveryForm.controls.distanceMode.setValue('areas');
      component.onDistanceModeChange();
      expect(component.deliveryForm.controls.distanceKm.value).toBe(0);
    });

    it('should set distance to 1 when switching to distance mode with 0', () => {
      component.deliveryForm.controls.distanceKm.setValue(0);
      component.deliveryForm.controls.distanceMode.setValue('distance');
      component.onDistanceModeChange();
      expect(component.deliveryForm.controls.distanceKm.value).toBe(1);
    });
  });
});
