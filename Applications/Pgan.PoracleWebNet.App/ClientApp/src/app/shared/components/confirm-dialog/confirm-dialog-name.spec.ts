import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';

import { ConfirmDialogComponent } from './confirm-dialog.component';

/**
 * The confirm dialog declares itself an alertdialog, and axe (aria-dialog-name, serious) found it had no
 * name: it pointed at its message for a description and at nothing for a label, so a screen reader
 * announced "alert dialog" and then the body. Every delete, duplicate and reseed prompt uses it.
 */
describe('ConfirmDialogComponent accessible name', () => {
  const render = (title: string) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MAT_DIALOG_DATA, useValue: { message: 'This copies every alarm.', title, warn: true } },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
      ],
      imports: [ConfirmDialogComponent],
    });
    const fixture = TestBed.createComponent(ConfirmDialogComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('is labelled by its title', () => {
    const host = render('Duplicate profile');
    const labelledBy = host.getAttribute('aria-labelledby');

    expect(labelledBy).toBeTruthy();
    expect(host.querySelector(`#${labelledBy}`)?.textContent?.trim()).toContain('Duplicate profile');
  });

  it('still describes itself by its message', () => {
    const host = render('Delete alarm');

    expect(host.getAttribute('aria-describedby')).toBe('confirm-dialog-message');
    expect(host.querySelector('#confirm-dialog-message')?.textContent).toBe('This copies every alarm.');
  });
});
