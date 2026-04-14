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

import { EVENT_TYPE_INFO, getDisplayName, getGruntIconUrl, hasNoGenderVariants, isEventType } from './invasion.constants';
import { Invasion, InvasionUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { InvasionService } from '../../core/services/invasion.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

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
  ],
  selector: 'app-invasion-edit-dialog',
  standalone: true,
  styleUrl: './invasion-edit-dialog.component.scss',
  templateUrl: './invasion-edit-dialog.component.html',
})
export class InvasionEditDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly invasionService = inject(InvasionService);
  private readonly snackBar = inject(MatSnackBar);

  readonly data = inject<Invasion>(MAT_DIALOG_DATA);

  readonly dialogRef = inject(MatDialogRef<InvasionEditDialogComponent>);
  form = this.fb.group({
    clean: [this.data.clean === 1],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    distanceMode: [this.data.distance === 0 ? 'areas' : ('distance' as 'areas' | 'distance')],
    gender: [this.data.gender],
    ping: [this.data.ping ?? ''],
    template: [this.data.template ?? ''],
  });

  readonly hideGender = hasNoGenderVariants(this.data.gruntType);
  readonly isEvent = isEventType(this.data.gruntType);
  readonly isWebhook = inject(AuthService).isImpersonating();

  saving = signal(false);

  getDisplayName(): string {
    return getDisplayName(this.data.gruntType) || this.i18n.instant('INVASIONS.UNKNOWN_GRUNT');
  }

  getEventColor(): string {
    return EVENT_TYPE_INFO[this.data.gruntType ?? '']?.color ?? '';
  }

  getEventIcon(): string {
    return EVENT_TYPE_INFO[this.data.gruntType ?? '']?.icon ?? '';
  }

  getEventImgUrl(): string {
    return EVENT_TYPE_INFO[this.data.gruntType ?? '']?.imgUrl ?? '';
  }

  getGenderLabel(): string {
    switch (this.data.gender) {
      case 1:
        return this.i18n.instant('INVASIONS.GENDER_MALE');
      case 2:
        return this.i18n.instant('INVASIONS.GENDER_FEMALE');
      default:
        return this.i18n.instant('INVASIONS.GENDER_ANY_LABEL');
    }
  }

  getGruntIcon(): string {
    return getGruntIconUrl(this.data.gruntType);
  }

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0);
    else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1);
  }

  save(): void {
    this.saving.set(true);
    const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    this.invasionService
      .update(this.data.uid, {
        clean: v.clean ? 1 : 0,
        distance: dist,
        gender: this.hideGender ? 0 : (v.gender ?? 0),
        gruntType: this.data.gruntType ?? '',
        ping: v.ping || null,
        template: v.template || null,
      } as InvasionUpdate)
      .subscribe({
        error: () => {
          this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_FAILED_UPDATE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_UPDATED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
