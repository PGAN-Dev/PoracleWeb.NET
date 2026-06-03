import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';

import { LureEditDialogComponent } from './lure-edit-dialog.component';
import { Lure, LureUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { LureService } from '../../core/services/lure.service';

describe('LureEditDialogComponent', () => {
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
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: LureService, useValue: lureService },
        { provide: AuthService, useValue: { isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
      ],
      imports: [LureEditDialogComponent, TranslateModule.forRoot()],
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
});
