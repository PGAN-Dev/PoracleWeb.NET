import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatRadioModule } from '@angular/material/radio';
import { TranslatePipe } from '@ngx-translate/core';

import { GeofenceAlarmsChoice } from '../../../core/models';

export interface GeofenceDeleteDialogData {
  /** Alarms limited to this fence alone. */
  count: number;
  name: string;
  /** Set when an admin deletes someone else's fence, so the copy names whose alarms these are. */
  owner?: string;
}

/**
 * Asked only when deleting a fence would widen alarms: an alarm whose only override area is this fence
 * would otherwise match across the profile's areas. Closes with the choice, or null to cancel.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    'aria-describedby': 'geofence-delete-dialog-body',
    'aria-labelledby': 'geofence-delete-dialog-title',
    role: 'alertdialog',
  },
  imports: [MatButtonModule, MatDialogModule, MatIconModule, MatRadioModule, TranslatePipe],
  selector: 'app-geofence-delete-dialog',
  standalone: true,
  styleUrl: './geofence-delete-dialog.component.scss',
  templateUrl: './geofence-delete-dialog.component.html',
})
export class GeofenceDeleteDialogComponent {
  private readonly dialogRef = inject<MatDialogRef<GeofenceDeleteDialogComponent, GeofenceAlarmsChoice | null>>(MatDialogRef);
  readonly data = inject<GeofenceDeleteDialogData>(MAT_DIALOG_DATA);
  private readonly prefix = this.data.owner ? 'GEOFENCES.ADMIN_DELETE_WIDEN' : 'GEOFENCES.DELETE_WIDEN';

  /** Picked by count, the way the active-hours editor picks its _ONE/_OTHER keys. */
  readonly bodyKey = computed(() => `${this.prefix}_BODY_${this.data.count === 1 ? 'ONE' : 'OTHER'}`);

  /** Deleting them is the default: a fence the user removes should stop alerting them, not alert more. */
  readonly choice = signal<GeofenceAlarmsChoice>('delete');

  readonly keepKey = computed(() => `${this.prefix}_KEEP`);

  cancel(): void {
    this.dialogRef.close(null);
  }

  confirm(): void {
    this.dialogRef.close(this.choice());
  }
}
