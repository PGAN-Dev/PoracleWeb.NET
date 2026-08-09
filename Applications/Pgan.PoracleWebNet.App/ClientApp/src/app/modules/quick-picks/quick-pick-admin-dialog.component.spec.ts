import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { QuickPickAdminDialogComponent } from './quick-pick-admin-dialog.component';
import { QuickPickDefinition } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { QuickPickService } from '../../core/services/quick-pick.service';

describe('QuickPickAdminDialogComponent', () => {
  let component: QuickPickAdminDialogComponent;
  let quickPickService: { saveAdmin: jest.Mock; saveUser: jest.Mock };

  function setup(data: QuickPickDefinition | null, isAdmin = true): void {
    quickPickService = {
      saveAdmin: jest.fn(() => of({})),
      saveUser: jest.fn(() => of({})),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: QuickPickService, useValue: quickPickService },
        { provide: AuthService, useValue: { isAdmin: () => isAdmin } },
      ],
      imports: [QuickPickAdminDialogComponent],
    });

    const fixture = TestBed.createComponent(QuickPickAdminDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function savedDefinition(): QuickPickDefinition {
    const call = quickPickService.saveAdmin.mock.calls[0] ?? quickPickService.saveUser.mock.calls[0];
    return call[0] as QuickPickDefinition;
  }

  describe('editing an existing definition', () => {
    // The built-in "nundo" preset is { minIv: 0, maxIv: 0 } -- that is the entire point of it. Skipping
    // falsy values on save turned "0% IV only" into "any IV" for everyone who applied it afterwards.
    // See #654.
    const nundo: QuickPickDefinition = {
      id: 'nundo',
      name: 'Nundo',
      alarmType: 'monster',
      category: 'PvP',
      description: 'Zero IV',
      enabled: true,
      filters: { maxIv: 0, minIv: 0 },
      icon: 'pokeball',
      scope: 'global',
      sortOrder: 1,
    };

    it('keeps a stored filter whose value is zero', () => {
      setup(nundo);

      component.save();

      const saved = savedDefinition();
      expect(saved.filters['minIv']).toBe(0);
      expect(saved.filters['maxIv']).toBe(0);
    });

    it('keeps a stored key the dialog has no control for', () => {
      // quick-pick-apply reads filters['clean'] as its base bitmask, and no form exposes it.
      setup({ ...nundo, filters: { clean: 5, maxIv: 0, minIv: 0 } });

      component.save();

      expect(savedDefinition().filters['clean']).toBe(5);
    });
  });

  describe('changing the alarm type', () => {
    const monsterPick: QuickPickDefinition = {
      id: 'p',
      name: 'P',
      alarmType: 'monster',
      category: 'PvP',
      description: '',
      enabled: true,
      filters: { clean: 5, minIv: 0, pvpRankingLeague: 1500 },
      icon: 'pokeball',
      scope: 'global',
      sortOrder: 1,
    };

    it('drops the previous type’s filters', () => {
      setup(monsterPick);
      component.mainForm.patchValue({ alarmType: 'lure' });

      component.save();

      expect(savedDefinition().filters['pvpRankingLeague']).toBeUndefined();
      expect(savedDefinition().filters['minIv']).toBeUndefined();
    });

    it('keeps clean, which belongs to no type', () => {
      // quick-pick-apply reads it as the base bitmask, and clearing wholesale reset a pick's
      // auto-delete, edit and summary bits on a type change. See #671.
      setup(monsterPick);
      component.mainForm.patchValue({ alarmType: 'lure' });

      component.save();

      expect(savedDefinition().filters['clean']).toBe(5);
    });
  });

  describe('creating a new definition', () => {
    it('does not write out the form defaults that mean "not set"', () => {
      // Most numeric controls default to 0. Persisting them would put an explicit minIv, pokemonId and
      // pvpRankingLeague of 0 on every new pick, which is the mirror image of the bug above.
      setup(null);
      component.mainForm.patchValue({ name: 'Fresh', alarmType: 'monster' });

      component.save();

      const filters = savedDefinition().filters;
      expect(filters['minIv']).toBeUndefined();
      expect(filters['pokemonId']).toBeUndefined();
      expect(filters['pvpRankingLeague']).toBeUndefined();
    });

    it('still writes a non-default value', () => {
      setup(null);
      component.mainForm.patchValue({ name: 'Hundo', alarmType: 'monster' });
      component.monsterForm.patchValue({ minIv: 100 });

      component.save();

      expect(savedDefinition().filters['minIv']).toBe(100);
    });
  });

  describe('scope', () => {
    it('keeps a user-scoped pick user-scoped when an admin edits it', () => {
      // Deciding from isAdmin alone republished an admin's own personal pick to every user. See #631.
      setup({
        id: 'mine',
        name: 'Mine',
        alarmType: 'monster',
        category: 'Common',
        description: '',
        enabled: true,
        filters: {},
        icon: 'bolt',
        scope: 'user',
        sortOrder: 0,
      });

      component.save();

      expect(quickPickService.saveUser).toHaveBeenCalled();
      expect(quickPickService.saveAdmin).not.toHaveBeenCalled();
    });

    it('publishes a new pick globally when an admin creates it', () => {
      setup(null);
      component.mainForm.patchValue({ name: 'Global', alarmType: 'monster' });

      component.save();

      expect(quickPickService.saveAdmin).toHaveBeenCalled();
    });
  });
});
