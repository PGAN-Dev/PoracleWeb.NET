import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { PlacesSectionComponent } from './places-section.component';
import { ConfigService } from '../../../core/services/config.service';
import { PlacesService } from '../../../core/services/places.service';

describe('PlacesSectionComponent', () => {
  let dialog: { open: jest.Mock };
  let places: {
    add: jest.Mock;
    load: jest.Mock;
    move: jest.Mock;
    named: jest.Mock;
    pin: jest.Mock;
    remove: jest.Mock;
  };
  let snackBar: { open: jest.Mock };
  let fixture: ComponentFixture<PlacesSectionComponent>;

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

    fixture = TestBed.createComponent(PlacesSectionComponent);
    const component = fixture.componentInstance;
    component.ngOnInit();
    return component;
  }

  it('always renders the move control on a saved place', () => {
    // No capability flag exists any more: every supported PoracleNG can move a place.
    create();
    fixture.detectChanges();

    const icons = Array.from(fixture.nativeElement.querySelectorAll('mat-icon')).map(el => (el as HTMLElement).textContent!.trim());
    expect(icons).toContain('edit_location_alt');
  });

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

    expect(snackBar.open).toHaveBeenCalledWith('WHERE.PLACE_IN_USE_OTHER', expect.anything(), expect.anything());
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
      data: { latitude: 1, longitude: 2, pickOnly: true, title: 'WHERE.PLACE_MOVE_NAMED' },
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

  it('asks for the name under the API rules: 64 characters, no hidden characters, a place-worded duplicate note', () => {
    const component = create();
    queueDialogResults({ latitude: 10, longitude: 20 }, false);

    component.addPlace();

    expect(dialog.open.mock.calls[1][1].data.promptField).toMatchObject({
      refuseHiddenCharacters: true,
      conflictKey: 'WHERE.PLACE_NAME_CONFLICT',
      maxLength: 64,
    });
  });

  it('shows the reason the server gave when it refuses a name', () => {
    const component = create();
    queueDialogResults({ latitude: 10, longitude: 20 }, 'gym');
    places.add.mockReturnValue(
      throwError(() => new HttpErrorResponse({ error: { error: 'That name is too long. Use 64 characters or fewer.' }, status: 400 })),
    );

    component.addPlace();

    expect(snackBar.open).toHaveBeenCalledWith('That name is too long. Use 64 characters or fewer.', expect.anything(), expect.anything());
  });

  it('falls back to its own message when the refusal carries no reason', () => {
    const component = create();
    queueDialogResults({ latitude: 10, longitude: 20 }, 'gym');
    places.add.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    component.addPlace();

    expect(snackBar.open).toHaveBeenCalledWith('WHERE.PLACE_SAVE_ERROR', expect.anything(), expect.anything());
  });

  it('names the place it is moving in the picker title', () => {
    const component = create();
    queueDialogResults(undefined);

    component.movePlace({ label: 'work', latitude: 1, longitude: 2 });

    expect(dialog.open.mock.calls[0][1].data.title).toBe('WHERE.PLACE_MOVE_NAMED');
  });

  it('warns before a delete that a place still in use is refused, rather than promising a fallback', () => {
    const component = create();
    queueDialogResults(false);

    component.confirmRemove({ label: 'work', latitude: 1, longitude: 2 });

    expect(dialog.open.mock.calls[0][1].data.message).toBe('WHERE.PLACE_DELETE_CONFIRM');
  });

  it('lists the alarms in the way when a delete is refused', () => {
    const component = create();
    const translate = TestBed.inject(TranslateService);
    const instant = jest.spyOn(translate, 'instant');
    places.remove.mockReturnValue(
      throwError(() => new HttpErrorResponse({ error: { referencingRules: ['pokemon 37337', 'raid 9'] }, status: 409 })),
    );

    component.removePlace({ label: 'work', latitude: 1, longitude: 2 });

    const call = instant.mock.calls.find(([key, params]) => key === 'WHERE.PLACE_IN_USE_OTHER' && params);
    expect(call?.[1]).toMatchObject({ count: 2, place: 'work', rules: 'NAV.POKEMON #37337, NAV.RAIDS #9' });
  });

  it('reports a failed move rather than leaving the card looking changed', () => {
    const component = create();
    queueDialogResults({ latitude: 9.5, longitude: 8.5 });
    places.move.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 501 })));

    component.movePlace({ label: 'work', latitude: 1, longitude: 2 });

    expect(snackBar.open).toHaveBeenCalledWith('WHERE.PLACE_MOVE_ERROR', expect.anything(), expect.anything());
  });
});
