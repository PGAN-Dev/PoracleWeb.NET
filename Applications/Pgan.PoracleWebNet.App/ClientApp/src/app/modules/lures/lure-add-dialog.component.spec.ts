import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';

import { LureAddDialogComponent } from './lure-add-dialog.component';
import { Lure, LureCreate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { LureService } from '../../core/services/lure.service';

describe('LureAddDialogComponent', () => {
  let component: LureAddDialogComponent;
  let dialogRef: { close: jest.Mock };
  let lureService: { create: jest.Mock };

  function setup() {
    dialogRef = { close: jest.fn() };
    lureService = { create: jest.fn().mockReturnValue(of({} as Lure)) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: LureService, useValue: lureService },
        { provide: AuthService, useValue: { isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
      ],
      imports: [LureAddDialogComponent, TranslateModule.forRoot()],
    });

    const fixture = TestBed.createComponent(LureAddDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function createdClean(): number {
    const create = lureService.create.mock.calls[0][0] as LureCreate;
    return create.clean;
  }

  beforeEach(() => setup());

  it('defaults the edit-in-place toggle to off', () => {
    expect(component.form.controls.editInPlace.value).toBe(false);
  });

  it('defaults the clean (auto-delete) toggle to off', () => {
    expect(component.form.controls.clean.value).toBe(false);
  });

  it('composes clean=0 when neither toggle is set', () => {
    component.selectedLureIds.set([501]);
    component.save();
    expect(createdClean()).toBe(0);
  });

  it('composes bit 2 when only edit-in-place is on', () => {
    component.selectedLureIds.set([501]);
    component.form.controls.editInPlace.setValue(true);
    component.save();
    expect(createdClean()).toBe(2);
  });

  it('composes bit 1 when only auto-delete is on', () => {
    component.selectedLureIds.set([501]);
    component.form.controls.clean.setValue(true);
    component.save();
    expect(createdClean()).toBe(1);
  });

  it('composes bits 1|2 = 3 when both toggles are on', () => {
    component.selectedLureIds.set([501]);
    component.form.controls.clean.setValue(true);
    component.form.controls.editInPlace.setValue(true);
    component.save();
    expect(createdClean()).toBe(3);
  });

  it('applies the same composed clean to every selected lure', () => {
    component.selectedLureIds.set([501, 502, 503]);
    component.form.controls.editInPlace.setValue(true);
    component.save();
    expect(lureService.create).toHaveBeenCalledTimes(3);
    for (const call of lureService.create.mock.calls) {
      expect((call[0] as LureCreate).clean).toBe(2);
    }
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('does nothing when no lure types are selected', () => {
    component.form.controls.editInPlace.setValue(true);
    component.save();
    expect(lureService.create).not.toHaveBeenCalled();
  });
});
