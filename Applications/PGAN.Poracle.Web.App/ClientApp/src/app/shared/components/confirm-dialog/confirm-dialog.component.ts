import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';

export interface ConfirmDialogData {
  title: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  warn?: boolean;
  showDontAskAgain?: boolean;
  itemDescription?: string;
}

export interface ConfirmDialogResult {
  confirmed: boolean;
  dontAskAgain: boolean;
}

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatCheckboxModule, FormsModule],
  template: `
    <h2 mat-dialog-title [class.warn-title]="data.warn">
      @if (data.warn) {
        <mat-icon class="title-icon warn-icon">warning</mat-icon>
      }
      {{ data.title }}
    </h2>
    <mat-dialog-content>
      <p>{{ data.message }}</p>
      @if (data.itemDescription) {
        <p class="item-description">{{ data.itemDescription }}</p>
      }
      @if (data.showDontAskAgain) {
        <mat-checkbox [(ngModel)]="dontAskAgain" class="dont-ask-checkbox">
          Don't ask again for this session
        </mat-checkbox>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="onCancel()">
        {{ data.cancelText || 'Cancel' }}
      </button>
      <button
        mat-raised-button
        [color]="data.warn ? 'warn' : 'primary'"
        (click)="onConfirm()"
      >
        @if (data.warn) {
          <mat-icon>delete</mat-icon>
        }
        {{ data.confirmText || 'Confirm' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      .warn-title {
        display: flex;
        align-items: center;
        gap: 8px;
        color: #d32f2f;
      }
      .title-icon {
        vertical-align: middle;
      }
      .warn-icon {
        color: #d32f2f;
      }
      .item-description {
        font-weight: 500;
        padding: 8px 12px;
        background: #fafafa;
        border-left: 3px solid #d32f2f;
        border-radius: 0 4px 4px 0;
        margin: 8px 0;
      }
      .dont-ask-checkbox {
        margin-top: 16px;
        display: block;
      }
    `,
  ],
})
export class ConfirmDialogComponent {
  readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<ConfirmDialogComponent>);

  dontAskAgain = false;

  onConfirm(): void {
    if (this.data.showDontAskAgain) {
      this.dialogRef.close({ confirmed: true, dontAskAgain: this.dontAskAgain } as ConfirmDialogResult);
    } else {
      this.dialogRef.close(true);
    }
  }

  onCancel(): void {
    if (this.data.showDontAskAgain) {
      this.dialogRef.close({ confirmed: false, dontAskAgain: false } as ConfirmDialogResult);
    } else {
      this.dialogRef.close(false);
    }
  }
}
