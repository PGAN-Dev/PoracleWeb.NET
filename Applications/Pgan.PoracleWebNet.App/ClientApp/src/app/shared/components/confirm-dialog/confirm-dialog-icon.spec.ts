import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';

import { ConfirmDialogComponent, ConfirmDialogData } from './confirm-dialog.component';

/**
 * Unlinking deletes nothing, so its confirm can't wear the warning triangle and a trash can. `icon` gives a
 * neutral confirm its own glyph; a warn confirm keeps the old look.
 */
describe('ConfirmDialogComponent icon', () => {
  const render = (data: ConfirmDialogData) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
      ],
      imports: [ConfirmDialogComponent],
    });
    const fixture = TestBed.createComponent(ConfirmDialogComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };
  const icons = (host: HTMLElement) => Array.from(host.querySelectorAll('mat-icon')).map(i => i.textContent?.trim());

  it('shows the named icon in the title and on the confirm button, and no warning or trash can', () => {
    const host = render({ confirmText: 'Unlink', icon: 'link_off', message: 'Nothing is deleted.', title: 'Unlink accounts' });

    expect(icons(host)).toEqual(['link_off', 'link_off']);
    expect(host.querySelector('.warn-title')).toBeNull();
    expect(host.querySelector('mat-dialog-actions [color="warn"]')).toBeNull();
  });

  it('keeps the warning and the trash can on a warn confirm without an icon', () => {
    const host = render({ message: 'This deletes it.', title: 'Delete alarm', warn: true });

    expect(icons(host)).toEqual(['warning', 'delete']);
  });

  it('shows no icon on a plain confirm', () => {
    expect(icons(render({ message: 'Sure?', title: 'Confirm' }))).toEqual([]);
  });
});
