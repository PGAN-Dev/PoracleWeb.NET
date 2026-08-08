import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';

import { GeofenceRegion, UserGeofence } from '../../../core/models';
import { RegionOption, RegionSelectorComponent } from '../region-selector/region-selector.component';

export interface GeofenceApprovalDialogData {
  geofence: UserGeofence;
  regions: GeofenceRegion[];
}

export interface GeofenceApprovalDialogResult {
  action: 'approve' | 'reject';
  groupName?: string;
  parentId?: number;
  promotedName?: string;
  reviewNotes?: string;
}

@Component({
  imports: [
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    RegionSelectorComponent,
    TranslatePipe,
  ],
  selector: 'app-geofence-approval-dialog',
  standalone: true,
  styleUrl: './geofence-approval-dialog.component.scss',
  templateUrl: './geofence-approval-dialog.component.html',
})
export class GeofenceApprovalDialogComponent {
  static readonly PROMOTED_NAME_MAX = 50;
  /**
   * Mirrors the server's promoted-name rules so the admin is told before the round-trip rather than
   * after it. The server rejects anything outside this set, and used to answer 404 for it, so the
   * SPA reported the submission as missing while it sat visible in the list. See #421.
   */
  static readonly PROMOTED_NAME_PATTERN = /^[a-zA-Z0-9 \-'.()&]+$/;

  readonly data = inject<GeofenceApprovalDialogData>(MAT_DIALOG_DATA);

  readonly dialogRef = inject(MatDialogRef<GeofenceApprovalDialogComponent>);
  // Region (Koji parent) the geofence will be filed under once public. Defaults to the submission's
  // existing region (which may be none — see issue #314), and the admin can change it here.
  readonly regionOptions: RegionOption[] = this.data.regions.map(r => ({
    id: r.id,
    label: r.displayName,
    shortLabel: r.displayName,
  }));

  // Hide the region picker when Koji defines no regions (flat project) — there is nothing to choose
  // from, so promotion just keeps whatever the submission had (issue #314).
  readonly hasRegions = this.regionOptions.length > 0;

  mode: 'approve' | 'reject' = 'approve';

  promotedName = '';
  reviewNotes = '';

  selectedRegionId: number | null = this.data.geofence.parentId > 0 ? this.data.geofence.parentId : null;

  constructor() {
    this.promotedName = this.data.geofence.displayName;
  }

  get canApprove(): boolean {
    return this.mode !== 'approve' || this.promotedNameError === null;
  }

  /** Empty is allowed — it means "keep the current display name". */
  get promotedNameError(): string | null {
    const value = this.promotedName.trim();
    if (!value) return null;
    if (value.length > GeofenceApprovalDialogComponent.PROMOTED_NAME_MAX) return 'ADMIN.APPROVAL_PROMOTED_NAME_TOO_LONG';
    if (!GeofenceApprovalDialogComponent.PROMOTED_NAME_PATTERN.test(value)) return 'ADMIN.APPROVAL_PROMOTED_NAME_INVALID';
    return null;
  }

  onApprove(): void {
    const result: GeofenceApprovalDialogResult = {
      action: 'approve',
      promotedName: this.promotedName.trim() || undefined,
    };

    // Only send region overrides when there are regions to choose from. With no regions, leave
    // parentId/groupName undefined so the backend keeps the submission's existing values (#314).
    if (this.hasRegions) {
      const region = this.selectedRegionId !== null ? this.data.regions.find(r => r.id === this.selectedRegionId) : undefined;
      result.groupName = region?.displayName ?? '';
      result.parentId = region?.id ?? 0;
    }

    this.dialogRef.close(result);
  }

  onCancel(): void {
    this.dialogRef.close(null);
  }

  onRegionPicked(option: RegionOption): void {
    this.selectedRegionId = option.id ?? null;
  }

  onReject(): void {
    this.dialogRef.close({
      action: 'reject',
      reviewNotes: this.reviewNotes.trim(),
    } as GeofenceApprovalDialogResult);
  }
}
