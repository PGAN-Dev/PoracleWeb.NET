import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
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

import { MaxBattleCreate, LinkSide } from '../../core/models';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { MaxBattleService } from '../../core/services/max-battle.service';
import { ScannerService } from '../../core/services/scanner.service';
import { AddRefusalComponent } from '../../shared/components/add-refusal/add-refusal.component';
import { DeliverToFieldComponent } from '../../shared/components/deliver-to/deliver-to-field.component';
import { PokemonSelectorComponent } from '../../shared/components/pokemon-selector/pokemon-selector.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { AddDialogData, AddRefusal, editExistingResult, ExistingRule, finishAdd, settleCreates } from '../../shared/utils/add-outcome';
import { AlarmScope, scopeToFields } from '../../shared/utils/alarm-scope';
import { compose } from '../../shared/utils/clean-flags';
import { injectDeliverTo } from '../../shared/utils/deliver-to';

/** Max Battle levels as defined in PoracleNG util.json */
interface MaxBattleLevel {
  gmax: boolean;
  label: string;
  value: number;
}

@Component({
  imports: [
    DeliverToFieldComponent,
    AddRefusalComponent,
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
    ScopePickerComponent,
  ],
  selector: 'app-max-battle-add-dialog',
  standalone: true,
  styleUrl: './max-battle-add-dialog.component.scss',
  templateUrl: './max-battle-add-dialog.component.html',
})
export class MaxBattleAddDialogComponent {
  private readonly alertDefaults = inject(AlertDefaultsService);

  private readonly data = inject<AddDialogData | null>(MAT_DIALOG_DATA, { optional: true });

  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly maxBattleService = inject(MaxBattleService);
  private readonly scannerService = inject(ScannerService);
  private readonly snackBar = inject(MatSnackBar);

  commonForm = this.fb.group({
    clean: [false],
    destination: [null as LinkSide | null],
    form: [0],
    gmaxOnly: [false],
    template: [''],
  });

  readonly deliver = injectDeliverTo();

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

  /** The refusal named a stored rule: close and let the list open that one for editing. */
  editExisting(rule: ExistingRule): void {
    this.dialogRef.close(editExistingResult(rule));
  }

  onPokemonSelected(ids: number[]): void {
    this.selectedPokemonIds.set(ids);
  }

  save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);
    this.refusal.set(null);
    const common = this.commonForm.getRawValue();
    const scope = scopeToFields(this.scope());

    const maxBattles: MaxBattleCreate[] = [];

    if (this.tabIndex === 0) {
      // By Level — one alarm per selected level, pokemonId = 9000 (any)
      for (const levelVal of this.selectedLevels()) {
        const levelDef = this.levels.find(l => l.value === levelVal);
        const maxBattle: MaxBattleCreate = {
          ...this.deliver.createFields(this.commonForm.controls.destination.value),
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
        maxBattles.push(maxBattle);
      }
    } else {
      // By Pokemon — one alarm per selected Pokemon, level = 9000 (any)
      for (const pokemonId of this.selectedPokemonIds()) {
        const maxBattle: MaxBattleCreate = {
          ...this.deliver.createFields(this.commonForm.controls.destination.value),
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
        maxBattles.push(maxBattle);
      }
    }

    // One row is a single Add, whose refusal keeps the dialog open with the reason inline; several go as one
    // batch request that PoracleNG answers with one dry run and one commit (#1090). See settleCreates.
    settleCreates(
      maxBattles,
      m => this.maxBattleService.create(m),
      m => this.maxBattleService.createBatch(m),
    ).subscribe(results => {
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

  selectAllLevels(): void {
    this.selectedLevels.set(this.levels.map(l => l.value));
  }

  toggleLevel(level: number): void {
    this.selectedLevels.update(levels => (levels.includes(level) ? levels.filter(l => l !== level) : [...levels, level]));
  }
}
