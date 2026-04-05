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
import { forkJoin } from 'rxjs';

import { MaxBattleCreate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { MaxBattleService } from '../../core/services/max-battle.service';
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
    MatCheckboxModule,
    MatRadioModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
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
  private readonly fb = inject(FormBuilder);
  private readonly maxBattleService = inject(MaxBattleService);
  private readonly snackBar = inject(MatSnackBar);

  commonForm = this.fb.group({
    clean: [false],
    distanceKm: [1],
    distanceMode: ['areas' as 'areas' | 'distance'],
    evolution: [9000],
    form: [0],
    gmax: [false],
    move: [9000],
    ping: [''],
    template: [''],
  });

  readonly dialogRef = inject(MatDialogRef<MaxBattleAddDialogComponent>);

  readonly isWebhook = inject(AuthService).isImpersonating();
  readonly levels = [1, 2, 3, 4, 5, 6];
  pokemonForm = this.fb.group({
    level: [1],
  });

  saving = signal(false);
  selectedLevels = signal<number[]>([]);
  selectedPokemonIds = signal<number[]>([]);

  tabIndex = 0;

  canSave(): boolean {
    if (this.tabIndex === 0) {
      return this.selectedLevels().length > 0;
    }
    return this.selectedPokemonIds().length > 0;
  }

  onDistanceModeChange(): void {
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
    const distanceMeters = common.distanceMode === 'areas' ? 0 : Math.round((common.distanceKm ?? 1) * 1000);

    const creates: ReturnType<typeof this.maxBattleService.create>[] = [];

    if (this.tabIndex === 0) {
      // By Level — one alarm per selected level, pokemonId = 9000 (any)
      for (const level of this.selectedLevels()) {
        const maxBattle: MaxBattleCreate = {
          clean: common.clean ? 1 : 0,
          distance: distanceMeters,
          evolution: common.evolution ?? 9000,
          form: common.form ?? 0,
          gmax: common.gmax ? 1 : 0,
          level,
          move: common.move ?? 9000,
          ping: common.ping || '',
          pokemonId: 9000,
          stationId: null,
          template: common.template || '',
        };
        creates.push(this.maxBattleService.create(maxBattle));
      }
    } else {
      // By Pokemon — one alarm per selected Pokemon
      const pokemonLevel = this.pokemonForm.controls.level.value ?? 1;
      for (const pokemonId of this.selectedPokemonIds()) {
        const maxBattle: MaxBattleCreate = {
          clean: common.clean ? 1 : 0,
          distance: distanceMeters,
          evolution: common.evolution ?? 9000,
          form: common.form ?? 0,
          gmax: common.gmax ? 1 : 0,
          level: pokemonLevel,
          move: common.move ?? 9000,
          ping: common.ping || '',
          pokemonId,
          stationId: null,
          template: common.template || '',
        };
        creates.push(this.maxBattleService.create(maxBattle));
      }
    }

    forkJoin(creates).subscribe({
      error: () => {
        this.snackBar.open('Failed to create alarm(s)', 'OK', { duration: 3000 });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open(`${creates.length} alarm(s) created`, 'OK', { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }

  toggleLevel(level: number): void {
    this.selectedLevels.update(levels => (levels.includes(level) ? levels.filter(l => l !== level) : [...levels, level]));
  }
}
