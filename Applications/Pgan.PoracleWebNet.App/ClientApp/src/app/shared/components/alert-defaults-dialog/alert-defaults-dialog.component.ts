import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe } from '@ngx-translate/core';

import { SavedPlaces } from '../../../core/models';
import {
  AlertDefaultsService,
  AlertLocationMode,
  MAX_DEFAULT_DISTANCE_KM,
  MIN_DEFAULT_DISTANCE_KM,
} from '../../../core/services/alert-defaults.service';
import { PlacesService } from '../../../core/services/places.service';
import { DeliveryPreviewComponent } from '../delivery-preview/delivery-preview.component';

@Component({
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
    MatSelectModule,
    MatIconModule,
    DeliveryPreviewComponent,
    TranslatePipe,
  ],
  selector: 'app-alert-defaults-dialog',
  standalone: true,
  styleUrl: './alert-defaults-dialog.component.scss',
  templateUrl: './alert-defaults-dialog.component.html',
})
export class AlertDefaultsDialogComponent implements OnInit {
  private readonly alertDefaults = inject(AlertDefaultsService);
  readonly dialogRef = inject(MatDialogRef<AlertDefaultsDialogComponent>);

  distanceKm = this.alertDefaults.defaultDistanceKm();

  readonly maxKm = MAX_DEFAULT_DISTANCE_KM;

  readonly minKm = MIN_DEFAULT_DISTANCE_KM;
  mode = signal<AlertLocationMode>(this.alertDefaults.defaultMode());

  /** Empty means the profile pin, which is what new alarms did before per-alarm scope. */
  placeLabel = this.alertDefaults.defaultPlaceLabel();
  readonly places = inject(PlacesService);

  get canSave(): boolean {
    return this.distanceError === null;
  }

  /**
   * The service clamps to 0.1-100 on save. Without surfacing that, the dialog accepted 200, showed
   * "200 km" in the live preview, then stored 100 - and 0 or -5 silently became 1. See #426.
   */
  get distanceError(): string | null {
    if (this.mode() !== 'distance') return null;
    const km = this.distanceKm;
    if (!Number.isFinite(km) || km < 0.1) return 'ALERT_DEFAULTS.DISTANCE_TOO_SMALL';
    if (km > 100) return 'ALERT_DEFAULTS.DISTANCE_TOO_LARGE';
    return null;
  }

  ngOnInit(): void {
    this.loadPlaces();
  }

  save(): void {
    if (!this.canSave) return;
    this.alertDefaults.save(this.mode(), this.distanceKm, this.placeLabel);
    this.dialogRef.close(true);
  }

  private loadPlaces(): void {
    this.places.load().subscribe({
      error: () => undefined,
      // A place the user has since deleted would otherwise keep seeding new alarms with a label
      // PoracleNG rejects, and the select would show a blank row for it.
      next: (places: SavedPlaces) => this.alertDefaults.reconcilePlace(places.named.map(p => p.label)),
    });
  }
}
