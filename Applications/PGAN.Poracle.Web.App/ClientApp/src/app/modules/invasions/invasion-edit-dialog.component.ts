import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';

import { Invasion, InvasionUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { InvasionService } from '../../core/services/invasion.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

@Component({
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatIconModule,
    MatRadioModule,
    MatTabsModule,
    MatSnackBarModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-invasion-edit-dialog',
  standalone: true,
  styleUrl: './invasion-edit-dialog.component.scss',
  templateUrl: './invasion-edit-dialog.component.html',
})
export class InvasionEditDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly invasionService = inject(InvasionService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Invasion>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<InvasionEditDialogComponent>);
  form = this.fb.group({
    clean: [this.data.clean === 1],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    distanceMode: [this.data.distance === 0 ? 'areas' : ('distance' as 'areas' | 'distance')],
    gender: [this.data.gender],
    ping: [this.data.ping ?? ''],
    template: [this.data.template ?? ''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  private static readonly GRUNT_MAP: Record<string, number> = {
    Bug: 1, Dark: 2, Dragon: 3, Electric: 4, Fairy: 5, Fighting: 6,
    Fire: 7, Flying: 8, Ghost: 9, Grass: 10, Ground: 11, Ice: 12,
    Metal: 13, Normal: 14, Poison: 15, Psychic: 16, Rock: 17, Water: 18,
    mixed: 41, Giovanni: 44, Decoy: 50,
  };

  saving = signal(false);

  getGruntIcon(): string {
    const id = InvasionEditDialogComponent.GRUNT_MAP[this.data.gruntType ?? ''] ?? 0;
    return id > 0 ? `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/invasion/${id}.png` : '';
  }

  getGenderLabel(): string {
    switch (this.data.gender) {
      case 1:
        return 'Male';
      case 2:
        return 'Female';
      default:
        return 'Any Gender';
    }
  }

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0);
    else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1);
  }

  save(): void {
    this.saving.set(true);
    const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    this.invasionService
      .update(this.data.uid, {
        clean: v.clean ? 1 : 0,
        distance: dist,
        gender: v.gender ?? 0,
        ping: v.ping || null,
        template: v.template || null,
      } as InvasionUpdate)
      .subscribe({
        error: () => {
          this.snackBar.open('Failed to update alarm', 'OK', { duration: 3000 });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open('Invasion alarm updated', 'OK', { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
