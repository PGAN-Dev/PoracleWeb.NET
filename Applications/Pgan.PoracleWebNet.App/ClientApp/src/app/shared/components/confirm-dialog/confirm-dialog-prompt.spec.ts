import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';

import { ConfirmDialogComponent, ConfirmDialogData } from './confirm-dialog.component';

/**
 * The name prompt (flows D8): Save was live for an empty or whitespace name and closed the dialog having
 * saved nothing, and a duplicate disabled Save with no word as to why, because the template-driven input
 * was never in an error state for its mat-error to show.
 */
describe('ConfirmDialogComponent name prompt', () => {
  let close: jest.Mock;

  function render(promptField: ConfirmDialogData['promptField']) {
    close = jest.fn();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MAT_DIALOG_DATA, useValue: { message: 'What should this place be called?', promptField, title: 'Name this place' } },
        { provide: MatDialogRef, useValue: { close } },
      ],
      imports: [ConfirmDialogComponent],
    });
    const fixture = TestBed.createComponent(ConfirmDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  /** Types into the field the way a person does, so ngModel and the touched flag see a real input event. */
  function type(fixture: ReturnType<typeof render>, value: string): void {
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  const saveButton = (fixture: ReturnType<typeof render>) =>
    fixture.nativeElement.querySelectorAll('mat-dialog-actions button')[1] as HTMLButtonElement;
  const errorText = (fixture: ReturnType<typeof render>) =>
    (fixture.nativeElement.querySelector('mat-error') as HTMLElement | null)?.textContent?.trim() ?? null;

  it('keeps Save off for an empty or whitespace name, without scolding before anything is typed', () => {
    const fixture = render({ label: 'Name', value: '' });
    expect(saveButton(fixture).disabled).toBe(true);
    expect(errorText(fixture)).toBeNull();

    type(fixture, '   ');
    expect(saveButton(fixture).disabled).toBe(true);
    expect(errorText(fixture)).toBe('DIALOG.PROMPT_REQUIRED');
  });

  it('says why a duplicate is refused, in the words the caller chose', () => {
    const fixture = render({ conflictKey: 'WHERE.PLACE_NAME_CONFLICT', existingNames: ['Work'], label: 'Name', value: '' });

    type(fixture, ' work ');

    expect(saveButton(fixture).disabled).toBe(true);
    expect(errorText(fixture)).toBe('WHERE.PLACE_NAME_CONFLICT');
  });

  it('keeps the profile wording for callers that pass no conflict key', () => {
    const fixture = render({ existingNames: ['Default'], label: 'Name', value: '' });

    type(fixture, 'default');

    expect(errorText(fixture)).toBe('DIALOG.PROMPT_CONFLICT');
  });

  it('refuses line breaks and text-direction characters when asked to', () => {
    const fixture = render({ refuseHiddenCharacters: true, label: 'Name', value: '' });

    type(fixture, 'Gym\u202Eevil');
    expect(saveButton(fixture).disabled).toBe(true);
    expect(errorText(fixture)).toBe('DIALOG.PROMPT_HIDDEN_CHARACTERS');

    // A text input drops a typed line feed, but a pasted line separator (U+2028) survives.
    type(fixture, 'Line\u2028break');
    expect(saveButton(fixture).disabled).toBe(true);
  });

  it('caps the length and counts towards it', () => {
    const fixture = render({ label: 'Name', maxLength: 64, value: '' });
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(input.getAttribute('maxlength')).toBe('64');

    type(fixture, 'Work');
    expect((fixture.nativeElement.querySelector('mat-hint') as HTMLElement).textContent?.trim()).toBe('4 / 64');

    type(fixture, 'a'.repeat(65));
    expect(saveButton(fixture).disabled).toBe(true);
  });

  it('accepts an ordinary name and hands it back trimmed', () => {
    const fixture = render({
      refuseHiddenCharacters: true,
      conflictKey: 'WHERE.PLACE_NAME_CONFLICT',
      existingNames: ['Work'],
      label: 'Name',
      maxLength: 64,
      value: '',
    });

    for (const name of ["Parents' house - Maumee", 'Café (2)', '👨\u200D👩\u200D👧 family', 'a'.repeat(64)]) {
      type(fixture, name);
      expect(saveButton(fixture).disabled).toBe(false);
      expect(errorText(fixture)).toBeNull();
    }

    type(fixture, '  Gym  ');
    fixture.componentInstance.onConfirm();
    expect(close).toHaveBeenCalledWith('Gym');
  });

  it('leaves a dialog without a prompt alone', () => {
    close = jest.fn();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MAT_DIALOG_DATA, useValue: { message: 'Delete?', title: 'Delete' } },
        { provide: MatDialogRef, useValue: { close } },
      ],
      imports: [ConfirmDialogComponent],
    });
    const fixture = TestBed.createComponent(ConfirmDialogComponent);
    fixture.detectChanges();

    expect(saveButton(fixture).disabled).toBe(false);
  });
});
