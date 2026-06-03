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
import { TranslateModule } from '@ngx-translate/core';

import { Lure, LureUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { LureService } from '../../core/services/lure.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { AUTO_DELETE, compose, EDIT, isAutoDelete, isEdit, preserve } from '../../shared/utils/clean-flags';

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
    TranslateModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-lure-edit-dialog',
  standalone: true,
  styleUrl: './lure-edit-dialog.component.scss',
  templateUrl: './lure-edit-dialog.component.html',
})
export class LureEditDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly lureService = inject(LureService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Lure>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<LureEditDialogComponent>);
  form = this.fb.group({
    clean: [isAutoDelete(this.data.clean)],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    distanceMode: [this.data.distance === 0 ? 'areas' : ('distance' as 'areas' | 'distance')],
    editInPlace: [isEdit(this.data.clean)],
    ping: [this.data.ping ?? ''],
    template: [this.data.template ?? ''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  saving = signal(false);
  getLureIcon(): string {
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/reward/item/${this.data.lureId}.png`;
  }

  getLureName(id: number): string {
    switch (id) {
      case 501:
        return 'Normal';
      case 502:
        return 'Glacial';
      case 503:
        return 'Mossy';
      case 504:
        return 'Magnetic';
      case 505:
        return 'Rainy';
      case 506:
        return 'Golden';
      default:
        return `Lure #${id}`;
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
    this.lureService
      .update(this.data.uid, {
        // Only bits 1 (auto-delete) and 2 (edit-in-place) are user-editable here; preserve
        // any summary bit (4) or future bits the bot may have set on this alarm.
        clean: preserve(this.data.clean, AUTO_DELETE | EDIT, compose(!!v.clean, !!v.editInPlace, false)),
        distance: dist,
        lureId: this.data.lureId,
        ping: v.ping || null,
        template: v.template || null,
      } as LureUpdate)
      .subscribe({
        error: () => {
          this.snackBar.open(this.i18n.instant('LURES.SNACK_FAILED_UPDATE'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open(this.i18n.instant('LURES.SNACK_UPDATED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
