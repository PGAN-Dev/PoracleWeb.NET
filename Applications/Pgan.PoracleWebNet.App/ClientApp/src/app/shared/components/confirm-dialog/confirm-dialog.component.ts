import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslateModule } from '@ngx-translate/core';

export interface ConfirmDialogData {
  cancelText?: string;
  confirmText?: string;
  itemDescription?: string;
  message: string;
  promptField?: { existingNames?: string[]; label: string; value: string };
  showDontAskAgain?: boolean;
  title: string;
  warn?: boolean;
}

export interface ConfirmDialogResult {
  confirmed: boolean;
  dontAskAgain: boolean;
}

@Component({
  host: {
    'aria-describedby': 'confirm-dialog-message',
    role: 'alertdialog',
  },
  imports: [
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    TranslateModule,
  ],
  selector: 'app-confirm-dialog',
  standalone: true,
  styleUrl: './confirm-dialog.component.scss',
  templateUrl: './confirm-dialog.component.html',
})
export class ConfirmDialogComponent {
  readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<ConfirmDialogComponent>);

  dontAskAgain = false;
  promptError = '';
  promptValue = this.data.promptField?.value ?? '';

  get hasPromptConflict(): boolean {
    if (!this.data.promptField?.existingNames) return false;
    const val = this.promptValue.trim().toLowerCase();
    return this.data.promptField.existingNames.some(n => n.toLowerCase() === val);
  }

  onCancel(): void {
    if (this.data.showDontAskAgain) {
      this.dialogRef.close({ confirmed: false, dontAskAgain: false } as ConfirmDialogResult);
    } else {
      this.dialogRef.close(false);
    }
  }

  onConfirm(): void {
    if (this.data.promptField) {
      this.dialogRef.close(this.promptValue.trim() || false);
    } else if (this.data.showDontAskAgain) {
      this.dialogRef.close({ confirmed: true, dontAskAgain: this.dontAskAgain } as ConfirmDialogResult);
    } else {
      this.dialogRef.close(true);
    }
  }
}
