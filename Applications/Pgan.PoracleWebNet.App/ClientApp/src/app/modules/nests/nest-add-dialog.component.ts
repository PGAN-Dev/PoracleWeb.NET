import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
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

import { LinkSide } from '../../core/models';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { NestService } from '../../core/services/nest.service';
import { AddRefusalComponent } from '../../shared/components/add-refusal/add-refusal.component';
import { DeliverToFieldComponent } from '../../shared/components/deliver-to/deliver-to-field.component';
import { PokemonSelectorComponent } from '../../shared/components/pokemon-selector/pokemon-selector.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { AddDialogData, AddRefusal, editExistingResult, ExistingRule, finishAdd, settle } from '../../shared/utils/add-outcome';
import { AlarmScope, scopeToFields } from '../../shared/utils/alarm-scope';
import { injectDeliverTo } from '../../shared/utils/deliver-to';

@Component({
  imports: [
    DeliverToFieldComponent,
    AddRefusalComponent,
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
    PokemonSelectorComponent,
    TemplateSelectorComponent,
    MatSelectModule,
    ScopePickerComponent,
  ],
  selector: 'app-nest-add-dialog',
  standalone: true,
  styleUrl: './nest-add-dialog.component.scss',
  templateUrl: './nest-add-dialog.component.html',
})
export class NestAddDialogComponent {
  private readonly alertDefaults = inject(AlertDefaultsService);

  private readonly data = inject<AddDialogData | null>(MAT_DIALOG_DATA, { optional: true });

  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly nestService = inject(NestService);
  private readonly snackBar = inject(MatSnackBar);
  readonly deliver = injectDeliverTo();
  readonly dialogRef = inject(MatDialogRef<NestAddDialogComponent>);

  form = this.fb.group({
    clean: [false],
    destination: [null as LinkSide | null],
    minSpawnAvg: [0],
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

  selectedPokemonIds = signal<number[]>([]);

  /** The refusal named a stored rule: close and let the list open that one for editing. */
  editExisting(rule: ExistingRule): void {
    this.dialogRef.close(editExistingResult(rule));
  }

  onPokemonSelected(ids: number[]): void {
    this.selectedPokemonIds.set(ids);
  }

  save(): void {
    if (this.selectedPokemonIds().length === 0) return;
    this.saving.set(true);
    this.refusal.set(null);
    const v = this.form.getRawValue();
    const scope = scopeToFields(this.scope());
    const creates = this.selectedPokemonIds().map(pokemonId =>
      this.nestService.create({
        ...this.deliver.createFields(this.form.controls.destination.value),
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        clean: v.clean ? 1 : 0,
        distance: scope.distance,
        minSpawnAvg: v.minSpawnAvg ?? 0,
        pokemonId,
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
}
