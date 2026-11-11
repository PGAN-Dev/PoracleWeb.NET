import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatTabGroup } from '@angular/material/tabs';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';

import { QuickPickApplyDialogComponent } from './quick-pick-apply-dialog.component';
import { LinkSide, QuickPickSummary } from '../../core/models';
import { ConfigService } from '../../core/services/config.service';
import { LinkedAccountService } from '../../core/services/linked-account.service';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

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

  function setup(data: QuickPickSummary, linked?: { canChoose: boolean; defaultDestination: LinkSide }) {
    dialogRef = { close: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        // delivery-preview links to /areas with routerLink, so a router is required.
        provideRouter([]),
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        // Without this the Delivery tab's body never attaches in jsdom, so its picker cannot be queried.
        provideNoopAnimations(),
        { provide: ConfigService, useValue: { apiHost: API } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
        ...(linked
          ? [
              {
                provide: LinkedAccountService,
                useValue: {
                  canChoose: signal(linked.canChoose),
                  status: signal({ defaultDestination: linked.defaultDestination, linked: true }),
                },
              },
            ]
          : []),
      ],
      imports: [QuickPickApplyDialogComponent],
    });

    const fixture = TestBed.createComponent(QuickPickApplyDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    return fixture;
  }

  async function templateSide(fixture: ReturnType<typeof setup>): Promise<LinkSide | null> {
    // The picker sits on the Delivery tab, which Material renders only once it is selected.
    fixture.debugElement.query(By.directive(MatTabGroup)).componentInstance.selectedIndex = 1;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.debugElement.query(By.directive(TemplateSelectorComponent)).componentInstance.side();
  }

  describe('template picker side', () => {
    // The dialog has no "Deliver to": an apply creates on the link's default destination, so the picker
    // must list that side's templates rather than the signed-in platform's.
    it('follows the default destination for a person who could choose', async () => {
      expect(await templateSide(setup(basePick, { canChoose: true, defaultDestination: 'telegram' }))).toBe('telegram');
    });

    it('leaves the picker on the signed-in platform for everyone else', async () => {
      expect(await templateSide(setup(basePick))).toBeNull();
      expect(await templateSide(setup(basePick, { canChoose: false, defaultDestination: 'telegram' }))).toBeNull();
    });
  });

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
      expect(form.template).toBe('');
    });

    it('seeds the scope from the saved alert defaults', () => {
      // The radius and mode moved to the shared scope picker, but the Alert Defaults preference still
      // has to reach a quick pick the same way it reaches an add dialog. Cleared and re-created here
      // because this spec uses the real AlertDefaultsService, which reads localStorage — without the
      // clear it inherits whatever another suite left behind.
      localStorage.clear();
      setup(basePick);

      expect(component.scope()).toEqual({ mode: 'profile' });
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
});
