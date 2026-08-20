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
import { TranslatePipe } from '@ngx-translate/core';

import { Raid, Egg, RaidUpdate, EggUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { EggService } from '../../core/services/egg.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { RaidService } from '../../core/services/raid.service';
import { GymPickerComponent } from '../../shared/components/gym-picker/gym-picker.component';
import { RsvpToggleComponent } from '../../shared/components/rsvp-toggle/rsvp-toggle.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { LevelLabelPipe } from '../../shared/pipes/level-label.pipe';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { AUTO_DELETE, EDIT, isAutoDelete } from '../../shared/utils/clean-flags';

export interface RaidEditDialogData {
  item: Raid | Egg;
  type: 'raid' | 'egg';
}

@Component({
  providers: [LevelLabelPipe],
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
    TranslatePipe,
    TemplateSelectorComponent,
    GymPickerComponent,
    RsvpToggleComponent,
    LevelLabelPipe,
    ScopePickerComponent,
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
  private readonly levelLabelPipe = inject(LevelLabelPipe);
  private readonly raidService = inject(RaidService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<RaidEditDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<RaidEditDialogComponent>);

  form = this.fb.group({
    clean: [isAutoDelete(this.data.item.clean)],
    rsvpChanges: [this.data.item.rsvpChanges],
    team: [this.data.item.team],
    template: [this.data.item.template ?? ''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  saving = signal(false);

  /** The alarm's current scope, read back into the shared picker. */
  readonly scope = signal<AlarmScope>(scopeOf(this.data.item.overrideLocationLabel, this.data.item.overrideAreas, this.data.item.distance));
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
      return this.levelLabelPipe.transform(this.data.item.level) + ' ' + this.i18n.instant('RAIDS.EGG_SUFFIX');
    }
    const raid = this.data.item as Raid;
    if (raid.pokemonId && raid.pokemonId !== 9000) {
      return this.i18n.instant('RAIDS.RAID_BOSS_NUM', { id: raid.pokemonId });
    }
    return this.levelLabelPipe.transform(raid.level) + ' ' + this.i18n.instant('RAIDS.RAID_SUFFIX');
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  save(): void {
    this.saving.set(true);
    const values = this.form.getRawValue();
    const scope = scopeToFields(this.scope());
    // clean is a PoracleNG bitmask: bit 1 = auto-delete, bit 2 = edit-in-place, bit 4 = summary.
    // RSVP modes (1/2) need the edit bit so count changes edit the alert instead of re-sending.
    // Preserve any other bits (e.g. bot-set summary) the web UI does not surface.
    const clean =
      (values.clean ? AUTO_DELETE : 0) | ((values.rsvpChanges ?? 0) >= 1 ? EDIT : 0) | (this.data.item.clean & ~(AUTO_DELETE | EDIT));

    if (this.data.type === 'raid') {
      const raid = this.data.item as Raid;
      const update: RaidUpdate = {
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        clean,
        distance: scope.distance,
        evolution: raid.evolution,
        exclusive: raid.exclusive,
        form: raid.form,
        gymId: this.selectedGymId() ?? '',
        level: raid.level,
        move: raid.move,
        pokemonId: raid.pokemonId,
        rsvpChanges: values.rsvpChanges ?? 0,
        team: values.team ?? 4,
        template: values.template || '',
      };
      this.raidService.update(this.data.item.uid, update).subscribe({
        // The server names what is wrong -- which alarm already uses these settings, which
        // field a file got wrong. A fixed string threw that away. See #567, #568.
        error: (err: { error?: { error?: string } }) => {
          this.snackBar.open(err?.error?.error ?? this.i18n.instant('RAIDS.SNACK_FAILED_UPDATE'), this.i18n.instant('TOAST.OK'), {
            duration: 6000,
          });
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
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        clean,
        distance: scope.distance,
        exclusive: egg.exclusive,
        gymId: this.selectedGymId() ?? '',
        level: egg.level,
        rsvpChanges: values.rsvpChanges ?? 0,
        team: values.team ?? 4,
        template: values.template || '',
      };
      this.eggService.update(this.data.item.uid, update).subscribe({
        // The server names what is wrong -- which alarm already uses these settings, which
        // field a file got wrong. A fixed string threw that away. See #567, #568.
        error: (err: { error?: { error?: string } }) => {
          this.snackBar.open(err?.error?.error ?? this.i18n.instant('RAIDS.SNACK_FAILED_UPDATE'), this.i18n.instant('TOAST.OK'), {
            duration: 6000,
          });
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
