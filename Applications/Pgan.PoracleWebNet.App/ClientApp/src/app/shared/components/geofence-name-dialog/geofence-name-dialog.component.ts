import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';

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
  imports: [
    FormsModule,
    MatButtonModule,
    MatChipsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    RegionSelectorComponent,
    TranslatePipe,
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
  readonly regionOptions: RegionOption[] = this.data.regions.map(r => ({
    id: r.id,
    label: r.displayName,
    shortLabel: r.displayName,
  }));

  // When Koji defines no regions (a flat project), there is nothing to pick — hide the region UI
  // entirely rather than showing an empty dropdown (issue #314).
  readonly hasRegions = this.regionOptions.length > 0;

  readonly manualSelect = signal(!this.data.detectedRegion);

  readonly namePattern = /^[a-zA-Z0-9 \-'.()&]+$/;

  selectedRegionId: number | null = this.data.detectedRegion?.id ?? null;

  get hasInvalidChars(): boolean {
    return this.displayName.trim().length > 0 && !this.namePattern.test(this.displayName.trim());
  }

  get isValid(): boolean {
    const name = this.displayName.trim();
    // Region is optional: a private geofence does not need a Koji region. The region/parent is only
    // used when an admin later promotes the geofence to a public Koji area. See issue #314.
    return name.length > 0 && name.length <= 50 && !this.hasInvalidChars;
  }

  onChangeRegion(): void {
    this.manualSelect.set(true);
  }

  onRegionPicked(option: RegionOption): void {
    this.selectedRegionId = option.id ?? null;
  }

  save(): void {
    if (!this.isValid) return;

    // Region is optional — fall back to an empty group / parentId 0 when none is selected.
    const region = this.selectedRegionId !== null ? this.data.regions.find(r => r.id === this.selectedRegionId) : undefined;

    this.dialogRef.close({
      displayName: this.displayName.trim(),
      groupName: region?.displayName ?? '',
      parentId: region?.id ?? 0,
    } as GeofenceNameDialogResult);
  }
}
