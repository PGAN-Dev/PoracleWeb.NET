import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { TranslatePipe } from '@ngx-translate/core';

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
    TranslatePipe,
  ],
  selector: 'app-distance-dialog',
  standalone: true,
  styleUrl: './distance-dialog.component.scss',
  templateUrl: './distance-dialog.component.html',
})
export class DistanceDialogComponent {
  readonly dialogRef = inject(MatDialogRef<DistanceDialogComponent>);

  distanceKm = 1;
  mode = signal<'areas' | 'distance'>('areas');

  /**
   * The input carried `min="0.1"` but nothing enforced it -- no validator, no form, no disabled binding --
   * so typing -5 and pressing Update All sent -5000. PoracleNG clamps the upper bound but not the lower,
   * and it treats distance > 0 as "use a radius", so a negative silently switched every selected alarm to
   * area-based delivery while the cards went on showing a negative radius. See #417.
   */
  get isValid(): boolean {
    return this.mode() === 'areas' || (Number.isFinite(this.distanceKm) && this.distanceKm > 0);
  }

  apply(): void {
    if (!this.isValid) return;
    const meters = this.mode() === 'areas' ? 0 : Math.round(this.distanceKm * 1000);
    this.dialogRef.close(meters);
  }
}
