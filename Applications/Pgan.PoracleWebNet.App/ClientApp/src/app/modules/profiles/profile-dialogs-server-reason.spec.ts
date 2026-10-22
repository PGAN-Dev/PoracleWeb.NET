import { HttpErrorResponse } from '@angular/common/http';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { ProfileAddDialogComponent } from './profile-add-dialog.component';
import { ProfileDuplicateDialogComponent } from './profile-duplicate-dialog.component';
import { ProfileEditDialogComponent } from './profile-edit-dialog.component';
import { ProfileService } from '../../core/services/profile.service';

/**
 * The API refuses a profile name with a reason ("Profile name must not contain control or text-direction
 * characters."), and the dialogs replaced it with "Failed to create profile", which tells the user nothing
 * they can act on.
 */
describe('profile dialogs show the server reason', () => {
  const reason = 'Profile name must not contain control or text-direction characters.';
  const refused = (error: unknown) => jest.fn(() => throwError(() => new HttpErrorResponse({ error, status: 400 })));

  const setup = <T>(component: Type<T>, service: Record<string, jest.Mock>) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideTranslateService(),
        { provide: ProfileService, useValue: { getAll: jest.fn(() => of([])), ...service } },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: { name: 'Home', profileNo: 1 } },
      ],
      imports: [component],
    });
    const fixture = TestBed.createComponent(component);
    const snackOpen = jest.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open').mockReturnValue({} as never);
    return { component: fixture.componentInstance, snackOpen };
  };

  afterEach(() => jest.restoreAllMocks());

  it('on create', () => {
    const { component, snackOpen } = setup(ProfileAddDialogComponent, { create: refused({ error: reason }) });
    component.profileName = 'evil\u202E';

    component.save();

    expect(snackOpen.mock.calls[0][0]).toBe(reason);
  });

  it('on create, in its own words when the server gave none', () => {
    const { component, snackOpen } = setup(ProfileAddDialogComponent, { create: refused(null) });
    component.profileName = 'Weekend';

    component.save();

    expect(snackOpen.mock.calls[0][0]).toBe('PROFILES.SNACK_FAILED_CREATE');
  });

  it('on rename', () => {
    const { component, snackOpen } = setup(ProfileEditDialogComponent, { update: refused({ error: reason }) });
    component.profileName = 'evil\u202E';

    component.save();

    expect(snackOpen.mock.calls[0][0]).toBe(reason);
  });

  it('on duplicate', () => {
    const { component, snackOpen } = setup(ProfileDuplicateDialogComponent, { duplicate: refused({ error: reason }) });
    component.profileName = 'evil\u202E';

    component.duplicate();

    expect(snackOpen.mock.calls[0][0]).toBe(reason);
  });
});
