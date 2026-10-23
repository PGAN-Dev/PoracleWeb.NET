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
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { LureService } from '../../core/services/lure.service';
import { AddRefusalComponent } from '../../shared/components/add-refusal/add-refusal.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { TranslatePluralPipe } from '../../shared/pipes/translate-plural.pipe';
import { AddDialogData, AddRefusal, editExistingResult, ExistingRule, finishAdd, settle } from '../../shared/utils/add-outcome';
import { AlarmScope, scopeToFields } from '../../shared/utils/alarm-scope';
import { compose } from '../../shared/utils/clean-flags';

interface LureOption {
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
    MatSelectModule,
    ScopePickerComponent,
  ],
  selector: 'app-lure-add-dialog',
  standalone: true,
  styleUrl: './lure-add-dialog.component.scss',
  templateUrl: './lure-add-dialog.component.html',
})
export class LureAddDialogComponent {
  private readonly alertDefaults = inject(AlertDefaultsService);
  private readonly data = inject<AddDialogData | null>(MAT_DIALOG_DATA, { optional: true });

  private readonly fb = inject(FormBuilder);

  private readonly i18n = inject(I18nService);
  private readonly icons = inject(IconService);
  private readonly lureService = inject(LureService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<LureAddDialogComponent>);
  form = this.fb.group({
    clean: [false],
    editInPlace: [false],
    template: [''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  lureTypes: LureOption[] = [
    { id: 501, name: this.i18n.instant('LURES.TYPE_NORMAL'), color: '#FF9800' },
    { id: 502, name: this.i18n.instant('LURES.TYPE_GLACIAL'), color: '#03A9F4' },
    { id: 503, name: this.i18n.instant('LURES.TYPE_MOSSY'), color: '#4CAF50' },
    { id: 504, name: this.i18n.instant('LURES.TYPE_MAGNETIC'), color: '#9E9E9E' },
    { id: 505, name: this.i18n.instant('LURES.TYPE_RAINY'), color: '#2196F3' },
    { id: 506, name: this.i18n.instant('LURES.TYPE_GOLDEN'), color: '#FFC107' },
  ];

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

  selectedLureIds = signal<number[]>([]);

  /** The refusal named a stored rule: close and let the list open that one for editing. */
  editExisting(rule: ExistingRule): void {
    this.dialogRef.close(editExistingResult(rule));
  }

  getLureIcon(lureId: number): string {
    return this.icons.getItemUrl(lureId);
  }

  save(): void {
    if (this.selectedLureIds().length === 0) return;
    this.saving.set(true);
    this.refusal.set(null);
    const v = this.form.getRawValue();
    const scope = scopeToFields(this.scope());
    const creates = this.selectedLureIds().map(lureId =>
      this.lureService.create({
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        // New lures have no prior bits, so compose bits 1 (auto-delete) and 2 (edit-in-place) directly.
        clean: compose(!!v.clean, !!v.editInPlace, false),
        distance: scope.distance,
        lureId,
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

  toggleLure(id: number): void {
    this.selectedLureIds.update(ids => (ids.includes(id) ? ids.filter(i => i !== id) : [...ids, id]));
  }
}
