import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslatePipe } from '@ngx-translate/core';

import { Monster, MonsterUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MonsterService } from '../../core/services/monster.service';
import { PoracleConfigService } from '../../core/services/poracle-config.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { WhereChipComponent } from '../../shared/components/where-chip/where-chip.component';
import { AUTO_DELETE, isAutoDelete, preserve } from '../../shared/utils/clean-flags';

@Component({
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatIconModule,
    MatExpansionModule,
    MatRadioModule,
    MatTabsModule,
    MatSnackBarModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
    TranslatePipe,
    WhereChipComponent,
  ],
  selector: 'app-pokemon-edit-dialog',
  standalone: true,
  styleUrl: './pokemon-edit-dialog.component.scss',
  templateUrl: './pokemon-edit-dialog.component.html',
})
export class PokemonEditDialogComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly monsterService = inject(MonsterService);
  private readonly poracleConfig = inject(PoracleConfigService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Monster>(MAT_DIALOG_DATA);
  readonly availableForms = computed(() => {
    return this.masterData.getFormsForPokemon(this.data.pokemonId);
  });

  readonly dialogRef = inject(MatDialogRef<PokemonEditDialogComponent>);

  form = this.fb.group({
    atk: [this.data.atk],
    clean: [isAutoDelete(this.data.clean)],
    def: [this.data.def],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    distanceMode: [this.data.distance === 0 ? 'areas' : ('distance' as 'areas' | 'distance')],
    form: [this.data.form],
    gender: [this.data.gender],
    maxAtk: [this.data.maxAtk],
    maxCp: [this.data.maxCp],
    maxDef: [this.data.maxDef],
    maxIv: [this.data.maxIv],
    maxLevel: [this.data.maxLevel],
    maxSize: [this.data.maxSize],
    maxSta: [this.data.maxSta],
    maxWeight: [this.data.maxWeight],
    minCp: [this.data.minCp],
    minIv: [this.data.minIv],
    minLevel: [this.data.minLevel],
    minWeight: [this.data.minWeight],
    // Read-only here on purpose: an alarm's place or areas are changed from the card chip, which is one
    // control in one place. This dialog only has to avoid destroying them, which it does by keeping
    // the stored label and sending the radius against it.
    placeLabel: [this.data.overrideLocationLabel ?? ''],
    pvpRankingBest: [this.data.pvpRankingBest],
    pvpRankingCap: [this.data.pvpRankingCap ?? 0],
    // 0 base, 1 any mega, 2 Mega X, 3 Mega Y — PoracleNG's pvp_ranking_evolution.
    pvpRankingEvolution: [this.data.pvpRankingEvolution ?? 0],
    pvpRankingLeague: [this.data.pvpRankingLeague],
    pvpRankingMinCp: [this.data.pvpRankingMinCp],
    pvpRankingWorst: [this.data.pvpRankingWorst],
    size: [this.data.size],
    sta: [this.data.sta],
    template: [this.data.template ?? ''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  pokemonName = this.data.pokemonId === 0 ? this.i18n.instant('POKEMON.ALL_POKEMON') : this.masterData.getPokemonName(this.data.pokemonId);

  readonly pvpCaps = computed(() => this.poracleConfig.serverConfig().pvpCaps);

  saving = signal(false);

  readonly showCapPicker = computed(() => this.pvpCaps().length > 1);

  getPokemonImage(): string {
    return this.iconService.getPokemonUrl(this.data.pokemonId, this.data.form);
  }

  /** True when the alarm is confined to areas, which the areas-or-distance control cannot express. */
  isAreaScoped(): boolean {
    return (this.data.overrideAreas?.length ?? 0) > 0;
  }

  ngOnInit(): void {
    this.poracleConfig.load().subscribe();
  }

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') {
      this.form.controls.distanceKm.setValue(0);
    } else {
      if (!this.form.controls.distanceKm.value) {
        this.form.controls.distanceKm.setValue(1);
      }
    }
  }

  onImageError(event: Event): void {
    const img = event.target as HTMLImageElement;
    const fallback = this.iconService.getPokemonFallbackUrl(this.data.pokemonId);
    if (!img.src.endsWith(`/${this.data.pokemonId}.png`)) {
      img.src = fallback;
    } else {
      img.style.display = 'none';
    }
  }

  save(): void {
    this.saving.set(true);
    const values = this.form.getRawValue();

    // An alarm confined to areas has no radius, so the two-way control above cannot describe it and
    // must not overwrite it. Leaving the fields out entirely means the API keeps what is stored: null
    // reads as "not stated" on the write path, which is exactly what this dialog means. See #730.
    const keepsAreas = (this.data.overrideAreas?.length ?? 0) > 0;
    const distanceMeters = keepsAreas
      ? this.data.distance
      : values.distanceMode === 'areas'
        ? 0
        : Math.round((values.distanceKm ?? 1) * 1000);

    const update: MonsterUpdate = {
      atk: values.atk ?? 0,
      clean: preserve(this.data.clean, AUTO_DELETE, values.clean ? 1 : 0),
      def: values.def ?? 0,
      distance: distanceMeters,
      form: values.form ?? 0,
      gender: values.gender ?? 0,
      maxAtk: values.maxAtk ?? 15,
      maxCp: values.maxCp ?? 9000,
      maxDef: values.maxDef ?? 15,
      maxIv: values.maxIv ?? 100,
      maxLevel: values.maxLevel ?? 55,
      maxSize: values.maxSize ?? 5,
      maxSta: values.maxSta ?? 15,
      maxWeight: values.maxWeight ?? 9000000,
      minCp: values.minCp ?? 0,
      minIv: values.minIv ?? 0,
      minLevel: values.minLevel ?? 0,
      minWeight: values.minWeight ?? 0,
      pvpRankingBest: values.pvpRankingLeague ? (values.pvpRankingBest ?? 1) : 0,
      pvpRankingCap: values.pvpRankingLeague ? (values.pvpRankingCap ?? 0) : 0,
      pvpRankingEvolution: values.pvpRankingLeague ? (values.pvpRankingEvolution ?? 0) : 0,
      pvpRankingLeague: values.pvpRankingLeague ?? 0,
      pvpRankingMinCp: values.pvpRankingLeague ? (values.pvpRankingMinCp ?? 0) : 0,
      pvpRankingWorst: values.pvpRankingLeague ? (values.pvpRankingWorst ?? 100) : 4096,
      size: values.size ?? -1,
      sta: values.sta ?? 0,
      template: values.template || '',
    };

    this.monsterService.update(this.data.uid, update).subscribe({
      // The server explains exactly what is wrong with a filter it refuses -- which min/max pair is
      // inverted, say. That message never reached anyone: this handler showed a fixed string, so a
      // transposed pair produced "failed" with no clue which field to fix. See #496.
      error: (err: { error?: { error?: string } }) => {
        const message = err?.error?.error ?? this.i18n.instant('POKEMON.SNACK_FAILED_UPDATE');
        this.snackBar.open(message, this.i18n.instant('COMMON.OK'), { duration: 6000 });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open(this.i18n.instant('POKEMON.SNACK_UPDATED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }
}
