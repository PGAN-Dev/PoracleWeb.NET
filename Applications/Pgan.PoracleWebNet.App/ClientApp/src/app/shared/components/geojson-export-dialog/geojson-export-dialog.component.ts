import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';

import { UserGeofence } from '../../../core/models';

export interface GeoJsonExportDialogData {
  geofences: UserGeofence[];
}

export interface GeoJsonExportDialogResult {
  selected: UserGeofence[];
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatCheckboxModule, MatDialogModule, MatIconModule],
  selector: 'app-geojson-export-dialog',
  standalone: true,
  styles: `
    mat-dialog-content {
      min-width: 360px;
      max-width: 480px;
    }
    .select-all-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 8px;
      padding: 0 4px;
    }
    .selected-count {
      font-size: 12px;
      color: var(--text-secondary, rgba(0, 0, 0, 0.54));
    }
    .geofence-list {
      max-height: 320px;
      overflow-y: auto;
      border: 1px solid var(--card-border, rgba(0, 0, 0, 0.12));
      border-radius: 8px;
    }
    .geofence-item {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 12px;
      border-bottom: 1px solid var(--divider, rgba(0, 0, 0, 0.06));
      transition: opacity 0.15s;
      &:last-child {
        border-bottom: none;
      }
      &.deselected {
        opacity: 0.45;
      }
    }
    .geofence-detail {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: 1px;
    }
    .geofence-name {
      font-size: 13px;
      font-weight: 500;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .geofence-meta {
      font-size: 11px;
      color: var(--text-secondary, rgba(0, 0, 0, 0.54));
    }
  `,
  template: `
    <h2 mat-dialog-title>Export Geofences</h2>
    <mat-dialog-content>
      <div class="select-all-row">
        <mat-checkbox [checked]="allSelected()" (change)="toggleAll($event.checked)">Select All</mat-checkbox>
        <span class="selected-count">{{ selectedCount() }} of {{ selections().length }} selected</span>
      </div>
      <div class="geofence-list">
        @for (item of selections(); track $index) {
          <div class="geofence-item" [class.deselected]="!item.selected">
            <mat-checkbox [checked]="item.selected" (change)="toggle($index)"></mat-checkbox>
            <div class="geofence-detail">
              <span class="geofence-name">{{ item.geofence.displayName }}</span>
              <span class="geofence-meta">{{ item.geofence.groupName || 'No region' }}</span>
            </div>
          </div>
        }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(null)">Cancel</button>
      <button mat-raised-button color="primary" [disabled]="selectedCount() === 0" (click)="export()">
        <mat-icon>download</mat-icon>
        Export {{ selectedCount() }} Geofence{{ selectedCount() !== 1 ? 's' : '' }}
      </button>
    </mat-dialog-actions>
  `,
})
export class GeoJsonExportDialogComponent {
  readonly data = inject<GeoJsonExportDialogData>(MAT_DIALOG_DATA);
  readonly selections = signal(this.data.geofences.map(g => ({ geofence: g, selected: true })));

  readonly allSelected = computed(() => {
    const items = this.selections();
    return items.length > 0 && items.every(s => s.selected);
  });

  readonly dialogRef = inject(MatDialogRef<GeoJsonExportDialogComponent>);
  readonly selectedCount = computed(() => this.selections().filter(s => s.selected).length);

  export(): void {
    const selected = this.selections()
      .filter(s => s.selected)
      .map(s => s.geofence);
    this.dialogRef.close({ selected } as GeoJsonExportDialogResult);
  }

  toggle(index: number): void {
    this.selections.update(items => items.map((item, i) => (i === index ? { ...item, selected: !item.selected } : item)));
  }

  toggleAll(checked: boolean): void {
    this.selections.update(items => items.map(item => ({ ...item, selected: checked })));
  }
}
