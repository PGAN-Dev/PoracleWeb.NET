import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { LureEditDialogComponent } from './lure-edit-dialog.component';
import { Lure, LureUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { LureService } from '../../core/services/lure.service';

describe('LureEditDialogComponent', () => {
  let webhookSession = false;
  let sessionType = 'webhook';
  let component: LureEditDialogComponent;
  let dialogRef: { close: jest.Mock };
  let lureService: { update: jest.Mock };

  const baseLure: Lure = {
    id: 'lure-1',
    uid: 42,
    clean: 0,
    distance: 0,
    lureId: 501,
    ping: null,
    profileNo: 1,
    template: null,
  };

  function setup(data: Lure) {
    dialogRef = { close: jest.fn() };
    lureService = { update: jest.fn().mockReturnValue(of(void 0)) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        // delivery-preview links to /areas with routerLink, so a router is required.
        provideRouter([]),
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: LureService, useValue: lureService },
        { provide: AuthService, useValue: { isImpersonating: () => webhookSession, user: () => ({ type: sessionType }) } },
      ],
      imports: [LureEditDialogComponent],
    });

    const fixture = TestBed.createComponent(LureEditDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function savedClean(): number {
    const update = lureService.update.mock.calls[0][1] as LureUpdate;
    return update.clean as number;
  }

  describe('form init from clean bits', () => {
    it('initializes both toggles off when clean=0', () => {
      setup({ ...baseLure, clean: 0 });
      expect(component.form.controls.clean.value).toBe(false);
      expect(component.form.controls.editInPlace.value).toBe(false);
    });

    it('initializes auto-delete on, edit off when clean=1', () => {
      setup({ ...baseLure, clean: 1 });
      expect(component.form.controls.clean.value).toBe(true);
      expect(component.form.controls.editInPlace.value).toBe(false);
    });

    it('initializes edit-in-place on from bit 2 when clean=2', () => {
      setup({ ...baseLure, clean: 2 });
      expect(component.form.controls.clean.value).toBe(false);
      expect(component.form.controls.editInPlace.value).toBe(true);
    });

    it('initializes both on when clean=3', () => {
      setup({ ...baseLure, clean: 3 });
      expect(component.form.controls.clean.value).toBe(true);
      expect(component.form.controls.editInPlace.value).toBe(true);
    });

    it('initializes edit-in-place on from bit 2 even when a summary bit is also set (clean=6)', () => {
      setup({ ...baseLure, clean: 6 });
      expect(component.form.controls.clean.value).toBe(false);
      expect(component.form.controls.editInPlace.value).toBe(true);
    });
  });

  describe('save composes bit 2 while preserving other bits', () => {
    it('sets bit 2 when toggled on, leaving auto-delete off (clean 0 -> 2)', () => {
      setup({ ...baseLure, clean: 0 });
      component.form.controls.editInPlace.setValue(true);
      component.save();
      expect(savedClean()).toBe(2);
    });

    it('combines auto-delete + edit (clean 0 -> 3)', () => {
      setup({ ...baseLure, clean: 0 });
      component.form.controls.clean.setValue(true);
      component.form.controls.editInPlace.setValue(true);
      component.save();
      expect(savedClean()).toBe(3);
    });

    it('clears bit 2 when toggled off (clean 3 -> 1)', () => {
      setup({ ...baseLure, clean: 3 });
      component.form.controls.editInPlace.setValue(false);
      component.save();
      expect(savedClean()).toBe(1);
    });

    it('preserves an unsurfaced summary bit when editing (clean 5 -> 7)', () => {
      // clean=5 => auto-delete (1) + summary (4); turning edit-in-place on must keep bit 4.
      setup({ ...baseLure, clean: 5 });
      component.form.controls.editInPlace.setValue(true);
      component.save();
      expect(savedClean()).toBe(7);
    });

    it('preserves the summary bit when turning auto-delete off (clean 5 -> 4)', () => {
      setup({ ...baseLure, clean: 5 });
      component.form.controls.clean.setValue(false);
      component.save();
      expect(savedClean()).toBe(4);
    });

    it('passes the composed clean and uid to the service', () => {
      setup({ ...baseLure, clean: 0 });
      component.form.controls.editInPlace.setValue(true);
      component.save();
      expect(lureService.update).toHaveBeenCalledWith(42, expect.objectContaining({ clean: 2 }));
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });
  });

  describe('Mention field', () => {
    // The dialog's only condition for the Mention field is the impersonation snapshot: PoracleNG 5.4.0
    // always stores a ping, so there is no server-config dependency left to consult.
    it('is offered on a webhook alarm', () => {
      webhookSession = true;
      sessionType = 'webhook';
      setup({ ...baseLure });
      expect(TestBed.createComponent(LureEditDialogComponent).componentInstance.showMention).toBe(true);
    });

    it('is offered while managing a Discord channel', () => {
      webhookSession = true;
      sessionType = 'discord:channel';
      setup({ ...baseLure });
      expect(TestBed.createComponent(LureEditDialogComponent).componentInstance.showMention).toBe(true);
    });

    it('is hidden while managing a Telegram group', () => {
      webhookSession = true;
      sessionType = 'telegram:group';
      setup({ ...baseLure });
      expect(TestBed.createComponent(LureEditDialogComponent).componentInstance.showMention).toBe(false);
    });

    it('is hidden on a personal alarm', () => {
      webhookSession = false;
      sessionType = 'discord:user';
      setup({ ...baseLure });
      expect(TestBed.createComponent(LureEditDialogComponent).componentInstance.showMention).toBe(false);
    });
  });

  /**
   * The dialog offers 0.1 to 100 km, but production holds rules stored far past that. Editing something
   * else on one of them has to save with the radius it already has.
   */
  describe('a radius stored past the dialog range', () => {
    it('saves another change and keeps the stored radius', () => {
      setup({ ...baseLure, distance: 500000 });

      expect(component.storedRadiusKm).toBe(500);
      expect(component.radiusValid()).toBe(true);

      component.form.controls.clean.setValue(true);
      component.save();

      const update = lureService.update.mock.calls[0][1] as LureUpdate;
      expect(update.distance).toBe(500000);
      expect(update.clean).toBe(1);
    });
  });

  /** Rounding to a tenth of a km rewrote a stored radius whenever any other field was saved. */
  describe('a stored radius survives an edit to another field', () => {
    it.each([
      ['a 40 m pin radius', { overrideLocationLabel: '', distance: 40 }],
      ['a 30 m place radius', { overrideLocationLabel: 'work', distance: 30 }],
      ['a 250 m pin radius', { overrideLocationLabel: '', distance: 250 }],
      ['a 1234 m pin radius', { overrideLocationLabel: '', distance: 1234 }],
    ])('%s', (_name, stored) => {
      setup({ ...baseLure, ...stored });

      component.form.controls.clean.setValue(true);
      component.save();

      const update = lureService.update.mock.calls[0][1] as LureUpdate;
      expect(update.distance).toBe(stored.distance);
      expect(update.overrideLocationLabel).toBe(stored.overrideLocationLabel);
    });
  });
});
