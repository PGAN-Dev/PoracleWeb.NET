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
import { TranslateModule } from '@ngx-translate/core';

import { Raid, Egg, RaidUpdate, EggUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { EggService } from '../../core/services/egg.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { RaidService } from '../../core/services/raid.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { GymPickerComponent } from '../../shared/components/gym-picker/gym-picker.component';
import { RsvpToggleComponent } from '../../shared/components/rsvp-toggle/rsvp-toggle.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

export interface RaidEditDialogData {
  item: Raid | Egg;
  type: 'raid' | 'egg';
}

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
    TranslateModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
    GymPickerComponent,
    RsvpToggleComponent,
  ],
  selector: 'app-raid-edit-dialog',
  standalone: true,
  styleUrl: './raid-edit-dialog.component.scss',
  templateUrl: './raid-edit-dialog.component.html',
})
export class RaidEditDialogComponent {
  private readonly eggService = inject(EggService);
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly iconService = inject(IconService);
  private readonly raidService = inject(RaidService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<RaidEditDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<RaidEditDialogComponent>);
  form = this.fb.group({
    clean: [this.data.item.clean === 1],
    distanceKm: [this.data.item.distance > 0 ? this.data.item.distance / 1000 : 1],
    distanceMode: [this.data.item.distance === 0 ? 'areas' : ('distance' as 'areas' | 'distance')],
    ping: [this.data.item.ping ?? ''],
    rsvpChanges: [this.data.item.rsvpChanges ?? 0],
    team: [this.data.item.team],
    template: [this.data.item.template ?? ''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  saving = signal(false);
  selectedGymId = signal<string | null>(this.data.item.gymId);

  getImage(): string {
    if (this.data.type === 'egg') {
      return this.iconService.getRaidEggUrl(this.data.item.level);
    }
    const raid = this.data.item as Raid;
    if (raid.pokemonId && raid.pokemonId !== 9000) {
      return this.iconService.getPokemonUrl(raid.pokemonId);
    }
    return this.iconService.getRaidEggUrl(this.data.item.level);
  }

  getTitle(): string {
    if (this.data.type === 'egg') {
      return this.i18n.instant('RAIDS.LEVEL_PREFIX') + ' ' + this.data.item.level + ' ' + this.i18n.instant('RAIDS.EGG_SUFFIX');
    }
    const raid = this.data.item as Raid;
    if (raid.pokemonId && raid.pokemonId !== 9000) {
      return this.i18n.instant('RAIDS.RAID_BOSS_NUM', { id: raid.pokemonId });
    }
    return this.i18n.instant('RAIDS.LEVEL_PREFIX') + ' ' + raid.level + ' ' + this.i18n.instant('RAIDS.RAID_SUFFIX');
  }

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') {
      this.form.controls.distanceKm.setValue(0);
    } else {
      if (!this.form.controls.distanceKm.value) {
        this.form.controls.distanceKm.setValue(1);
      }
    }
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  save(): void {
    this.saving.set(true);
    const values = this.form.getRawValue();
    const distanceMeters = values.distanceMode === 'areas' ? 0 : Math.round((values.distanceKm ?? 1) * 1000);

    if (this.data.type === 'raid') {
      const raid = this.data.item as Raid;
      const update: RaidUpdate = {
        clean: values.clean ? 1 : 0,
        distance: distanceMeters,
        evolution: raid.evolution,
        exclusive: raid.exclusive,
        form: raid.form,
        gymId: this.selectedGymId() || null,
        level: raid.level,
        move: raid.move,
        ping: values.ping || null,
        pokemonId: raid.pokemonId,
        rsvpChanges: values.rsvpChanges ?? 0,
        team: values.team ?? 4,
        template: values.template || null,
      };
      this.raidService.update(this.data.item.uid, update).subscribe({
        error: () => {
          this.snackBar.open(this.i18n.instant('RAIDS.SNACK_FAILED_UPDATE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open(this.i18n.instant('RAIDS.SNACK_UPDATED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
    } else {
      const egg = this.data.item as Egg;
      const update: EggUpdate = {
        clean: values.clean ? 1 : 0,
        distance: distanceMeters,
        exclusive: egg.exclusive,
        gymId: this.selectedGymId() || null,
        level: egg.level,
        ping: values.ping || null,
        rsvpChanges: values.rsvpChanges ?? 0,
        team: values.team ?? 4,
        template: values.template || null,
      };
      this.eggService.update(this.data.item.uid, update).subscribe({
        error: () => {
          this.snackBar.open(this.i18n.instant('RAIDS.SNACK_FAILED_UPDATE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open(this.i18n.instant('RAIDS.SNACK_EGG_UPDATED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
    }
  }
}
