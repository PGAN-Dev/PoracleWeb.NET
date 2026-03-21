import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { GeofenceRegion } from '../../../core/models';

export interface GeofenceNameDialogData {
  detectedRegion: { id: number; name: string; displayName: string } | null;
  regions: GeofenceRegion[];
}

export interface GeofenceNameDialogResult {
  displayName: string;
  groupName: string;
  parentId: number;
}

@Component({
  imports: [
    FormsModule,
    MatButtonModule,
    MatChipsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  selector: 'app-geofence-name-dialog',
  standalone: true,
  styleUrl: './geofence-name-dialog.component.scss',
  templateUrl: './geofence-name-dialog.component.html',
})
export class GeofenceNameDialogComponent {
  readonly data = inject<GeofenceNameDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<GeofenceNameDialogComponent>);

  displayName = '';
  readonly manualSelect = signal(!this.data.detectedRegion);
  selectedRegionId: number | null = this.data.detectedRegion?.id ?? null;

  get isValid(): boolean {
    return this.displayName.trim().length > 0 && this.displayName.trim().length <= 50 && this.selectedRegionId !== null;
  }

  onChangeRegion(): void {
    this.manualSelect.set(true);
  }

  save(): void {
    if (!this.isValid) return;

    const region = this.data.regions.find(r => r.id === this.selectedRegionId);
    if (!region) return;

    this.dialogRef.close({
      displayName: this.displayName.trim(),
      groupName: region.name,
      parentId: region.id,
    } as GeofenceNameDialogResult);
  }
}
