import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';

import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { GymService } from '../../core/services/gym.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { AddRefusalComponent } from '../../shared/components/add-refusal/add-refusal.component';
import { GymPickerComponent } from '../../shared/components/gym-picker/gym-picker.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { TranslatePluralPipe } from '../../shared/pipes/translate-plural.pipe';
import { AddDialogData, AddRefusal, editExistingResult, ExistingRule, finishAdd, settle } from '../../shared/utils/add-outcome';
import { AlarmScope, scopeToFields } from '../../shared/utils/alarm-scope';
import { compose } from '../../shared/utils/clean-flags';

interface TeamOption {
  color: string;
  id: number;
  name: string;
}

@Component({
  imports: [
    AddRefusalComponent,
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSlideToggleModule,
    MatIconModule,
    MatCheckboxModule,
    MatRadioModule,
    MatTabsModule,
    MatSnackBarModule,
    TranslatePipe,
    TranslatePluralPipe,
    TemplateSelectorComponent,
    GymPickerComponent,
    MatSelectModule,
    ScopePickerComponent,
  ],
  selector: 'app-gym-add-dialog',
  standalone: true,
  styleUrl: './gym-add-dialog.component.scss',
  templateUrl: './gym-add-dialog.component.html',
})
export class GymAddDialogComponent {
  private readonly alertDefaults = inject(AlertDefaultsService);
  private readonly data = inject<AddDialogData | null>(MAT_DIALOG_DATA, { optional: true });

  private readonly fb = inject(FormBuilder);

  private readonly gymService = inject(GymService);
  private readonly i18n = inject(I18nService);
  private readonly icons = inject(IconService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<GymAddDialogComponent>);
  form = this.fb.group({
    battleChanges: [false],
    clean: [false],
    slotChanges: [false],
    template: [''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  /** False while the scope picker holds a radius it won't accept, which keeps Save off. */
  readonly radiusValid = signal(true);

  /** Why the last single Add was refused; shown above the actions, cleared on the next save. */
  readonly refusal = signal<AddRefusal | null>(null);

  saving = signal(false);

  /**
   * Seeded from the saved defaults so the Alert Defaults preference still reaches new alarms; the
   * picker owns it from there.
   */
  readonly scope = signal<AlarmScope>(
    this.alertDefaults.defaultMode() === 'areas'
      ? { mode: 'profile' }
      : {
          distanceKm: this.alertDefaults.defaultDistanceKm(),
          mode: this.alertDefaults.defaultPlaceLabel() ? 'place' : 'profile',
          placeLabel: this.alertDefaults.defaultPlaceLabel(),
        },
  );

  selectedGymId = signal<string | null>(null);

  selectedTeamIds = signal<number[]>([]);
  teams: TeamOption[] = [
    { id: 0, name: this.i18n.instant('GYMS.TEAM_NEUTRAL'), color: '#9E9E9E' },
    { id: 1, name: this.i18n.instant('GYMS.TEAM_MYSTIC'), color: '#2196F3' },
    { id: 2, name: this.i18n.instant('GYMS.TEAM_VALOR'), color: '#F44336' },
    { id: 3, name: this.i18n.instant('GYMS.TEAM_INSTINCT'), color: '#FFC107' },
  ];

  /** The refusal named a stored rule: close and let the list open that one for editing. */
  editExisting(rule: ExistingRule): void {
    this.dialogRef.close(editExistingResult(rule));
  }

  getGymIcon(team: number): string {
    return this.icons.getGymUrl(team);
  }

  save(): void {
    if (this.selectedTeamIds().length === 0) return;
    this.saving.set(true);
    this.refusal.set(null);
    const v = this.form.getRawValue();
    const scope = scopeToFields(this.scope());
    const creates = this.selectedTeamIds().map(team =>
      this.gymService.create({
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        battleChanges: v.battleChanges ? 1 : 0,
        clean: compose(!!v.clean, false, false),
        distance: scope.distance,
        gymId: this.selectedGymId() ?? '',
        slotChanges: v.slotChanges ? 1 : 0,
        team,
        template: v.template || null,
      }),
    );
    // Each create settles on its own, so one refusal cannot hide the ones that landed (#577). A single
    // refused Add keeps the dialog open with the reason inline; see finishAdd.
    forkJoin(creates.map(c => settle(c))).subscribe(results => {
      this.saving.set(false);
      finishAdd(results, {
        dialogRef: this.dialogRef,
        failedKey: 'ALARM.FAILED_CREATE',
        i18n: this.i18n,
        lookup: this.data?.existingRule,
        refusal: this.refusal,
        snackBar: this.snackBar,
      });
    });
  }

  toggleTeam(id: number): void {
    this.selectedTeamIds.update(ids => (ids.includes(id) ? ids.filter(i => i !== id) : [...ids, id]));
  }
}
