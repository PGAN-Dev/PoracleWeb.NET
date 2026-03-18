import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';

export interface ConfirmDialogData {
  cancelText?: string;
  confirmText?: string;
  itemDescription?: string;
  message: string;
  showDontAskAgain?: boolean;
  title: string;
  warn?: boolean;
}

export interface ConfirmDialogResult {
  confirmed: boolean;
  dontAskAgain: boolean;
}

@Component({
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatCheckboxModule, FormsModule],
  selector: 'app-confirm-dialog',
  standalone: true,
  styleUrl: './confirm-dialog.component.scss',
  templateUrl: './confirm-dialog.component.html',
})
export class ConfirmDialogComponent {
  readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<ConfirmDialogComponent>);

  dontAskAgain = false;

  onCancel(): void {
    if (this.data.showDontAskAgain) {
      this.dialogRef.close({ confirmed: false, dontAskAgain: false } as ConfirmDialogResult);
    } else {
      this.dialogRef.close(false);
    }
  }

  onConfirm(): void {
    if (this.data.showDontAskAgain) {
      this.dialogRef.close({ confirmed: true, dontAskAgain: this.dontAskAgain } as ConfirmDialogResult);
    } else {
      this.dialogRef.close(true);
    }
  }
}
