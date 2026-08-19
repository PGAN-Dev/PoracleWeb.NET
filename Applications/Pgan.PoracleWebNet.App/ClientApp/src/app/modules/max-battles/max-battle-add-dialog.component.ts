import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslatePipe } from '@ngx-translate/core';
import { catchError, forkJoin, of } from 'rxjs';

import { MaxBattleCreate } from '../../core/models';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { MaxBattleService } from '../../core/services/max-battle.service';
import { PlacesService } from '../../core/services/places.service';
import { ScannerService } from '../../core/services/scanner.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { PokemonSelectorComponent } from '../../shared/components/pokemon-selector/pokemon-selector.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { scopeToFields } from '../../shared/utils/alarm-scope';
import { compose } from '../../shared/utils/clean-flags';

/** Max Battle levels as defined in PoracleNG util.json */
interface MaxBattleLevel {
  gmax: boolean;
  label: string;
  value: number;
}

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
    MatTabsModule,
    MatCheckboxModule,
    MatRadioModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    TranslatePipe,
    PokemonSelectorComponent,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-max-battle-add-dialog',
  standalone: true,
  styleUrl: './max-battle-add-dialog.component.scss',
  templateUrl: './max-battle-add-dialog.component.html',
})
export class MaxBattleAddDialogComponent {
  private readonly alertDefaults = inject(AlertDefaultsService);
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly maxBattleService = inject(MaxBattleService);
  private readonly scannerService = inject(ScannerService);
  private readonly snackBar = inject(MatSnackBar);
  commonForm = this.fb.group({
    clean: [false],
    distanceKm: [this.alertDefaults.defaultDistanceKm()],
    distanceMode: [this.alertDefaults.defaultMode()],
    form: [0],
    gmaxOnly: [false],
    // Empty means the profile pin, which is what a radius has always meant.
    placeLabel: [this.alertDefaults.defaultPlaceLabel()],
    template: [''],
  });

  readonly dialogRef = inject(MatDialogRef<MaxBattleAddDialogComponent>);

  readonly isWebhook = inject(AuthService).isImpersonating();

  /** PoracleNG max battle levels: 1-5 = Dynamax, 7 = Gigantamax, 8 = Legendary Gigantamax */
  readonly levels: MaxBattleLevel[] = [
    { gmax: false, label: this.i18n.instant('MAX_BATTLES.LEVEL_1'), value: 1 },
    { gmax: false, label: this.i18n.instant('MAX_BATTLES.LEVEL_2'), value: 2 },
    { gmax: false, label: this.i18n.instant('MAX_BATTLES.LEVEL_3'), value: 3 },
    { gmax: false, label: this.i18n.instant('MAX_BATTLES.LEVEL_4'), value: 4 },
    { gmax: false, label: this.i18n.instant('MAX_BATTLES.LEVEL_5'), value: 5 },
    { gmax: true, label: this.i18n.instant('MAX_BATTLES.LEVEL_GMAX'), value: 7 },
    { gmax: true, label: this.i18n.instant('MAX_BATTLES.LEVEL_GMAX_LEGENDARY'), value: 8 },
  ];

  maxBattlePokemonIds = signal<number[] | null>(null);

  readonly places = inject(PlacesService);
  saving = signal(false);
  selectedLevels = signal<number[]>([]);
  selectedPokemonIds = signal<number[]>([]);

  tabIndex = 0;

  constructor() {
    this.scannerService.getMaxBattlePokemonIds().subscribe(ids => {
      this.maxBattlePokemonIds.set(ids.length > 0 ? ids : null);
    });
  }

  canSave(): boolean {
    if (this.tabIndex === 0) {
      return this.selectedLevels().length > 0;
    }
    return this.selectedPokemonIds().length > 0;
  }

  onDistanceModeChange(): void {
    if (this.commonForm.controls.distanceMode.value === 'areas') this.commonForm.controls.placeLabel.setValue('');
    if (this.commonForm.controls.distanceMode.value === 'areas') {
      this.commonForm.controls.distanceKm.setValue(0);
    } else {
      if (!this.commonForm.controls.distanceKm.value) {
        this.commonForm.controls.distanceKm.setValue(1);
      }
    }
  }

  onPokemonSelected(ids: number[]): void {
    this.selectedPokemonIds.set(ids);
  }

  save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);
    const common = this.commonForm.getRawValue();
    // One conversion for all three answers, shared with the card chip and the scope sheet.
    const scope = scopeToFields(
      common.distanceMode === 'areas'
        ? { mode: 'profile' }
        : { distanceKm: common.distanceKm ?? 1, mode: common.placeLabel ? 'place' : 'profile', placeLabel: common.placeLabel ?? '' },
    );

    const creates: ReturnType<typeof this.maxBattleService.create>[] = [];

    if (this.tabIndex === 0) {
      // By Level — one alarm per selected level, pokemonId = 9000 (any)
      for (const levelVal of this.selectedLevels()) {
        const levelDef = this.levels.find(l => l.value === levelVal);
        const maxBattle: MaxBattleCreate = {
          overrideAreas: scope.overrideAreas,
          overrideLocationLabel: scope.overrideLocationLabel,
          clean: compose(!!common.clean, false, false),
          distance: scope.distance,
          evolution: 9000,
          form: common.form ?? 0,
          gmax: levelDef?.gmax ? 1 : 0,
          level: levelVal,
          move: 9000,
          pokemonId: 9000,
          stationId: null,
          template: common.template || '',
        };
        creates.push(this.maxBattleService.create(maxBattle));
      }
    } else {
      // By Pokemon — one alarm per selected Pokemon, level = 9000 (any)
      for (const pokemonId of this.selectedPokemonIds()) {
        const maxBattle: MaxBattleCreate = {
          overrideAreas: scope.overrideAreas,
          overrideLocationLabel: scope.overrideLocationLabel,
          clean: compose(!!common.clean, false, false),
          distance: scope.distance,
          evolution: 9000,
          form: common.form ?? 0,
          gmax: common.gmaxOnly ? 1 : 0,
          level: 9000,
          move: 9000,
          pokemonId,
          stationId: null,
          template: common.template || '',
        };
        creates.push(this.maxBattleService.create(maxBattle));
      }
    }

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
          this.snackBar.open(refused[0].failed?.error?.error ?? this.i18n.instant('COMMON.ERROR'), this.i18n.instant('COMMON.OK'), {
            duration: 6000,
          });
        } else {
          const message =
            duplicates > 0
              ? this.i18n.instant('ALARM.SNACK_CREATED_WITH_DUPLICATES', { count: created, duplicates })
              : this.i18n.instant('COMMON.SAVED', { count: created });
          this.snackBar.open(message, this.i18n.instant('COMMON.OK'), { duration: 4000 });
        }

        // Close either way: whatever was created is real, and the list must reload to show it.
        this.dialogRef.close(true);
      },
    });
  }

  selectAllLevels(): void {
    this.selectedLevels.set(this.levels.map(l => l.value));
  }

  toggleLevel(level: number): void {
    this.selectedLevels.update(levels => (levels.includes(level) ? levels.filter(l => l !== level) : [...levels, level]));
  }
}
