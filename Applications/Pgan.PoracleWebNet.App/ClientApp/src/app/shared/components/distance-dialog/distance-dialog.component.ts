import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { TranslateModule } from '@ngx-translate/core';

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
  selector: 'app-distance-dialog',
  standalone: true,
  styleUrl: './distance-dialog.component.scss',
  templateUrl: './distance-dialog.component.html',
})
export class DistanceDialogComponent {
  readonly dialogRef = inject(MatDialogRef<DistanceDialogComponent>);

  distanceKm = 1;
  mode = signal<'areas' | 'distance'>('areas');

  apply(): void {
    const meters = this.mode() === 'areas' ? 0 : Math.round(this.distanceKm * 1000);
    this.dialogRef.close(meters);
  }
}
