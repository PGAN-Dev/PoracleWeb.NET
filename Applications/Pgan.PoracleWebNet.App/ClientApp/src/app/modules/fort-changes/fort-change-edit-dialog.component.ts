import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
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

import { FortChange, FortChangeUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { FortChangeService } from '../../core/services/fort-change.service';
import { I18nService } from '../../core/services/i18n.service';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';

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
  selector: 'app-fort-change-edit-dialog',
  standalone: true,
  styleUrl: './fort-change-edit-dialog.component.scss',
  templateUrl: './fort-change-edit-dialog.component.html',
})
export class FortChangeEditDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly fortChangeService = inject(FortChangeService);
  private readonly i18n = inject(I18nService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<FortChange>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<FortChangeEditDialogComponent>);

  form = this.fb.group({
    changeTypeImageUrl: [this.data.changeTypes?.includes('image_url') ?? false],
    changeTypeLocation: [this.data.changeTypes?.includes('location') ?? false],
    changeTypeName: [this.data.changeTypes?.includes('name') ?? false],
    changeTypeNew: [this.data.changeTypes?.includes('new') ?? false],
    changeTypeRemoval: [this.data.changeTypes?.includes('removal') ?? false],
    fortType: [this.data.fortType ?? 'everything'],
    includeEmpty: [this.data.includeEmpty === 1],
    template: [this.data.template ?? ''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  saving = signal(false);

  /** The alarm's current scope, read back into the shared picker. */
  readonly scope = signal<AlarmScope>(scopeOf(this.data.overrideLocationLabel, this.data.overrideAreas, this.data.distance));

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
      .update(this.data.uid, {
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        changeTypes,
        distance: scope.distance,
        fortType: v.fortType,
        includeEmpty: v.includeEmpty ? 1 : 0,
        template: v.template || '',
      } as FortChangeUpdate)
      .subscribe({
        // The server names what is wrong -- which alarm already uses these settings, which
        // field a file got wrong. A fixed string threw that away. See #567, #568.
        error: (err: { error?: { error?: string } }) => {
          this.snackBar.open(err?.error?.error ?? this.i18n.instant('FORT_CHANGES.UPDATE_FAILED'), this.i18n.instant('COMMON.OK'), {
            duration: 6000,
          });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open(this.i18n.instant('FORT_CHANGES.UPDATE_SUCCESS'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
