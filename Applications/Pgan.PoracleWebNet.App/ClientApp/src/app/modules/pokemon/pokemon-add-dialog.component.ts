import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
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
import { forkJoin } from 'rxjs';

import { syncUnscanned } from './include-unscanned';
import { MonsterCreate, LinkSide } from '../../core/models';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MonsterService } from '../../core/services/monster.service';
import { PoracleConfigService } from '../../core/services/poracle-config.service';
import { AddRefusalComponent } from '../../shared/components/add-refusal/add-refusal.component';
import { DeliverToFieldComponent } from '../../shared/components/deliver-to/deliver-to-field.component';
import { PersonalFilterPanelComponent } from '../../shared/components/personal-filter-panel/personal-filter-panel.component';
import { PokemonSelectorComponent } from '../../shared/components/pokemon-selector/pokemon-selector.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { AddDialogData, AddRefusal, editExistingResult, ExistingRule, finishAdd, settle } from '../../shared/utils/add-outcome';
import { AlarmScope, scopeToFields } from '../../shared/utils/alarm-scope';
import { compose } from '../../shared/utils/clean-flags';
import { ANY_COSTUME, costumeHintKey } from '../../shared/utils/costumes';
import { injectDeliverTo } from '../../shared/utils/deliver-to';
import { minTimeLabel, minTimeOptions } from '../../shared/utils/min-time';
import { compose as composePersonal } from '../../shared/utils/personal-flags';

@Component({
  imports: [
    DeliverToFieldComponent,
    AddRefusalComponent,
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatIconModule,
    MatTabsModule,
    MatExpansionModule,
    MatRadioModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    PokemonSelectorComponent,
    TemplateSelectorComponent,
    TranslatePipe,
    PersonalFilterPanelComponent,
    ScopePickerComponent,
  ],
  selector: 'app-pokemon-add-dialog',
  standalone: true,
  styleUrl: './pokemon-add-dialog.component.scss',
  templateUrl: './pokemon-add-dialog.component.html',
})
export class PokemonAddDialogComponent implements OnInit {
  private readonly alertDefaults = inject(AlertDefaultsService);

  private readonly data = inject<AddDialogData | null>(MAT_DIALOG_DATA, { optional: true });

  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly masterData = inject(MasterDataService);
  private readonly monsterService = inject(MonsterService);
  private readonly poracleConfig = inject(PoracleConfigService);
  private readonly snackBar = inject(MatSnackBar);
  selectedPokemonIds = signal<number[]>([]);

  readonly availableForms = computed(() => {
    const ids = this.selectedPokemonIds();
    if (ids.length !== 1 || ids[0] === 0) return [];
    return this.masterData.getFormsForPokemon(ids[0]);
  });

  /** Tracks whether the user has manually changed the cap since the default was applied. */
  readonly capTouched = signal(false);
  readonly deliver = injectDeliverTo();
  readonly dialogRef = inject(MatDialogRef<PokemonAddDialogComponent>);

  /** Whether to show the edit-in-place toggle at all -- see MonsterService.editSupported. */
  readonly editSupported = this.monsterService.editSupported;

