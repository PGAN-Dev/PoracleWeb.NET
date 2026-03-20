import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';

import { Gym, GymUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { GymService } from '../../core/services/gym.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

@Component({
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSlideToggleModule,
    MatIconModule,
    MatRadioModule,
    MatTabsModule,
    MatSnackBarModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-gym-edit-dialog',
  standalone: true,
  styleUrl: './gym-edit-dialog.component.scss',
  templateUrl: './gym-edit-dialog.component.html',
})
export class GymEditDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly gymService = inject(GymService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Gym>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<GymEditDialogComponent>);
  form = this.fb.group({
    battle_changes: [this.data.battle_changes === 1],
    clean: [this.data.clean === 1],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    distanceMode: [this.data.distance === 0 ? 'areas' : ('distance' as 'areas' | 'distance')],
    ping: [this.data.ping ?? ''],
    slot_changes: [this.data.slot_changes === 1],
    template: [this.data.template ?? ''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  saving = signal(false);
  getTeamColor(team: number): string {
    switch (team) {
      case 0:
        return '#9E9E9E';
      case 1:
        return '#2196F3';
      case 2:
        return '#F44336';
      case 3:
        return '#FFC107';
      default:
        return '#9E9E9E';
    }
  }

  getTeamName(team: number): string {
    switch (team) {
      case 0:
        return 'Neutral';
      case 1:
        return 'Mystic (Blue)';
      case 2:
        return 'Valor (Red)';
      case 3:
        return 'Instinct (Yellow)';
      default:
        return `Team ${team}`;
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
    this.gymService
      .update(this.data.uid, {
        battle_changes: v.battle_changes ? 1 : 0,
        clean: v.clean ? 1 : 0,
        distance: dist,
        ping: v.ping || null,
        slot_changes: v.slot_changes ? 1 : 0,
        template: v.template || null,
      } as GymUpdate)
      .subscribe({
        error: () => {
          this.snackBar.open('Failed to update alarm', 'OK', { duration: 3000 });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open('Gym alarm updated', 'OK', { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
