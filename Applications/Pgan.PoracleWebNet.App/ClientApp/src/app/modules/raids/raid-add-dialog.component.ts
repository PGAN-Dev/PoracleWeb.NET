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

import { RaidCreate, EggCreate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { EggService } from '../../core/services/egg.service';
import { RaidService } from '../../core/services/raid.service';
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
  selector: 'app-raid-add-dialog',
  standalone: true,
  styleUrl: './raid-add-dialog.component.scss',
  templateUrl: './raid-add-dialog.component.html',
})
export class RaidAddDialogComponent {
  private readonly eggService = inject(EggService);
  private readonly fb = inject(FormBuilder);
  private readonly raidService = inject(RaidService);
  private readonly snackBar = inject(MatSnackBar);
  bossForm = this.fb.group({
    level: [0],
  });

  commonForm = this.fb.group({
    clean: [false],
    distanceKm: [1],
    distanceMode: ['areas' as 'areas' | 'distance'],
    ping: [''],
    team: [0],
    template: [''],
  });

  readonly dialogRef = inject(MatDialogRef<RaidAddDialogComponent>);

  readonly isWebhook = inject(AuthService).isImpersonating();
  levels = [1, 2, 3, 4, 5, 6];
  saving = signal(false);
  selectedEggLevels = signal<number[]>([]);
  selectedPokemonIds = signal<number[]>([]);

  selectedRaidLevels = signal<number[]>([]);

  tabIndex = 0;

  canSave(): boolean {
    if (this.tabIndex === 0) {
      return this.selectedRaidLevels().length > 0 || this.selectedEggLevels().length > 0;
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

    const creates: ReturnType<typeof this.raidService.create | typeof this.eggService.create>[] = [];

    if (this.tabIndex === 0) {
      // By Level
      for (const level of this.selectedRaidLevels()) {
        const raid: RaidCreate = {
          clean: common.clean ? 1 : 0,
          distance: distanceMeters,
          exclusive: 0,
          form: 0,
          gymId: null,
          level,
          move: 0,
          ping: common.ping || null,
          pokemonId: 9000,
          team: common.team ?? 0,
          template: common.template || null,
        };
        creates.push(this.raidService.create(raid));
      }
      for (const level of this.selectedEggLevels()) {
        const egg: EggCreate = {
          clean: common.clean ? 1 : 0,
          distance: distanceMeters,
          exclusive: 0,
          level,
          ping: common.ping || null,
          team: common.team ?? 0,
          template: common.template || null,
        };
        creates.push(this.eggService.create(egg));
      }
    } else {
      // By Boss
      const bossLevel = this.bossForm.controls.level.value ?? 0;
      for (const pokemonId of this.selectedPokemonIds()) {
        const raid: RaidCreate = {
          clean: common.clean ? 1 : 0,
          distance: distanceMeters,
          exclusive: 0,
          form: 0,
          gymId: null,
          level: bossLevel,
          move: 0,
          ping: common.ping || null,
          pokemonId,
          team: common.team ?? 0,
          template: common.template || null,
        };
        creates.push(this.raidService.create(raid));
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

  toggleEggLevel(level: number): void {
    this.selectedEggLevels.update(levels => (levels.includes(level) ? levels.filter(l => l !== level) : [...levels, level]));
  }

  toggleRaidLevel(level: number): void {
    this.selectedRaidLevels.update(levels => (levels.includes(level) ? levels.filter(l => l !== level) : [...levels, level]));
  }
}
