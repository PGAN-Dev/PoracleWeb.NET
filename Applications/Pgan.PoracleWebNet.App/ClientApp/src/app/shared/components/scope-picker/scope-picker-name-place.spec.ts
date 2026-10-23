import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { ScopePickerComponent } from './scope-picker.component';
import { ConfigService } from '../../../core/services/config.service';
import { PlacesService } from '../../../core/services/places.service';

/**
 * Naming a place from inside an add or edit dialog is the sibling of Areas & Places' "Add a place" (flows
 * D7, D8): it asks with the same rules, and a refusal says why instead of being swallowed, which used to
 * leave the alarm aimed at nothing with no word about it.
 */
describe('ScopePickerComponent naming a place', () => {
  let dialog: { open: jest.Mock };
  let snackBar: { open: jest.Mock };
  let places: { add: jest.Mock; load: jest.Mock; named: ReturnType<typeof signal>; pin: () => null };

  function create(): ScopePickerComponent {
    dialog = { open: jest.fn() };
    snackBar = { open: jest.fn() };
    places = {
      named: signal([{ label: 'Work', latitude: 1, longitude: 2 }]),
      add: jest.fn().mockReturnValue(of({ named: [], default: null })),
      load: jest.fn().mockReturnValue(of({ named: [], default: null })),
      pin: () => null,
    };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test' } },
        { provide: PlacesService, useValue: places },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
      imports: [ScopePickerComponent],
    });
    const fixture = TestBed.createComponent(ScopePickerComponent);
    fixture.componentRef.setInput('scope', { distanceKm: 1, mode: 'profile' });
    return fixture.componentInstance;
  }

  function addPlace(picker: ScopePickerComponent, ...results: unknown[]): void {
    results.forEach(result => dialog.open.mockReturnValueOnce({ afterClosed: () => of(result) }));
    picker.onTargetChange('__add-place__');
  }

  it('asks for the name with the same rules as Areas & Places', () => {
    const picker = create();

    addPlace(picker, { latitude: 1, longitude: 2 }, false);

    expect(dialog.open.mock.calls[1][1].data.promptField).toMatchObject({
      refuseHiddenCharacters: true,
      conflictKey: 'WHERE.PLACE_NAME_CONFLICT',
      existingNames: ['Work'],
      maxLength: 64,
    });
  });

  it('selects a place it saved', () => {
    const picker = create();

    addPlace(picker, { latitude: 1, longitude: 2 }, 'Gym');

    expect(places.add).toHaveBeenCalledWith({ label: 'Gym', latitude: 1, longitude: 2 });
    expect(picker.placeLabel()).toBe('Gym');
  });

  it('says why a name was refused, and selects nothing', () => {
    const picker = create();
    places.add.mockReturnValue(
      throwError(() => new HttpErrorResponse({ error: { error: 'You already have a place called Gym.' }, status: 400 })),
    );

    addPlace(picker, { latitude: 1, longitude: 2 }, 'Gym');

    expect(snackBar.open).toHaveBeenCalledWith('You already have a place called Gym.', expect.anything(), expect.anything());
    expect(picker.placeLabel()).not.toBe('Gym');
  });
});
