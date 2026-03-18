import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatIconModule } from '@angular/material/icon';
import { DeliveryPreviewComponent } from '../delivery-preview/delivery-preview.component';

@Component({
  selector: 'app-distance-dialog',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
    MatIconModule,
    DeliveryPreviewComponent,
  ],
  template: `
    <h2 mat-dialog-title>Update All Distance</h2>
    <mat-dialog-content>
      <p class="dialog-desc">Set the location mode for all alarms of this type.</p>

      <mat-radio-group [(ngModel)]="mode" class="mode-group">
        <mat-radio-button value="areas">
          <div class="radio-label">
            <mat-icon>map</mat-icon>
            <div>
              <strong>Use Areas</strong>
              <p class="radio-hint">Notifications will use your configured geofence areas</p>
            </div>
          </div>
        </mat-radio-button>
        <mat-radio-button value="distance">
          <div class="radio-label">
            <mat-icon>straighten</mat-icon>
            <div>
              <strong>Set Distance</strong>
              <p class="radio-hint">Notify within a radius from your location</p>
            </div>
          </div>
        </mat-radio-button>
      </mat-radio-group>

      @if (mode() === 'distance') {
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Distance</mat-label>
          <input matInput type="number" [(ngModel)]="distanceKm" min="0.1" step="0.1" />
          <span matSuffix>km</span>
        </mat-form-field>
      }

      <app-delivery-preview
        [mode]="mode()"
        [distanceKm]="mode() === 'distance' ? distanceKm : 0">
      </app-delivery-preview>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(null)">Cancel</button>
      <button mat-raised-button color="primary" (click)="apply()">
        Update All
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { min-width: 320px; max-width: 440px; }
    .dialog-desc { margin: 0 0 16px; color: var(--text-secondary, rgba(0,0,0,0.54)); font-size: 14px; }
    .mode-group { display: flex; flex-direction: column; gap: 12px; margin-bottom: 16px; }
    .radio-label { display: flex; align-items: flex-start; gap: 8px; }
    .radio-label mat-icon { margin-top: 2px; color: var(--text-secondary, rgba(0,0,0,0.54)); }
    .radio-hint { margin: 2px 0 0; font-size: 12px; color: var(--text-secondary, rgba(0,0,0,0.54)); font-weight: normal; }
    .full-width { width: 100%; }
  `],
})
export class DistanceDialogComponent {
  readonly dialogRef = inject(MatDialogRef<DistanceDialogComponent>);

  mode = signal<'areas' | 'distance'>('areas');
  distanceKm = 1;

  apply(): void {
    const meters = this.mode() === 'areas' ? 0 : Math.round(this.distanceKm * 1000);
    this.dialogRef.close(meters);
  }
}
