import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
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
import { catchError, forkJoin, of } from 'rxjs';

import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { NestService } from '../../core/services/nest.service';
import { PlacesService } from '../../core/services/places.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { PokemonSelectorComponent } from '../../shared/components/pokemon-selector/pokemon-selector.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { scopeToFields } from '../../shared/utils/alarm-scope';

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
    TranslatePipe,
    PokemonSelectorComponent,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
    MatSelectModule,
  ],
  selector: 'app-nest-add-dialog',
  standalone: true,
  styleUrl: './nest-add-dialog.component.scss',
  templateUrl: './nest-add-dialog.component.html',
})
export class NestAddDialogComponent {
  private readonly alertDefaults = inject(AlertDefaultsService);
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly nestService = inject(NestService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<NestAddDialogComponent>);
  form = this.fb.group({
    clean: [false],
    distanceKm: [this.alertDefaults.defaultDistanceKm()],
    distanceMode: [this.alertDefaults.defaultMode()],
    minSpawnAvg: [0],
    // Empty means the profile pin, which is what a radius has always meant.
    placeLabel: [this.alertDefaults.defaultPlaceLabel()],
    template: [''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  readonly places = inject(PlacesService);

  saving = signal(false);
  selectedPokemonIds = signal<number[]>([]);
  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.placeLabel.setValue('');
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0);
    else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1);
  }

  onPokemonSelected(ids: number[]): void {
    this.selectedPokemonIds.set(ids);
  }

  save(): void {
    if (this.selectedPokemonIds().length === 0) return;
    this.saving.set(true);
    const v = this.form.getRawValue();
    // One conversion for all three answers, shared with the card chip and the scope sheet.
    const scope = scopeToFields(
      v.distanceMode === 'areas'
        ? { mode: 'profile' }
        : { distanceKm: v.distanceKm ?? 1, mode: v.placeLabel ? 'place' : 'profile', placeLabel: v.placeLabel ?? '' },
    );
    const creates = this.selectedPokemonIds().map(pokemonId =>
      this.nestService.create({
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        clean: v.clean ? 1 : 0,
        distance: scope.distance,
        minSpawnAvg: v.minSpawnAvg ?? 0,
        pokemonId,
        template: v.template || null,
      }),
    );
    // forkJoin fails fast, so one refused alarm aborted the whole batch: the creates that had already
    // succeeded were never reported, the dialog stayed open and the list never reloaded. Each request
    // settles on its own now, and the toast says how many landed. See #577.
    forkJoin(creates.map(c => c.pipe(catchError((err: { error?: { error?: string } }) => of({ failed: err }))))).subscribe({
      // The server names what is wrong -- which alarm already uses these settings, which
      // field a file got wrong. A fixed string threw that away. See #567, #568.
      // Each create settles on its own, so a refused one no longer hides the ones that landed.
      // The first refusal's message is shown, because it names what is in the way. See #577.
      next: (results: ({ uid?: number } | { failed: { error?: { error?: string } } })[]) => {
        const refused = results.filter((r): r is { failed: { error?: { error?: string } } } => 'failed' in r);
        // Three outcomes, not two: refused (409), already tracked (200 with no uid), and created. The
        // pokemon dialog has split these since #495; the rest reported duplicates as creations. See #605.
        const landed = results.filter((r): r is { uid?: number } => !('failed' in r));
        const created = landed.filter(r => (r.uid ?? 0) > 0).length;
        const duplicates = landed.length - created;
        this.saving.set(false);

        if (refused.length > 0) {
          this.snackBar.open(
            refused[0].failed?.error?.error ?? this.i18n.instant('NESTS.SNACK_FAILED_CREATE'),
            this.i18n.instant('COMMON.OK'),
            { duration: 6000 },
          );
        } else {
          const message =
            duplicates > 0
              ? this.i18n.instant('ALARM.SNACK_CREATED_WITH_DUPLICATES', { count: created, duplicates })
              : this.i18n.instant('NESTS.SNACK_CREATED', { count: created });
          this.snackBar.open(message, this.i18n.instant('COMMON.OK'), { duration: 4000 });
        }

        // Close either way: whatever was created is real, and the list must reload to show it.
        this.dialogRef.close(true);
      },
    });
  }
}
