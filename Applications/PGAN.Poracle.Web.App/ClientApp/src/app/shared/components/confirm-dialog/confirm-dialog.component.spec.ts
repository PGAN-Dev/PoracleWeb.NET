import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { ConfirmDialogComponent, ConfirmDialogData, ConfirmDialogResult } from './confirm-dialog.component';

describe('ConfirmDialogComponent', () => {
  let component: ConfirmDialogComponent;
  let dialogRef: { close: jest.Mock };

  function setup(data: ConfirmDialogData) {
    dialogRef = { close: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
      imports: [ConfirmDialogComponent],
    });

    const fixture = TestBed.createComponent(ConfirmDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  describe('without showDontAskAgain', () => {
    beforeEach(() => {
      setup({
        message: 'Are you sure you want to delete this?',
        title: 'Delete Item',
      });
    });

    it('should create with injected data', () => {
      expect(component.data.title).toBe('Delete Item');
      expect(component.data.message).toBe('Are you sure you want to delete this?');
    });

    it('should close with true on confirm', () => {
      component.onConfirm();
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('should close with false on cancel', () => {
      component.onCancel();
      expect(dialogRef.close).toHaveBeenCalledWith(false);
    });
  });

  describe('with showDontAskAgain', () => {
    beforeEach(() => {
      setup({
        message: 'Delete all items?',
        showDontAskAgain: true,
        title: 'Delete All',
      });
    });

    it('should close with ConfirmDialogResult on confirm', () => {
      component.dontAskAgain = true;
      component.onConfirm();

      expect(dialogRef.close).toHaveBeenCalledWith({
        confirmed: true,
        dontAskAgain: true,
      } as ConfirmDialogResult);
    });

    it('should close with dontAskAgain false when unchecked', () => {
      component.dontAskAgain = false;
      component.onConfirm();

      expect(dialogRef.close).toHaveBeenCalledWith({
        confirmed: true,
        dontAskAgain: false,
      } as ConfirmDialogResult);
    });

    it('should close with confirmed false on cancel', () => {
      component.onCancel();

      expect(dialogRef.close).toHaveBeenCalledWith({
        confirmed: false,
        dontAskAgain: false,
      } as ConfirmDialogResult);
    });
  });

  describe('with custom button text', () => {
    it('should accept custom confirm and cancel text', () => {
      setup({
        cancelText: 'Nah',
        confirmText: 'Yes, do it',
        message: 'Custom message',
        title: 'Custom',
      });

      expect(component.data.confirmText).toBe('Yes, do it');
      expect(component.data.cancelText).toBe('Nah');
    });
  });

  describe('with warn flag', () => {
    it('should pass warn flag through data', () => {
      setup({
        message: 'This is destructive',
        title: 'Warning',
        warn: true,
      });

      expect(component.data.warn).toBe(true);
    });
  });
});
