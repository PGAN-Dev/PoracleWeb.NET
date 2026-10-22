import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { ErrorStateMatcher } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { TranslatePipe } from '@ngx-translate/core';

import { TranslatePluralPipe } from '../../pipes/translate-plural.pipe';
import { MAX_RADIUS_KM, MIN_RADIUS_KM, radiusAcceptable } from '../../utils/alarm-scope';
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
    TranslatePluralPipe,
  ],
  selector: 'app-distance-dialog',
  standalone: true,
  styleUrl: './distance-dialog.component.scss',
  templateUrl: './distance-dialog.component.html',
})
export class DistanceDialogComponent {
  private readonly data = inject<DistanceDialogData | null>(MAT_DIALOG_DATA, { optional: true });

  /** How many alarms the change acts on: the selection, never "all of this type". */
  readonly count = this.data?.count ?? 0;

  readonly dialogRef = inject(MatDialogRef<DistanceDialogComponent>);

  /** Null while the field is empty: a number input bound with ngModel reports an empty value as null. */
  distanceKm: null | number = 1;
  mode = signal<'areas' | 'distance'>('areas');

  /** Shows the error whenever Apply is off, including for an empty field, which no validator flags. */
  protected readonly radiusErrorMatcher: ErrorStateMatcher = { isErrorState: () => !this.isValid };

  protected readonly radiusMax = MAX_RADIUS_KM;
  protected readonly radiusMin = MIN_RADIUS_KM;

  /**
   * The input carried `min="0.1"` but nothing enforced it -- no validator, no form, no disabled binding --
   * so typing -5 and pressing Update All sent -5000. PoracleNG clamps the upper bound but not the lower,
   * and it treats distance > 0 as "use a radius", so a negative silently switched every selected alarm to
   * area-based delivery while the cards went on showing a negative radius. See #417. It is held to the
   * range the alarm dialogs offer, so a bulk change can't store a radius a single edit would refuse.
   */
  get isValid(): boolean {
    return this.mode() === 'areas' || radiusAcceptable(this.distanceKm);
  }

  apply(): void {
    if (!this.isValid) return;
    const meters = this.mode() === 'areas' ? 0 : Math.round((this.distanceKm ?? 0) * 1000);
    this.dialogRef.close(meters);
  }
}

/** What a list passes when it opens the dialog. */
export interface DistanceDialogData {
  /** The number of selected alarms the change will act on. */
  count: number;
}
