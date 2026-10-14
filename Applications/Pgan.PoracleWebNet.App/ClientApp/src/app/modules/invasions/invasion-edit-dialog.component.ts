import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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

import { EVENT_TYPE_INFO, getGruntDisplayName, getGruntIconPath, isEventType, isGenderFixed } from './invasion.constants';
import { Invasion, InvasionUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { InvasionService } from '../../core/services/invasion.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { PoracleConfigService } from '../../core/services/poracle-config.service';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { AUTO_DELETE, isAutoDelete, preserve } from '../../shared/utils/clean-flags';

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
    TranslatePipe,
    TemplateSelectorComponent,
    ScopePickerComponent,
  ],
  selector: 'app-invasion-edit-dialog',
  standalone: true,
  styleUrl: './invasion-edit-dialog.component.scss',
  templateUrl: './invasion-edit-dialog.component.html',
})
export class InvasionEditDialogComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly icons = inject(IconService);
  private readonly invasionService = inject(InvasionService);
  private readonly masterData = inject(MasterDataService);
  private readonly poracleConfig = inject(PoracleConfigService);
  private readonly snackBar = inject(MatSnackBar);

  readonly data = inject<Invasion>(MAT_DIALOG_DATA);

  readonly dialogRef = inject(MatDialogRef<InvasionEditDialogComponent>);

  form = this.fb.group({
    clean: [isAutoDelete(this.data.clean)],
    gender: [this.data.gender],
    // Always present even when the control is hidden -- see pingSupported below -- so a mention
    // round-trips unchanged through a save this dialog makes for an unrelated field.
    ping: [this.data.ping ?? '', [Validators.maxLength(255)]],
    template: [this.data.template ?? ''],
  });

  readonly hideGender = isGenderFixed(this.data.gruntType);

  readonly isEvent = isEventType(this.data.gruntType);
  readonly isWebhook = inject(AuthService).isImpersonating();

  /** Whether to show the Mention control at all -- see pokemon-edit-dialog's pingSupported for why. */
  readonly pingSupported = computed(() => this.poracleConfig.serverConfig().pingSupported);

  saving = signal(false);
  /** The alarm's current scope, read back into the shared picker. */
  readonly scope = signal<AlarmScope>(scopeOf(this.data.overrideLocationLabel, this.data.overrideAreas, this.data.distance));
  readonly selectedGender = toSignal(this.form.controls.gender.valueChanges, { initialValue: this.data.gender });

  getDisplayName(): string {
    return getGruntDisplayName(
      this.data.gruntType,
      this.data.gender,
      key => this.i18n.instant(key),
      this.masterData.getGruntName(this.data.gruntType, this.data.gender),
      this.masterData.isGruntNameUntranslated(this.data.gruntType, this.data.gender),
    );
  }

  getEventColor(): string {
    return EVENT_TYPE_INFO[this.data.gruntType ?? '']?.color ?? '';
  }

  getEventIcon(): string {
    return EVENT_TYPE_INFO[this.data.gruntType ?? '']?.icon ?? '';
  }

  getEventImgUrl(): string {
    const path = EVENT_TYPE_INFO[this.data.gruntType ?? '']?.imgPath;
    return path ? this.icons.getPackUrl(path) : '';
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
    return this.icons.getPackUrl(getGruntIconPath(this.data.gruntType, this.selectedGender()));
  }

  ngOnInit(): void {
    this.poracleConfig.load().subscribe();
  }

  save(): void {
    this.saving.set(true);
    const v = this.form.getRawValue();
    const scope = scopeToFields(this.scope());
    this.invasionService
      .update(this.data.uid, {
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        clean: preserve(this.data.clean, AUTO_DELETE, v.clean ? 1 : 0),
        distance: scope.distance,
        // Preserve the stored gender when the dropdown is hidden — a Mixed Male alarm
        // (gender=1) must stay at 1 across edits; zeroing it would widen the filter to
        // also match female Mixed grunts.
        gender: this.hideGender ? (this.data.gender ?? 0) : (v.gender ?? 0),
        gruntType: this.data.gruntType ?? '',
        ping: v.ping ?? '',
        template: v.template || '',
      } as InvasionUpdate)
      .subscribe({
        // The server names what is wrong -- which alarm already uses these settings, which
        // field a file got wrong. A fixed string threw that away. See #567, #568.
        error: (err: { error?: { error?: string } }) => {
          this.snackBar.open(err?.error?.error ?? this.i18n.instant('INVASIONS.SNACK_FAILED_UPDATE'), this.i18n.instant('TOAST.OK'), {
            duration: 6000,
          });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_UPDATED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
