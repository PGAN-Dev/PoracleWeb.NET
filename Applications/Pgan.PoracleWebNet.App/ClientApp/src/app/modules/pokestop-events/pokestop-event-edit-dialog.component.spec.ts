import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { PokestopEventEditDialogComponent } from './pokestop-event-edit-dialog.component';
import { PokestopEvent, PokestopEventUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { I18nService } from '../../core/services/i18n.service';
import { PokestopEventService } from '../../core/services/pokestop-event.service';

/**
 * Every write re-keys the row upstream, so this dialog gets one shot at the uid it was opened with:
 * a payload that drops a field the user did not touch loses it for good.
 */
describe('PokestopEventEditDialogComponent', () => {
  let component: PokestopEventEditDialogComponent;
  let dialogRef: { close: jest.Mock };
  let fixture: ComponentFixture<PokestopEventEditDialogComponent>;
  let pokestopEventService: { update: jest.Mock };
  let snackBar: { open: jest.Mock };

  const SHOWCASE = 9;
  const KECLEON = 8;
  const GOLD_STOP = 7;

  const base: PokestopEvent = {
    id: 'u1',
    overrideAreas: null,
    overrideLocationLabel: null,
    uid: 42,
    clean: 0,
    displayType: SHOWCASE,
    distance: 0,
    eventName: 'showcase',
    profileNo: 0,
    template: null,
  };

  function setup(event: Partial<PokestopEvent> = {}): void {
    dialogRef = { close: jest.fn() };
    snackBar = { open: jest.fn() };
    pokestopEventService = { update: jest.fn().mockReturnValue(of(void 0)) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: I18nService, useValue: { instant: (k: string) => k } },
        { provide: MAT_DIALOG_DATA, useValue: { ...base, ...event } },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: PokestopEventService, useValue: pokestopEventService },
        { provide: AuthService, useValue: { isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
      ],
      imports: [PokestopEventEditDialogComponent],
    });

    // MatSnackBarModule is imported by the component, so the real service sits in its own injector.
    TestBed.overrideComponent(PokestopEventEditDialogComponent, {
      add: { providers: [{ provide: MatSnackBar, useValue: snackBar }] },
    });

    fixture = TestBed.createComponent(PokestopEventEditDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function saved(): PokestopEventUpdate {
    return pokestopEventService.update.mock.calls[0][1] as PokestopEventUpdate;
  }

  describe('seeding from the rule it was opened with', () => {
    it('opens on the rule’s own event', () => {
      setup({ displayType: KECLEON });

      expect(component.form.controls.displayType.value).toBe(KECLEON);
    });

    it('opens on the rule’s template', () => {
      setup({ template: 'my-template' });

      expect(component.form.controls.template.value).toBe('my-template');
    });

    it('reads a missing template as an empty box rather than the string "null"', () => {
      setup({ template: null });

      expect(component.form.controls.template.value).toBe('');
    });

    it('reads the auto-delete toggle off bit 1 alone', () => {
      setup({ clean: 0 });
      expect(component.form.controls.autoDelete.value).toBe(false);

      setup({ clean: 1 });
      expect(component.form.controls.autoDelete.value).toBe(true);

      // Bit 4 is the summary flag; it is not this toggle.
      setup({ clean: 4 });
      expect(component.form.controls.autoDelete.value).toBe(false);

      setup({ clean: 5 });
      expect(component.form.controls.autoDelete.value).toBe(true);
    });

    it('offers all three events, and only those, to switch to', () => {
      setup();

      expect(component.options.map(o => o.displayType)).toEqual([SHOWCASE, KECLEON, GOLD_STOP]);
    });

    it('renders one option per event, with no wildcard row', () => {
      setup();
      const select = fixture.nativeElement.querySelector('mat-select') as HTMLElement;
      select.querySelector<HTMLElement>('.mat-mdc-select-trigger')!.click();
      fixture.detectChanges();

      const options = Array.from(document.querySelectorAll('mat-option')).map(o => o.textContent!.trim());
      expect(options).toEqual(['INVASIONS.EVENT_TYPES.SHOWCASE', 'INVASIONS.EVENT_TYPES.KECLEON', 'INVASIONS.EVENT_TYPES.GOLD_STOP']);
    });
  });

  describe('seeding the delivery scope', () => {
    it('opens on the areas the rule is confined to', () => {
      setup({ overrideAreas: ['terrigal'], distance: 0 });

      expect(component.scope()).toEqual({ areas: ['terrigal'], mode: 'areas' });
    });

    it('opens on the place and radius the rule measures from', () => {
      setup({ overrideLocationLabel: 'work', distance: 2500 });

      expect(component.scope()).toEqual({ distanceKm: 2.5, mode: 'place', placeLabel: 'work' });
    });

    it('opens on a bare radius as measured from the profile pin', () => {
      setup({ distance: 1000 });

      expect(component.scope()).toEqual({ distanceKm: 1, mode: 'profile' });
    });
  });

  describe('the update it emits', () => {
    it('writes the whole rule back against the uid it was opened with', () => {
      setup({ overrideAreas: ['terrigal'], displayType: KECLEON, template: 'my-template' });

      component.save();

      expect(pokestopEventService.update).toHaveBeenCalledWith(42, {
        overrideAreas: ['terrigal'],
        overrideLocationLabel: '',
        clean: 0,
        displayType: KECLEON,
        distance: 0,
        template: 'my-template',
      });
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('carries a switched event through', () => {
      setup({ displayType: SHOWCASE });
      component.form.controls.displayType.setValue(GOLD_STOP);

      component.save();

      expect(saved().displayType).toBe(GOLD_STOP);
    });

    it('keeps the rule’s own event when the control was somehow cleared', () => {
      setup({ displayType: KECLEON });
      component.form.controls.displayType.setValue(null);

      component.save();

      expect(saved().displayType).toBe(KECLEON);
    });

    it('sends an emptied template as an empty string, not null', () => {
      // null means "keep what is stored" on the write path, so clearing the box has to say so.
      setup({ template: 'my-template' });
      component.form.controls.template.setValue('');

      component.save();

      expect(saved().template).toBe('');
    });

    it('writes a rescoped rule as the three stored fields', () => {
      setup({ overrideAreas: ['terrigal'] });
      component.scope.set({ distanceKm: 2, mode: 'place', placeLabel: 'work' });

      component.save();

      expect(saved()).toMatchObject({ overrideAreas: [], overrideLocationLabel: 'work', distance: 2000 });
    });

    it('clears a place override when the rule is confined to areas instead', () => {
      setup({ overrideLocationLabel: 'work', distance: 2000 });
      component.scope.set({ areas: ['gosford'], mode: 'areas' });

      component.save();

      expect(saved()).toMatchObject({ overrideAreas: ['gosford'], overrideLocationLabel: '', distance: 0 });
    });
  });

  describe('clean bitmask (#292)', () => {
    it('sets bit 1 when auto-delete is turned on', () => {
      setup({ clean: 0 });
      component.form.controls.autoDelete.setValue(true);

      component.save();

      expect(saved().clean).toBe(1);
    });

    it('clears bit 1 when auto-delete is turned off', () => {
      setup({ clean: 1 });
      component.form.controls.autoDelete.setValue(false);

      component.save();

      expect(saved().clean).toBe(0);
    });

    it('preserves bits this dialog has no control for (clean 6 -> 7)', () => {
      // 6 = edit-in-place (2) + summary (4). Turning auto-delete on must keep both.
      setup({ clean: 6 });
      component.form.controls.autoDelete.setValue(true);

      component.save();

      expect(saved().clean).toBe(7);
    });

    it('preserves the summary bit when auto-delete is turned off (clean 5 -> 4)', () => {
      setup({ clean: 5 });
      component.form.controls.autoDelete.setValue(false);

      component.save();

      expect(saved().clean).toBe(4);
    });
  });

  describe('a refused update', () => {
    it('says what the server said and leaves the dialog open', () => {
      setup();
      pokestopEventService.update.mockReturnValue(throwError(() => ({ error: { error: 'Already tracking Kecleon' } })));

      component.save();

      expect(snackBar.open).toHaveBeenCalledWith('Already tracking Kecleon', 'COMMON.OK', expect.anything());
      expect(dialogRef.close).not.toHaveBeenCalled();
      // Re-enabled, or the user is left with a dialog they cannot retry from.
      expect(component.saving()).toBe(false);
    });

    it('falls back to its own message when the server gave no reason', () => {
      setup();
      pokestopEventService.update.mockReturnValue(throwError(() => new Error('network')));

      component.save();

      expect(snackBar.open).toHaveBeenCalledWith('POKESTOP_EVENTS.UPDATE_FAILED', 'COMMON.OK', expect.anything());
    });
  });
});
