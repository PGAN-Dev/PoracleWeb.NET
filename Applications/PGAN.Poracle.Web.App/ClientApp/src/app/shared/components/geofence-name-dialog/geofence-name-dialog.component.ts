import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

import { GeofenceRegion } from '../../../core/models';
import { RegionOption, RegionSelectorComponent } from '../region-selector/region-selector.component';

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
  imports: [FormsModule, MatButtonModule, MatChipsModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule, RegionSelectorComponent],
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
  readonly namePattern = /^[a-zA-Z0-9 \-'.()&]+$/;

  readonly regionOptions: RegionOption[] = this.data.regions.map(r => ({
    id: r.id,
    label: r.displayName,
    shortLabel: r.displayName,
  }));

  selectedRegionId: number | null = this.data.detectedRegion?.id ?? null;

  get hasInvalidChars(): boolean {
    return this.displayName.trim().length > 0 && !this.namePattern.test(this.displayName.trim());
  }

  get isValid(): boolean {
    const name = this.displayName.trim();
    return name.length > 0 && name.length <= 50 && !this.hasInvalidChars && this.selectedRegionId !== null;
  }

  onChangeRegion(): void {
    this.manualSelect.set(true);
  }

  onRegionPicked(option: RegionOption): void {
    this.selectedRegionId = option.id ?? null;
  }

  save(): void {
    if (!this.isValid) return;

    const region = this.data.regions.find(r => r.id === this.selectedRegionId);
    if (!region) return;

    this.dialogRef.close({
      displayName: this.displayName.trim(),
      groupName: region.displayName,
      parentId: region.id,
    } as GeofenceNameDialogResult);
  }
}
