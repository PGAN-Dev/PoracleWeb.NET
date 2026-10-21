import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';

import { GeofenceDeleteDialogComponent, GeofenceDeleteDialogData } from './geofence-delete-dialog.component';

describe('GeofenceDeleteDialogComponent', () => {
  let dialogRef: { close: jest.Mock };

  function setup(data: GeofenceDeleteDialogData = { name: 'My Park', count: 3 }) {
    dialogRef = { close: jest.fn() };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideTranslateService(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
      imports: [GeofenceDeleteDialogComponent],
    });
    const fixture = TestBed.createComponent(GeofenceDeleteDialogComponent);
    fixture.detectChanges();
    return { component: fixture.componentInstance, fixture };
  }

  it('preselects deleting the alarms', () => {
    const { component, fixture } = setup();

    expect(component.choice()).toBe('delete');
    const checked = fixture.debugElement.queryAll(By.css('mat-radio-button')).filter(b => b.componentInstance.checked);
    expect(checked.map(b => b.componentInstance.value)).toEqual(['delete']);
  });

  it('closes with delete when confirmed without changing the choice', () => {
    const { component } = setup();

    component.confirm();

    expect(dialogRef.close).toHaveBeenCalledWith('delete');
  });

  it('closes with keep when the user chose to keep them', () => {
    const { component } = setup();

    component.choice.set('keep');
    component.confirm();

    expect(dialogRef.close).toHaveBeenCalledWith('keep');
  });

  it('closes with null on cancel, so nothing is deleted', () => {
    const { component } = setup();

    component.cancel();

    expect(dialogRef.close).toHaveBeenCalledWith(null);
  });

  it('offers both choices', () => {
    const { fixture } = setup();

    const values = fixture.debugElement.queryAll(By.css('mat-radio-button')).map(b => b.componentInstance.value);
    expect(values).toEqual(['delete', 'keep']);
  });
});
