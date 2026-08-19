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
import { TranslatePipe } from '@ngx-translate/core';

import { Gym, GymUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { GymService } from '../../core/services/gym.service';
import { I18nService } from '../../core/services/i18n.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { GymPickerComponent } from '../../shared/components/gym-picker/gym-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { WhereChipComponent } from '../../shared/components/where-chip/where-chip.component';
import { AUTO_DELETE, isAutoDelete, preserve } from '../../shared/utils/clean-flags';

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
    TranslatePipe,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
    GymPickerComponent,
    WhereChipComponent,
  ],
  selector: 'app-gym-edit-dialog',
  standalone: true,
  styleUrl: './gym-edit-dialog.component.scss',
  templateUrl: './gym-edit-dialog.component.html',
})
export class GymEditDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly gymService = inject(GymService);
  private readonly i18n = inject(I18nService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Gym>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<GymEditDialogComponent>);
  form = this.fb.group({
    battleChanges: [this.data.battleChanges === 1],
    clean: [isAutoDelete(this.data.clean)],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    distanceMode: [this.data.distance === 0 ? 'areas' : ('distance' as 'areas' | 'distance')],
    slotChanges: [this.data.slotChanges === 1],
    template: [this.data.template ?? ''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  saving = signal(false);
  selectedGymId = signal<string | null>(this.data.gymId);
  getGymIcon(): string {
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/gym/${this.data.team}.png`;
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

  /** True when the alarm is confined to areas, which the areas-or-distance control cannot express. */
  isAreaScoped(): boolean {
    return (this.data.overrideAreas?.length ?? 0) > 0;
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
        battleChanges: v.battleChanges ? 1 : 0,
        clean: preserve(this.data.clean, AUTO_DELETE, v.clean ? 1 : 0),
        distance: dist,
        gymId: this.selectedGymId() ?? '',
        slotChanges: v.slotChanges ? 1 : 0,
        team: this.data.team,
        template: v.template || '',
      } as GymUpdate)
      .subscribe({
        // The server names what is wrong -- which alarm already uses these settings, which
        // field a file got wrong. A fixed string threw that away. See #567, #568.
        error: (err: { error?: { error?: string } }) => {
          this.snackBar.open(err?.error?.error ?? this.i18n.instant('GYMS.SNACK_FAILED_UPDATE'), this.i18n.instant('COMMON.OK'), {
            duration: 6000,
          });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open(this.i18n.instant('GYMS.SNACK_UPDATED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
