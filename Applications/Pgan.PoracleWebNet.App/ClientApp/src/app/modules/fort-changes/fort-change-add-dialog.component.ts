import { Component, computed, inject, signal } from '@angular/core';
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

import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { FortChangeService } from '../../core/services/fort-change.service';
import { I18nService } from '../../core/services/i18n.service';
import { SettingsService } from '../../core/services/settings.service';
import { AddRefusalComponent } from '../../shared/components/add-refusal/add-refusal.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import {
  AddDialogData,
  AddRefusal,
  editExistingResult,
  ExistingRule,
  createFailureText,
  refusalOf,
  reportCreated,
} from '../../shared/utils/add-outcome';
import { AlarmScope, scopeToFields } from '../../shared/utils/alarm-scope';

@Component({
  imports: [
    AddRefusalComponent,
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatRadioModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatTabsModule,
    MatSnackBarModule,
    TranslatePipe,
    TemplateSelectorComponent,
    ScopePickerComponent,
  ],
  selector: 'app-fort-change-add-dialog',
  standalone: true,
  styleUrl: './fort-change-add-dialog.component.scss',
  templateUrl: './fort-change-add-dialog.component.html',
})
export class FortChangeAddDialogComponent {
  private readonly alertDefaults = inject(AlertDefaultsService);

  private readonly data = inject<AddDialogData | null>(MAT_DIALOG_DATA, { optional: true });

  private readonly fb = inject(FormBuilder);
  private readonly fortChangeService = inject(FortChangeService);
  private readonly i18n = inject(I18nService);
  private readonly settings = inject(SettingsService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<FortChangeAddDialogComponent>);
  form = this.fb.group({
    changeTypeDescription: [false],
    changeTypeImageUrl: [false],
    changeTypeLocation: [true],
    changeTypeName: [true],
    changeTypeNew: [true],
    changeTypeRemoval: [true],
    fortType: ['everything'],
    includeEmpty: [false],
    template: [''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  /** False while the scope picker holds a radius it won't accept, which keeps Save off. */
  readonly radiusValid = signal(true);
  /** Why the last Add was refused; shown above the actions, cleared on the next save. */
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

  /**
   * Whether the template selector shows. Fort changes have no clean bit, so with templates off the
   * "Message settings" heading would label nothing (the Mention field aside, on a webhook edit).
   */
  readonly templatesEnabled = computed(() => this.settings.siteSettings()['enable_templates']?.toLowerCase() === 'true');

  /** The refusal named a stored rule: close and let the list open that one for editing. */
  editExisting(rule: ExistingRule): void {
    this.dialogRef.close(editExistingResult(rule));
  }

  save(): void {
    this.saving.set(true);
    this.refusal.set(null);
    const v = this.form.getRawValue();
    const scope = scopeToFields(this.scope());
    const changeTypes: string[] = [];
    if (v.changeTypeName) changeTypes.push('name');
    if (v.changeTypeLocation) changeTypes.push('location');
    if (v.changeTypeImageUrl) changeTypes.push('image_url');
    if (v.changeTypeRemoval) changeTypes.push('removal');
    if (v.changeTypeNew) changeTypes.push('new');
    if (v.changeTypeDescription) changeTypes.push('description');

    this.fortChangeService
      .create({
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        changeTypes,
        distance: scope.distance,
        fortType: v.fortType,
        includeEmpty: v.includeEmpty ? 1 : 0,
        template: v.template || null,
      })
      .subscribe({
        // The server names what is wrong -- which alarm already uses these settings, which
        // field a file got wrong. A fixed string threw that away (#567, #568). Nothing was created, so
        // the dialog stays open with the reason inline and the user's settings kept.
        error: (err: unknown) => {
          this.refusal.set(refusalOf(err, createFailureText(err, this.i18n, 'ALARM.FAILED_CREATE'), this.data?.existingRule));
          this.saving.set(false);
        },
        next: created => {
          // uid 0: PoracleNG found this exact rule already stored and left it as it was.
          const unchanged = (created?.uid ?? 0) <= 0;
          reportCreated(unchanged ? 0 : 1, unchanged ? 1 : 0, this.i18n, this.snackBar);
          this.dialogRef.close(true);
        },
      });
  }
}
