import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

import { UserGeofence } from '../../../core/models';

export interface GeofenceApprovalDialogData {
  geofence: UserGeofence;
}

export interface GeofenceApprovalDialogResult {
  action: 'approve' | 'reject';
  promotedName?: string;
  reviewNotes?: string;
}

@Component({
  imports: [DatePipe, FormsModule, MatButtonModule, MatButtonToggleModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule],
  selector: 'app-geofence-approval-dialog',
  standalone: true,
  styleUrl: './geofence-approval-dialog.component.scss',
  templateUrl: './geofence-approval-dialog.component.html',
})
export class GeofenceApprovalDialogComponent {
  readonly data = inject<GeofenceApprovalDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<GeofenceApprovalDialogComponent>);

  mode: 'approve' | 'reject' = 'approve';
  promotedName = '';
  reviewNotes = '';

  constructor() {
    this.promotedName = this.data.geofence.displayName;
  }

  onApprove(): void {
    this.dialogRef.close({
      action: 'approve',
      promotedName: this.promotedName.trim() || undefined,
    } as GeofenceApprovalDialogResult);
  }

  onCancel(): void {
    this.dialogRef.close(null);
  }

  onReject(): void {
    this.dialogRef.close({
      action: 'reject',
      reviewNotes: this.reviewNotes.trim(),
    } as GeofenceApprovalDialogResult);
  }
}
