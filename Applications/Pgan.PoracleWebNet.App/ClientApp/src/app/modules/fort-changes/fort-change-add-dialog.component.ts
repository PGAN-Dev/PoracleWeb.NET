import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
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
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { AlarmScope, scopeToFields } from '../../shared/utils/alarm-scope';

@Component({
  imports: [
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

  private readonly fb = inject(FormBuilder);

  private readonly fortChangeService = inject(FortChangeService);
  private readonly i18n = inject(I18nService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<FortChangeAddDialogComponent>);
  form = this.fb.group({
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

  save(): void {
    this.saving.set(true);
    const v = this.form.getRawValue();
    const scope = scopeToFields(this.scope());
    const changeTypes: string[] = [];
    if (v.changeTypeName) changeTypes.push('name');
    if (v.changeTypeLocation) changeTypes.push('location');
    if (v.changeTypeImageUrl) changeTypes.push('image_url');
    if (v.changeTypeRemoval) changeTypes.push('removal');
    if (v.changeTypeNew) changeTypes.push('new');

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
        // field a file got wrong. A fixed string threw that away. See #567, #568.
        error: (err: { error?: { error?: string } }) => {
          this.snackBar.open(err?.error?.error ?? this.i18n.instant('FORT_CHANGES.CREATE_FAILED'), this.i18n.instant('COMMON.OK'), {
            duration: 6000,
          });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open(this.i18n.instant('FORT_CHANGES.CREATE_SUCCESS'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
