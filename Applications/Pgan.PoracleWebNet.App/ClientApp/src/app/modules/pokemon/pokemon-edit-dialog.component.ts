import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
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

import { Monster, MonsterUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MonsterService } from '../../core/services/monster.service';
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
    MatExpansionModule,
    MatRadioModule,
    MatTabsModule,
    MatSnackBarModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-pokemon-edit-dialog',
  standalone: true,
  styleUrl: './pokemon-edit-dialog.component.scss',
  templateUrl: './pokemon-edit-dialog.component.html',
})
export class PokemonEditDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly monsterService = inject(MonsterService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Monster>(MAT_DIALOG_DATA);
  readonly availableForms = computed(() => {
    return this.masterData.getFormsForPokemon(this.data.pokemonId);
  });

  readonly dialogRef = inject(MatDialogRef<PokemonEditDialogComponent>);

  form = this.fb.group({
    atk: [this.data.atk],
    clean: [this.data.clean === 1],
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
    maxSta: [this.data.maxSta],
    maxWeight: [this.data.maxWeight],
    minCp: [this.data.minCp],
    minIv: [this.data.minIv],
    minLevel: [this.data.minLevel],
    minWeight: [this.data.minWeight],
    ping: [this.data.ping ?? ''],
    pvpRankingBest: [this.data.pvpRankingBest],
    pvpRankingLeague: [this.data.pvpRankingLeague],
    pvpRankingMinCp: [this.data.pvpRankingMinCp],
    pvpRankingWorst: [this.data.pvpRankingWorst],
    sta: [this.data.sta],
    template: [this.data.template ?? ''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  pokemonName = this.data.pokemonId === 0 ? 'All Pokemon' : this.masterData.getPokemonName(this.data.pokemonId);

  saving = signal(false);

  getPokemonImage(): string {
    return this.iconService.getPokemonUrl(this.data.pokemonId, this.data.form);
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

    const distanceMeters = values.distanceMode === 'areas' ? 0 : Math.round((values.distanceKm ?? 1) * 1000);

    const update: MonsterUpdate = {
      atk: values.atk ?? 0,
      clean: values.clean ? 1 : 0,
      def: values.def ?? 0,
      distance: distanceMeters,
      form: values.form ?? 0,
      gender: values.gender ?? 0,
      maxAtk: values.maxAtk ?? 15,
      maxCp: values.maxCp ?? 9000,
      maxDef: values.maxDef ?? 15,
      maxIv: values.maxIv ?? 100,
      maxLevel: values.maxLevel ?? 40,
      maxSta: values.maxSta ?? 15,
      maxWeight: values.maxWeight ?? 9000000,
      minCp: values.minCp ?? 0,
      minIv: values.minIv ?? 0,
      minLevel: values.minLevel ?? 0,
      minWeight: values.minWeight ?? 0,
      ping: values.ping || null,
      pvpRankingBest: values.pvpRankingBest ?? 1,
      pvpRankingLeague: values.pvpRankingLeague ?? 0,
      pvpRankingMinCp: values.pvpRankingMinCp ?? 0,
      pvpRankingWorst: values.pvpRankingWorst ?? 100,
      sta: values.sta ?? 0,
      template: values.template || null,
    };

    this.monsterService.update(this.data.uid, update).subscribe({
      error: () => {
        this.snackBar.open('Failed to update alarm', 'OK', { duration: 3000 });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open('Pokemon alarm updated', 'OK', { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }
}
