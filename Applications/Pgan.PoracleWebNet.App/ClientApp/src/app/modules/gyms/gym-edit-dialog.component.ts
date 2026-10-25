import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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

import { Gym, GymUpdate, LinkSide } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { GymService } from '../../core/services/gym.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { DeliverToFieldComponent } from '../../shared/components/deliver-to/deliver-to-field.component';
import { GymPickerComponent } from '../../shared/components/gym-picker/gym-picker.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { createFailureText } from '../../shared/utils/add-outcome';
import { AlarmScope, metresToKm, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { AUTO_DELETE, isAutoDelete, preserve } from '../../shared/utils/clean-flags';
import { injectDeliverTo, moveEditMessage } from '../../shared/utils/deliver-to';
import { mentionCapable } from '../../shared/utils/human-kind';

@Component({
  imports: [
    DeliverToFieldComponent,
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
    GymPickerComponent,
    ScopePickerComponent,
  ],
  selector: 'app-gym-edit-dialog',
  standalone: true,
  styleUrl: './gym-edit-dialog.component.scss',
  templateUrl: './gym-edit-dialog.component.html',
})
export class GymEditDialogComponent {
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly gymService = inject(GymService);
  private readonly i18n = inject(I18nService);
  private readonly icons = inject(IconService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Gym>(MAT_DIALOG_DATA);
  readonly deliver = injectDeliverTo();

  readonly dialogRef = inject(MatDialogRef<GymEditDialogComponent>);

  form = this.fb.group({
    battleChanges: [this.data.battleChanges === 1],
    clean: [isAutoDelete(this.data.clean)],
    destination: [(this.data.destination ?? null) as LinkSide | null],
    // Always present even when the Mention control is hidden (not a webhook alarm) -- so a mention
    // round-trips unchanged through a save this dialog makes for an unrelated field.
    ping: [this.data.ping ?? '', [Validators.maxLength(255)]],
    slotChanges: [this.data.slotChanges === 1],
    template: [this.data.template ?? ''],
  });

  /** False while the scope picker holds a radius it won't accept, which keeps Save off. */
  readonly radiusValid = signal(true);

  saving = signal(false);

  /** The alarm's current scope, read back into the shared picker. */
  readonly scope = signal<AlarmScope>(scopeOf(this.data.overrideLocationLabel, this.data.overrideAreas, this.data.distance));

  selectedGymId = signal<string | null>(this.data.gymId);

  readonly showMention = this.auth.isImpersonating() && mentionCapable(this.auth.user()?.type);
  /** The radius as stored, which an edit may keep even outside the picker's range. */
  readonly storedRadiusKm = metresToKm(this.data.distance);

  getGymIcon(): string {
    return this.icons.getGymUrl(this.data.team);
  }

  getTeamName(team: number): string {
    switch (team) {
      case 0:
        return this.i18n.instant('GYMS.TEAM_NEUTRAL');
      case 1:
        return this.i18n.instant('GYMS.TEAM_MYSTIC');
      case 2:
        return this.i18n.instant('GYMS.TEAM_VALOR');
      case 3:
        return this.i18n.instant('GYMS.TEAM_INSTINCT');
      case 4:
        return this.i18n.instant('RAIDS.TEAM_ANY');
      default:
        return this.i18n.instant('GYMS.TEAM_UNKNOWN', { id: team });
    }
  }

  save(): void {
    this.saving.set(true);
    const v = this.form.getRawValue();
    const scope = scopeToFields(this.scope());
    this.gymService
      .update(this.data.uid, {
        ...this.deliver.moveFields(this.data.destination, this.form.controls.destination.value),
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        battleChanges: v.battleChanges ? 1 : 0,
        clean: preserve(this.data.clean, AUTO_DELETE, v.clean ? 1 : 0),
        distance: scope.distance,
        gymId: this.selectedGymId() ?? '',
        ping: v.ping ?? '',
        slotChanges: v.slotChanges ? 1 : 0,
        team: this.data.team,
        template: v.template || '',
      } as GymUpdate)
      .subscribe({
        // The server names what is wrong -- which alarm already uses these settings, which
        // field a file got wrong. A fixed string threw that away. See #567, #568.
        error: (err: unknown) => {
          this.snackBar.open(createFailureText(err, this.i18n, 'ALARM.FAILED_UPDATE'), this.i18n.instant('COMMON.OK'), {
            duration: 6000,
          });
          this.saving.set(false);
        },
        next: moved => {
          this.snackBar.open(moveEditMessage(moved, this.i18n) ?? this.i18n.instant('GYMS.SNACK_UPDATED'), this.i18n.instant('COMMON.OK'), {
            duration: 3000,
          });
          this.dialogRef.close(true);
        },
      });
  }
}
