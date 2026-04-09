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
import { TranslateModule } from '@ngx-translate/core';

import { FortChange, FortChangeUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { FortChangeService } from '../../core/services/fort-change.service';
import { I18nService } from '../../core/services/i18n.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

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
    TranslateModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
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
    clean: [this.data.clean === 1],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    distanceMode: [this.data.distance === 0 ? 'areas' : ('distance' as 'areas' | 'distance')],
    fortType: [this.data.fortType ?? 'everything'],
    includeEmpty: [this.data.includeEmpty === 1],
    ping: [this.data.ping ?? ''],
    template: [this.data.template ?? ''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  saving = signal(false);

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0);
    else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1);
  }

  save(): void {
    this.saving.set(true);
    const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    const changeTypes: string[] = [];
    if (v.changeTypeName) changeTypes.push('name');
    if (v.changeTypeLocation) changeTypes.push('location');
    if (v.changeTypeImageUrl) changeTypes.push('image_url');
    if (v.changeTypeRemoval) changeTypes.push('removal');
    if (v.changeTypeNew) changeTypes.push('new');

    this.fortChangeService
      .update(this.data.uid, {
        changeTypes,
        clean: v.clean ? 1 : 0,
        distance: dist,
        fortType: v.fortType,
        includeEmpty: v.includeEmpty ? 1 : 0,
        ping: v.ping || null,
        template: v.template || null,
      } as FortChangeUpdate)
      .subscribe({
        error: () => {
          this.snackBar.open('Failed to update alarm', this.i18n.instant('COMMON.OK'), { duration: 3000 });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open('Fort change alarm updated', this.i18n.instant('COMMON.OK'), { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
