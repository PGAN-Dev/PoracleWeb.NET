import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { PlacesSectionComponent } from './places-section.component';
import { ConfigService } from '../../../core/services/config.service';
import { PlacesService } from '../../../core/services/places.service';

describe('PlacesSectionComponent', () => {
  let dialog: { open: jest.Mock };
  let places: {
    add: jest.Mock;
    canEdit: jest.Mock;
    load: jest.Mock;
    move: jest.Mock;
    named: jest.Mock;
    pin: jest.Mock;
    remove: jest.Mock;
  };
  let snackBar: { open: jest.Mock };

  /** Queues what each successive dialog.open() should resolve to. */
  function queueDialogResults(...results: unknown[]): void {
    results.forEach(result => dialog.open.mockReturnValueOnce({ afterClosed: () => of(result) }));
  }

  function create(): PlacesSectionComponent {
    dialog = { open: jest.fn() };
    snackBar = { open: jest.fn() };
    places = {
      named: jest.fn().mockReturnValue([{ label: 'work', latitude: 1, longitude: 2 }]),
      add: jest.fn().mockReturnValue(of({ named: [], default: null })),
      canEdit: jest.fn().mockReturnValue(true),
      load: jest.fn().mockReturnValue(of({ named: [], default: null })),
      move: jest.fn().mockReturnValue(of({ named: [], default: null })),
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
      imports: [PlacesSectionComponent],
    });

    // MatDialogModule is in the component's own imports, so its MatDialog wins over the TestBed
    // provider. Overriding at the component injector is the only level that beats it.
    TestBed.overrideComponent(PlacesSectionComponent, {
      set: {
        providers: [
          { provide: MatDialog, useValue: dialog },
          { provide: MatSnackBar, useValue: snackBar },
        ],
      },
    });

    const component = TestBed.createComponent(PlacesSectionComponent).componentInstance;
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

  it('opens the picker at the place it is moving, and sends the point back under the same label', () => {
    // The label is what every alarm points at, so a move must not touch it. That is the whole reason
    // this exists rather than delete-and-re-add, which 409s while an alarm still references it.
    const component = create();
    queueDialogResults({ latitude: 9.5, longitude: 8.5 });

    component.movePlace({ label: 'work', latitude: 1, longitude: 2 });

    expect(dialog.open).toHaveBeenCalledWith(expect.anything(), {
      data: { latitude: 1, longitude: 2, pickOnly: true },
    });
    expect(places.move).toHaveBeenCalledWith('work', 9.5, 8.5);
    expect(snackBar.open).toHaveBeenCalledWith('WHERE.PLACE_MOVED', expect.anything(), expect.anything());
  });

  it('writes nothing when the picker is dismissed', () => {
    const component = create();
    queueDialogResults(undefined);

    component.movePlace({ label: 'work', latitude: 1, longitude: 2 });

    expect(places.move).not.toHaveBeenCalled();
  });

  it('reports a failed move rather than leaving the card looking changed', () => {
    const component = create();
    queueDialogResults({ latitude: 9.5, longitude: 8.5 });
    places.move.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 501 })));

    component.movePlace({ label: 'work', latitude: 1, longitude: 2 });

    expect(snackBar.open).toHaveBeenCalledWith('WHERE.PLACE_MOVE_ERROR', expect.anything(), expect.anything());
  });
});
