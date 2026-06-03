import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { TranslateModule } from '@ngx-translate/core';

import {
  AlertDefaultsService,
  AlertLocationMode,
  MAX_DEFAULT_DISTANCE_KM,
  MIN_DEFAULT_DISTANCE_KM,
} from '../../../core/services/alert-defaults.service';
import { DeliveryPreviewComponent } from '../delivery-preview/delivery-preview.component';

@Component({
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
    MatIconModule,
    DeliveryPreviewComponent,
    TranslateModule,
  ],
  selector: 'app-alert-defaults-dialog',
  standalone: true,
  styleUrl: './alert-defaults-dialog.component.scss',
  templateUrl: './alert-defaults-dialog.component.html',
})
export class AlertDefaultsDialogComponent {
  private readonly alertDefaults = inject(AlertDefaultsService);

  readonly dialogRef = inject(MatDialogRef<AlertDefaultsDialogComponent>);

  distanceKm = this.alertDefaults.defaultDistanceKm();
  readonly maxKm = MAX_DEFAULT_DISTANCE_KM;

  readonly minKm = MIN_DEFAULT_DISTANCE_KM;
  mode = signal<AlertLocationMode>(this.alertDefaults.defaultMode());

  save(): void {
    this.alertDefaults.save(this.mode(), this.distanceKm);
    this.dialogRef.close(true);
  }
}