  filtersForm = this.fb.group({
    atk: [0, [Validators.min(0), Validators.max(15)]],
    // 9000 = any costume. Never let this default to 0 -- that is "no costume", a real filter.
    costume: [ANY_COSTUME],
    def: [0, [Validators.min(0), Validators.max(15)]],
    form: [0],
    forms: [[] as number[]],
    gender: [0],
    // Poracle stores "include Pokemon nobody has encountered" as min_iv -1, which also means any IV.
    // The Min IV box can't take -1, so this switch is the only way to ask for it. See syncUnscanned().
    includeUnscanned: [false],
    maxAtk: [15, [Validators.min(0), Validators.max(15)]],
    maxCp: [9000, [Validators.min(0), Validators.max(9000)]],
    maxDef: [15, [Validators.min(0), Validators.max(15)]],
    maxIv: [100, [Validators.min(0), Validators.max(100)]],
    maxLevel: [55, [Validators.min(0), Validators.max(55)]],
    maxSize: [5],
    maxSta: [15, [Validators.min(0), Validators.max(15)]],
    maxWeight: [9000000],
    minCp: [0, [Validators.min(0), Validators.max(9000)]],
    minIv: [0, [Validators.min(0), Validators.max(100)]],
    minLevel: [0, [Validators.min(0), Validators.max(55)]],
    minTime: [0],
    minWeight: [0],
    // Always present, even when the toggles are hidden, so the form shape does not depend on the server.
    personalBackground: [false],
    personalShiny: [false],
    size: [-1],
    sta: [0, [Validators.min(0), Validators.max(15)]],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  notifForm = this.fb.group({
    clean: [false],
    destination: [null as LinkSide | null],
    editInPlace: [false],
    // Empty means the profile pin, which is what "set a distance" has always meant. A label points the
    // radius at a saved place instead.
    placeLabel: [''],
    template: [''],
  });

  /** Whether the connected PoracleNG stores personal shiny rules at all. */
  readonly personalSupported = computed(() => this.poracleConfig.serverConfig().personalSupported);

  /** Caps offered by Poracle (e.g. [50] or [50, 51]). Empty = hide the cap picker entirely. */
  readonly pvpCaps = computed(() => this.poracleConfig.serverConfig().pvpCaps);

  pvpForm = this.fb.group({
    pvpRankingBest: [1],
    pvpRankingCap: [0],
    // 0 base, 1 any mega, 2 Mega X, 3 Mega Y — PoracleNG's pvp_ranking_evolution.
    pvpRankingEvolution: [0],
    pvpRankingLeague: [0],
    pvpRankingMinCp: [0],
    pvpRankingWorst: [100],
  });

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

  /** Whether to render the cap picker at all — only when Poracle offers more than one cap. */
  readonly showCapPicker = computed(() => this.pvpCaps().length > 1);

  /** The hint under the costume select, which changes with the selection. */
  costumeHint(): string {
    return costumeHintKey(this.filtersForm.controls.costume.value ?? ANY_COSTUME, this.costumeNamesAvailable());
  }

  /** Whether the masterfile's costume names loaded; drives the hint and nothing else. */
  costumeNamesAvailable(): boolean {
    return this.masterData.costumesAvailable();
  }

  /** The named costumes for the select, newest first. */
  costumeOptions(): { id: number; name: string }[] {
    return this.masterData.getCostumes();
  }

  /** The refusal named a stored rule: close and let the list open that one for editing. */
  editExisting(rule: ExistingRule): void {
    this.dialogRef.close(editExistingResult(rule));
  }

  isFormValid(): boolean {
    return this.selectedPokemonIds().length > 0 && this.filtersForm.valid && this.notifForm.valid;
  }

  /** The preset list, widened to keep whatever the rule already holds. */
  minTimeChoices(): number[] {
    return minTimeOptions(this.filtersForm.controls.minTime.value ?? 0);
  }

  minTimeParams(seconds: number): Record<string, number> | undefined {
    return minTimeLabel(seconds).params;
  }

  minTimeText(seconds: number): string {
    return minTimeLabel(seconds).key;
  }

  ngOnInit(): void {
    // Pre-fill the cap from Poracle's admin-configured default. Users can still override.
    this.poracleConfig.load().subscribe(cfg => {
      this.pvpForm.controls.pvpRankingCap.setValue(cfg.defaultPvpCap);
    });

    this.pvpForm.controls.pvpRankingCap.valueChanges.subscribe(() => {
      this.capTouched.set(true);
    });

    syncUnscanned(this.filtersForm.controls);
  }

  onPokemonSelected(ids: number[]): void {
    this.selectedPokemonIds.set(ids);
  }

  save(): void {
    if (!this.isFormValid()) return;
    this.saving.set(true);
    this.refusal.set(null);

    const filters = this.filtersForm.getRawValue();
    const pvp = this.pvpForm.getRawValue();
    const notif = this.notifForm.getRawValue();
    const scope = scopeToFields(this.scope());

    // PoracleNG models `form` as a single int per tracking entry, so a multi-form
    // selection fans out into one alarm per form. When specific forms are available we
    // use the multi-select; an empty selection means "all forms" (0). Otherwise we fall
    // back to the manual form-id number input.
    const formIds =
      this.availableForms().length > 0 ? (filters.forms && filters.forms.length > 0 ? filters.forms : [0]) : [filters.form ?? 0];

    const creates = this.selectedPokemonIds().flatMap(pokemonId =>
      formIds.map(form => {
        const monster: MonsterCreate = {
          ...this.deliver.createFields(this.notifForm.controls.destination.value),
          overrideAreas: scope.overrideAreas,
          overrideLocationLabel: scope.overrideLocationLabel,
          atk: filters.atk ?? 0,
          clean: compose(!!notif.clean, !!notif.editInPlace, false),
          costume: filters.costume ?? ANY_COSTUME,
          def: filters.def ?? 0,
          distance: scope.distance,
          form,
          gender: filters.gender ?? 0,
          maxAtk: filters.maxAtk ?? 15,
          maxCp: filters.maxCp ?? 9000,
          maxDef: filters.maxDef ?? 15,
          maxIv: filters.maxIv ?? 100,
          maxLevel: filters.maxLevel ?? 55,
          maxSize: filters.maxSize ?? 5,
          maxSta: filters.maxSta ?? 15,
          maxWeight: filters.maxWeight ?? 9000000,
          minCp: filters.minCp ?? 0,
          minIv: filters.includeUnscanned ? -1 : (filters.minIv ?? 0),
          minLevel: filters.minLevel ?? 0,
          minTime: filters.minTime ?? 0,
          minWeight: filters.minWeight ?? 0,
          personal: composePersonal(!!filters.personalShiny, !!filters.personalBackground),
          pokemonId,
          pvpRankingBest: pvp.pvpRankingLeague ? (pvp.pvpRankingBest ?? 1) : 1,
          pvpRankingCap: pvp.pvpRankingLeague ? (pvp.pvpRankingCap ?? 0) : 0,
          pvpRankingEvolution: pvp.pvpRankingLeague ? (pvp.pvpRankingEvolution ?? 0) : 0,
          pvpRankingLeague: pvp.pvpRankingLeague ?? 0,
          pvpRankingMinCp: pvp.pvpRankingLeague ? (pvp.pvpRankingMinCp ?? 0) : 0,
          pvpRankingWorst: pvp.pvpRankingLeague ? (pvp.pvpRankingWorst ?? 100) : 4096,
          size: filters.size ?? -1,
          sta: filters.sta ?? 0,
          template: notif.template || null,
        };
        return this.monsterService.create(monster);
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
