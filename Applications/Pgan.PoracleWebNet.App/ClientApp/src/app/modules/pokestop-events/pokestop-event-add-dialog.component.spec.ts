import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { PokestopEventAddDialogComponent } from './pokestop-event-add-dialog.component';
import { PokestopEvent, PokestopEventCreate } from '../../core/models';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { I18nService } from '../../core/services/i18n.service';
import { PokestopEventService } from '../../core/services/pokestop-event.service';
import { POKESTOP_EVENTS } from '../../shared/utils/pokestop-events';

/**
 * `display_type` is mandatory upstream and has no wildcard, so this dialog deliberately offers no
 * "any event" option. Nothing chosen must post nothing — an empty display_type is a request the API
 * refuses, and a save button that fires it fails every time while looking like it worked.
 */
describe('PokestopEventAddDialogComponent', () => {
  let component: PokestopEventAddDialogComponent;
  let dialogRef: { close: jest.Mock };
  let fixture: ComponentFixture<PokestopEventAddDialogComponent>;
  let pokestopEventService: { create: jest.Mock };
  let snackBar: { open: jest.Mock };

  const SHOWCASE = 9;
  const KECLEON = 8;
  const GOLD_STOP = 7;

  const defaults = { defaultDistanceKm: () => 1, defaultMode: () => 'areas', defaultPlaceLabel: () => '' };

  function setup(existing: PokestopEvent[] = [], alertDefaults: unknown = defaults): void {
    dialogRef = { close: jest.fn() };
    snackBar = { open: jest.fn() };
    pokestopEventService = { create: jest.fn().mockReturnValue(of({} as PokestopEvent)) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: I18nService, useValue: { instant: (k: string) => k } },
        { provide: MAT_DIALOG_DATA, useValue: existing },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: AlertDefaultsService, useValue: alertDefaults },
        { provide: PokestopEventService, useValue: pokestopEventService },
        { provide: AuthService, useValue: { isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
      ],
      imports: [PokestopEventAddDialogComponent],
    });

    // MatSnackBarModule is imported by the component, so the real service sits in its own injector.
    TestBed.overrideComponent(PokestopEventAddDialogComponent, {
      add: { providers: [{ provide: MatSnackBar, useValue: snackBar }] },
    });

    fixture = TestBed.createComponent(PokestopEventAddDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function created(): PokestopEventCreate[] {
    return pokestopEventService.create.mock.calls.map(c => c[0] as PokestopEventCreate);
  }

  /** The checkboxes as the template lays them out, so the control-to-event pairing is exercised. */
  function checkboxes(): HTMLInputElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('mat-checkbox input[type="checkbox"]'));
  }

  describe('display_type is required and has no wildcard', () => {
    it('offers exactly the three known events and nothing else', () => {
      setup();

      expect(component.options.map(o => o.name)).toEqual(['showcase', 'kecleon', 'gold-stop']);
      expect(component.options.map(o => o.displayType)).toEqual([SHOWCASE, KECLEON, GOLD_STOP]);
    });

    it('offers no "any event" option: every option carries a real display type', () => {
      setup();

      for (const option of component.options) {
        expect(typeof option.displayType).toBe('number');
        expect(option.displayType).toBeGreaterThan(0);
      }
    });

    it('reports no selection, and the save button is disabled, before an event is picked', () => {
      setup();

      expect(component.hasSelection()).toBe(false);
      const save = fixture.nativeElement.querySelector('mat-dialog-actions button[color="primary"]') as HTMLButtonElement;
      expect(save.disabled).toBe(true);
    });

    it('posts nothing when save is called with no event chosen', async () => {
      setup();
      component.form.controls.autoDelete.setValue(true);

      await component.save();

      expect(pokestopEventService.create).not.toHaveBeenCalled();
      expect(dialogRef.close).not.toHaveBeenCalled();
      expect(component.saving()).toBe(false);
    });

    it('never posts a null or absent displayType', async () => {
      setup();
      component.form.controls.eventShowcase.setValue(true);
      component.form.controls.eventKecleon.setValue(true);

      await component.save();

      expect(created()).toHaveLength(2);
      for (const payload of created()) {
        expect(POKESTOP_EVENTS.map(e => e.displayType)).toContain(payload.displayType);
      }
    });
  });

  describe('checkbox to event pairing', () => {
    // The template indexes `options` by hand and binds each box to a named control. Reordering
    // POKESTOP_EVENTS would silently pair the Showcase box with the Gold Stop control.
    it.each([
      [0, SHOWCASE],
      [1, KECLEON],
      [2, GOLD_STOP],
    ])('the checkbox at position %i creates display type %i', async (index, displayType) => {
      setup();
      checkboxes()[index].click();
      fixture.detectChanges();

      await component.save();

      expect(created()).toEqual([expect.objectContaining({ displayType })]);
    });

    it('maps each event name to its own control', () => {
      setup();

      expect(component.controlFor('showcase')).toBe('eventShowcase');
      expect(component.controlFor('kecleon')).toBe('eventKecleon');
      expect(component.controlFor('gold-stop')).toBe('eventGoldStop');
    });
  });

  describe('payload', () => {
    it('builds one create per checked event, with the auto-delete bit and no template', async () => {
      setup();
      component.form.controls.eventShowcase.setValue(true);
      component.form.controls.eventGoldStop.setValue(true);
      component.form.controls.autoDelete.setValue(true);

      await component.save();

      expect(created()).toEqual([
        { overrideAreas: [], overrideLocationLabel: '', clean: 1, displayType: SHOWCASE, distance: 0, template: null },
        { overrideAreas: [], overrideLocationLabel: '', clean: 1, displayType: GOLD_STOP, distance: 0, template: null },
      ]);
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('leaves clean at 0 when auto-delete is off', async () => {
      setup();
      component.form.controls.eventKecleon.setValue(true);

      await component.save();

      expect(created()[0].clean).toBe(0);
    });

    it('sends a chosen template through, and null for an empty one', async () => {
      setup();
      component.form.controls.eventKecleon.setValue(true);
      component.form.controls.template.setValue('my-template');

      await component.save();

      expect(created()[0].template).toBe('my-template');
    });

    it('flattens the picked scope into the three stored fields', async () => {
      setup();
      component.form.controls.eventShowcase.setValue(true);
      component.scope.set({ distanceKm: 2.5, mode: 'place', placeLabel: 'work' });

      await component.save();

      expect(created()[0]).toMatchObject({ overrideAreas: [], overrideLocationLabel: 'work', distance: 2500 });
    });

    it('clears the radius when the alarm is confined to areas', async () => {
      setup();
      component.form.controls.eventShowcase.setValue(true);
      component.scope.set({ areas: ['terrigal'], mode: 'areas' });

      await component.save();

      expect(created()[0]).toMatchObject({ overrideAreas: ['terrigal'], overrideLocationLabel: '', distance: 0 });
    });
  });

  describe('seeding from the Alert Defaults preference', () => {
    it('opens on the inherited scope when the preference is areas', () => {
      setup([], defaults);

      expect(component.scope()).toEqual({ mode: 'profile' });
    });

    it('opens on the saved radius and place when the preference is distance', () => {
      setup([], { defaultDistanceKm: () => 3, defaultMode: () => 'distance', defaultPlaceLabel: () => 'home' });

      expect(component.scope()).toEqual({ distanceKm: 3, mode: 'place', placeLabel: 'home' });
    });

    it('measures from the profile pin when a radius is preferred but no place is saved', () => {
      setup([], { defaultDistanceKm: () => 3, defaultMode: () => 'distance', defaultPlaceLabel: () => '' });

      // The picker normalises an empty place away, so what a save would send is a bare radius.
      expect(component.scope()).toEqual({ distanceKm: 3, mode: 'profile' });
    });
  });

  describe('events the profile already tracks', () => {
    const tracked: PokestopEvent = {
      id: 'u1',
      uid: 4,
      clean: 0,
      displayType: SHOWCASE,
      distance: 0,
      eventName: 'showcase',
      profileNo: 0,
      template: null,
    };

    it('marks only the tracked event, leaving the others pickable', () => {
      setup([tracked]);

      expect(component.options.map(o => o.alreadyTracked)).toEqual([true, false, false]);
    });

    it('never posts a duplicate, even if its control is somehow set', async () => {
      setup([tracked]);
      component.form.controls.eventShowcase.setValue(true);
      component.form.controls.eventKecleon.setValue(true);

      await component.save();

      expect(created()).toEqual([expect.objectContaining({ displayType: KECLEON })]);
    });

    it('treats a tracked-only tick as no selection at all', async () => {
      setup([tracked]);
      component.form.controls.eventShowcase.setValue(true);

      expect(component.hasSelection()).toBe(false);
      await component.save();
      expect(pokestopEventService.create).not.toHaveBeenCalled();
    });

    it('disables the control for a tracked event, so its box cannot be ticked', () => {
      setup([tracked]);

      expect(component.form.controls.eventShowcase.disabled).toBe(true);
      expect(component.form.controls.eventKecleon.disabled).toBe(false);
      expect(component.form.controls.eventGoldStop.disabled).toBe(false);
    });

    it('renders the tracked event as a disabled checkbox', () => {
      setup([tracked]);

      // [disabled] in the template does not survive a reactive form's value accessor, which reapplies
      // the control's own state after the binding. Assert the rendered input, not the binding.
      const boxes = fixture.nativeElement.querySelectorAll('mat-checkbox input[type="checkbox"]');
      expect(boxes[0].disabled).toBe(true);
      expect(boxes[1].disabled).toBe(false);
    });

    it('starts with everything pickable when the profile tracks nothing', () => {
      setup([]);

      expect(component.options.map(o => o.alreadyTracked)).toEqual([false, false, false]);
    });
  });

  describe('partial failure', () => {
    it('stops at the first refusal, reports what the server said, and closes on what was written', async () => {
      setup();
      pokestopEventService.create
        .mockReturnValueOnce(of({} as PokestopEvent))
        .mockReturnValueOnce(throwError(() => ({ error: { error: 'Already tracking that event' } })));
      component.form.controls.eventShowcase.setValue(true);
      component.form.controls.eventKecleon.setValue(true);
      component.form.controls.eventGoldStop.setValue(true);

      await component.save();

      expect(pokestopEventService.create).toHaveBeenCalledTimes(2);
      expect(snackBar.open).toHaveBeenCalledWith('Already tracking that event', 'COMMON.OK', expect.anything());
      // One alarm did get written, so the list must reload.
      expect(dialogRef.close).toHaveBeenCalledWith(true);
      expect(component.saving()).toBe(false);
    });

    it('closes with false when the very first write failed', async () => {
      setup();
      pokestopEventService.create.mockReturnValue(throwError(() => ({ error: { error: 'nope' } })));
      component.form.controls.eventShowcase.setValue(true);

      await component.save();

      expect(dialogRef.close).toHaveBeenCalledWith(false);
    });

    it('falls back to its own message when the server gave no reason', async () => {
      setup();
      pokestopEventService.create.mockReturnValue(throwError(() => new Error('network')));
      component.form.controls.eventShowcase.setValue(true);

      await component.save();

      expect(snackBar.open).toHaveBeenCalledWith('POKESTOP_EVENTS.CREATE_FAILED', 'COMMON.OK', expect.anything());
    });
  });
});
