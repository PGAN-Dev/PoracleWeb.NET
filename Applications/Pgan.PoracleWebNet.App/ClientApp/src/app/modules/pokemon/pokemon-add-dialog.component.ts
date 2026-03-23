import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
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
import { forkJoin } from 'rxjs';

import { MonsterCreate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MonsterService } from '../../core/services/monster.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { PokemonSelectorComponent } from '../../shared/components/pokemon-selector/pokemon-selector.component';
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
    MatTabsModule,
    MatExpansionModule,
    MatRadioModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    PokemonSelectorComponent,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-pokemon-add-dialog',
  standalone: true,
  styleUrl: './pokemon-add-dialog.component.scss',
  templateUrl: './pokemon-add-dialog.component.html',
})
export class PokemonAddDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly masterData = inject(MasterDataService);
  private readonly monsterService = inject(MonsterService);
  private readonly snackBar = inject(MatSnackBar);
  selectedPokemonIds = signal<number[]>([]);
  readonly availableForms = computed(() => {
    const ids = this.selectedPokemonIds();
    if (ids.length !== 1 || ids[0] === 0) return [];
    return this.masterData.getFormsForPokemon(ids[0]);
  });

  readonly dialogRef = inject(MatDialogRef<PokemonAddDialogComponent>);

  filtersForm = this.fb.group({
    atk: [0, [Validators.min(0), Validators.max(15)]],
    def: [0, [Validators.min(0), Validators.max(15)]],
    form: [0],
    gender: [0],
    maxAtk: [15, [Validators.min(0), Validators.max(15)]],
    maxCp: [9000, [Validators.min(0), Validators.max(9000)]],
    maxDef: [15, [Validators.min(0), Validators.max(15)]],
    maxIv: [100, [Validators.min(0), Validators.max(100)]],
    maxLevel: [40, [Validators.min(0), Validators.max(50)]],
    maxSta: [15, [Validators.min(0), Validators.max(15)]],
    maxWeight: [9000000],
    minCp: [0, [Validators.min(0), Validators.max(9000)]],
    minIv: [0, [Validators.min(0), Validators.max(100)]],
    minLevel: [0, [Validators.min(0), Validators.max(50)]],
    minWeight: [0],
    sta: [0, [Validators.min(0), Validators.max(15)]],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  notifForm = this.fb.group({
    clean: [false],
    distanceKm: [1],
    distanceMode: ['areas' as 'areas' | 'distance'],
    ping: [''],
    template: [''],
  });

  pvpForm = this.fb.group({
    pvpRankingBest: [1],
    pvpRankingLeague: [0],
    pvpRankingMinCp: [0],
    pvpRankingWorst: [100],
  });

  saving = signal(false);

  isFormValid(): boolean {
    return this.selectedPokemonIds().length > 0 && this.filtersForm.valid && this.notifForm.valid;
  }

  onDistanceModeChange(): void {
    if (this.notifForm.controls.distanceMode.value === 'areas') {
      this.notifForm.controls.distanceKm.setValue(0);
    } else if (!this.notifForm.controls.distanceKm.value) {
      this.notifForm.controls.distanceKm.setValue(1);
    }
  }

  onPokemonSelected(ids: number[]): void {
    this.selectedPokemonIds.set(ids);
  }

  save(): void {
    if (!this.isFormValid()) return;
    this.saving.set(true);

    const filters = this.filtersForm.getRawValue();
    const pvp = this.pvpForm.getRawValue();
    const notif = this.notifForm.getRawValue();
    const distanceMeters = notif.distanceMode === 'areas' ? 0 : Math.round((notif.distanceKm ?? 1) * 1000);

    const creates = this.selectedPokemonIds().map(pokemonId => {
      const monster: MonsterCreate = {
        atk: filters.atk ?? 0,
        clean: notif.clean ? 1 : 0,
        def: filters.def ?? 0,
        distance: distanceMeters,
        form: filters.form ?? 0,
        gender: filters.gender ?? 0,
        maxAtk: filters.maxAtk ?? 15,
        maxCp: filters.maxCp ?? 9000,
        maxDef: filters.maxDef ?? 15,
        maxIv: filters.maxIv ?? 100,
        maxLevel: filters.maxLevel ?? 40,
        maxSta: filters.maxSta ?? 15,
        maxWeight: filters.maxWeight ?? 9000000,
        minCp: filters.minCp ?? 0,
        minIv: filters.minIv ?? 0,
        minLevel: filters.minLevel ?? 0,
        minWeight: filters.minWeight ?? 0,
        ping: notif.ping || null,
        pokemonId,
        pvpRankingBest: pvp.pvpRankingBest ?? 1,
        pvpRankingLeague: pvp.pvpRankingLeague ?? 0,
        pvpRankingMinCp: pvp.pvpRankingMinCp ?? 0,
        pvpRankingWorst: pvp.pvpRankingWorst ?? 100,
        sta: filters.sta ?? 0,
        template: notif.template || null,
      };
      return this.monsterService.create(monster);
    });

    forkJoin(creates).subscribe({
      error: () => {
        this.snackBar.open('Failed to create alarm(s)', 'OK', { duration: 3000 });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open(`${creates.length} Pokemon alarm(s) created`, 'OK', { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }
}
