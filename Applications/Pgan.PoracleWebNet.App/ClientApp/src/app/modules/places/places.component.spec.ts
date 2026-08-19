import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { PlacesComponent } from './places.component';
import { ConfigService } from '../../core/services/config.service';
import { PlacesService } from '../../core/services/places.service';

describe('PlacesComponent', () => {
  let dialog: { open: jest.Mock };
  let places: {
    add: jest.Mock;
    load: jest.Mock;
    named: jest.Mock;
    pin: jest.Mock;
    remove: jest.Mock;
  };
  let snackBar: { open: jest.Mock };

  /** Queues what each successive dialog.open() should resolve to. */
  function queueDialogResults(...results: unknown[]): void {
    results.forEach(result => dialog.open.mockReturnValueOnce({ afterClosed: () => of(result) }));
  }

  function create(): PlacesComponent {
    dialog = { open: jest.fn() };
    snackBar = { open: jest.fn() };
    places = {
      named: jest.fn().mockReturnValue([{ label: 'work', latitude: 1, longitude: 2 }]),
      add: jest.fn().mockReturnValue(of({ named: [], default: null })),
      load: jest.fn().mockReturnValue(of({ named: [], default: null })),
      pin: jest.fn().mockReturnValue({ label: '', latitude: 3, longitude: 4 }),
      remove: jest.fn().mockReturnValue(of(void 0)),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: PlacesService, useValue: places },
        { provide: ConfigService, useValue: { apiHost: 'http://test' } },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
      imports: [PlacesComponent],
    });

    // MatDialogModule is in the component's own imports, so its MatDialog wins over the TestBed
    // provider. Overriding at the component injector is the only level that beats it.
    TestBed.overrideComponent(PlacesComponent, {
      set: {
        providers: [
          { provide: MatDialog, useValue: dialog },
          { provide: MatSnackBar, useValue: snackBar },
        ],
      },
    });

    const component = TestBed.createComponent(PlacesComponent).componentInstance;
    component.ngOnInit();
    return component;
  }

  it('borrows the location dialog as a picker rather than moving the profile pin', () => {
    // Without pickOnly the location dialog saves whatever point is chosen as the user's pin, so
    // naming a place would quietly relocate every alarm that has no override.
    const component = create();
    queueDialogResults(undefined);

    component.addPlace();

    expect(dialog.open.mock.calls[0][1].data).toMatchObject({ pickOnly: true });
  });

  it('saves the place once a point is picked and a name given', () => {
    const component = create();
    queueDialogResults({ latitude: 10, longitude: 20 }, 'gym');

    component.addPlace();

    expect(places.add).toHaveBeenCalledWith({ label: 'gym', latitude: 10, longitude: 20 });
  });

  it('saves nothing when the naming step is cancelled', () => {
    // ConfirmDialog's prompt closes with false on cancel, which is falsy in the same way an empty
    // name is: both mean no place.
    const component = create();
    queueDialogResults({ latitude: 10, longitude: 20 }, false);

    component.addPlace();

    expect(places.add).not.toHaveBeenCalled();
  });

  it('offers the existing names so the prompt can refuse a duplicate', () => {
    const component = create();
    queueDialogResults({ latitude: 10, longitude: 20 }, false);

    component.addPlace();

    expect(dialog.open.mock.calls[1][1].data.promptField.existingNames).toEqual(['work']);
  });

  it('says how many alerts are in the way when a place cannot be deleted', () => {
    // The 409 carries the alarms still pointing at it. "Could not delete" would leave the user with
    // nothing to act on.
    const component = create();
    places.remove.mockReturnValue(
      throwError(() => new HttpErrorResponse({ error: { referencingRules: ['pokemon 7', 'raid 9'] }, status: 409 })),
    );

    component.removePlace({ label: 'work', latitude: 1, longitude: 2 });

    expect(snackBar.open).toHaveBeenCalledWith('WHERE.PLACE_IN_USE', expect.anything(), expect.anything());
  });

  it('reports a plain failure when the delete fails for any other reason', () => {
    const component = create();
    places.remove.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    component.removePlace({ label: 'work', latitude: 1, longitude: 2 });

    expect(snackBar.open).toHaveBeenCalledWith('WHERE.PLACE_DELETE_ERROR', expect.anything(), expect.anything());
  });

  it('deletes only after the confirmation is accepted', () => {
    const component = create();
    queueDialogResults(false);

    component.confirmRemove({ label: 'work', latitude: 1, longitude: 2 });

    expect(places.remove).not.toHaveBeenCalled();
  });
});
