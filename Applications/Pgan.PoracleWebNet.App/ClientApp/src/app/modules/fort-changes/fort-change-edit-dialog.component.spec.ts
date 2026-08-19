import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { FortChangeEditDialogComponent } from './fort-change-edit-dialog.component';
import { FortChange, FortChangeUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { FortChangeService } from '../../core/services/fort-change.service';
import { I18nService } from '../../core/services/i18n.service';

/**
 * The change type list is rebuilt from checkboxes on every save, so anything the dialog cannot draw is
 * a candidate for silent deletion. PoracleNG's `!fort` command accepts six types and this UI drew five:
 * a rule set with `description` lost it the next time its owner changed the radius.
 */
describe('FortChangeEditDialogComponent', () => {
  let component: FortChangeEditDialogComponent;
  let fortChangeService: { update: jest.Mock };

  const base: FortChange = {
    id: 'u1',
    uid: 9,
    changeTypes: [],
    distance: 0,
    fortType: 'everything',
    includeEmpty: false,
    profileNo: 0,
    template: null,
  };

  function setup(fort: Partial<FortChange>) {
    fortChangeService = { update: jest.fn().mockReturnValue(of(void 0)) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { ...base, ...fort } },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: FortChangeService, useValue: fortChangeService },
        { provide: I18nService, useValue: { instant: (k: string) => k } },
        { provide: AuthService, useValue: { isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
      ],
      imports: [FortChangeEditDialogComponent],
    });

    TestBed.overrideComponent(FortChangeEditDialogComponent, {
      add: { providers: [{ provide: MatSnackBar, useValue: { open: jest.fn() } }] },
    });

    component = TestBed.createComponent(FortChangeEditDialogComponent).componentInstance;
  }

  function saved(): string[] {
    return (fortChangeService.update.mock.calls[0][1] as FortChangeUpdate).changeTypes ?? [];
  }

  it('reads a description rule back into its checkbox', () => {
    setup({ changeTypes: ['name', 'description'] });

    expect(component.form.controls.changeTypeDescription.value).toBe(true);
  });

  it('saves description when it is ticked', () => {
    setup({ changeTypes: ['description'] });
    component.save();

    expect(saved()).toContain('description');
  });

  it('drops description when it is unticked', () => {
    // The legitimate twin: preserving unknown types must not make a ticked box unremovable.
    setup({ changeTypes: ['name', 'description'] });
    component.form.controls.changeTypeDescription.setValue(false);
    component.save();

    expect(saved()).toEqual(['name']);
  });

  it('carries through a change type this dialog has no box for', () => {
    // Whatever PoracleNG grows next. Losing it on an unrelated save is the failure mode.
    setup({ changeTypes: ['name', 'teleport'] });
    component.save();

    expect(saved()).toEqual(['name', 'teleport']);
  });

  it('leaves an ordinary rule exactly as it was', () => {
    setup({ changeTypes: ['name', 'location'] });
    component.save();

    expect(saved().sort()).toEqual(['location', 'name']);
  });
});
