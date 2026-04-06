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

import { MaxBattle, MaxBattleUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MaxBattleService } from '../../core/services/max-battle.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

export interface MaxBattleEditDialogData {
  item: MaxBattle;
}

/** PoracleNG max battle levels: 1-5 = Dynamax, 7 = Gigantamax, 8 = Legendary Gigantamax */
interface MaxBattleLevelOption {
  gmax: boolean;
  label: string;
  value: number;
}

const LEVEL_OPTIONS: MaxBattleLevelOption[] = [
  { gmax: false, label: '1 Star', value: 1 },
  { gmax: false, label: '2 Star', value: 2 },
  { gmax: false, label: '3 Star', value: 3 },
  { gmax: false, label: '4 Star', value: 4 },
  { gmax: false, label: '5 Star (Legendary)', value: 5 },
  { gmax: true, label: 'Gigantamax', value: 7 },
  { gmax: true, label: 'Legendary Gigantamax', value: 8 },
];

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
    MatRadioModule,
    MatTabsModule,
    MatSnackBarModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-max-battle-edit-dialog',
  standalone: true,
  styleUrl: './max-battle-edit-dialog.component.scss',
  templateUrl: './max-battle-edit-dialog.component.html',
})
export class MaxBattleEditDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly maxBattleService = inject(MaxBattleService);
  private readonly snackBar = inject(MatSnackBar);

  readonly data = inject<MaxBattleEditDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<MaxBattleEditDialogComponent>);

  form = this.fb.group({
    clean: [this.data.item.clean === 1],
    distanceKm: [this.data.item.distance > 0 ? this.data.item.distance / 1000 : 1],
    distanceMode: [this.data.item.distance === 0 ? 'areas' : ('distance' as 'areas' | 'distance')],
    gmax: [this.data.item.gmax === 1],
    level: [this.data.item.level],
    ping: [this.data.item.ping ?? ''],
    template: [this.data.item.template ?? ''],
  });

  readonly isLevelBased = this.data.item.pokemonId === 9000;
  readonly isWebhook = inject(AuthService).isImpersonating();
  readonly levelOptions = LEVEL_OPTIONS;

  saving = signal(false);

  getImage(): string {
    const item = this.data.item;
    if (item.pokemonId && item.pokemonId !== 9000) {
      return this.iconService.getPokemonUrl(item.pokemonId);
    }
    return '';
  }

  getLevelLabel(): string {
    const level = this.data.item.level;
    if (level === 9000) return 'Any Level';
    const opt = LEVEL_OPTIONS.find(l => l.value === level);
    return opt ? opt.label : `Level ${level}`;
  }

  getTitle(): string {
    const item = this.data.item;
    if (item.pokemonId && item.pokemonId !== 9000) {
      return this.masterData.getPokemonName(item.pokemonId);
    }
    return 'Any Pokemon';
  }

  isGmax(): boolean {
    return this.data.item.gmax === 1 || this.data.item.level === 7 || this.data.item.level === 8;
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
    (event.target as HTMLImageElement).style.display = 'none';
  }

  save(): void {
    this.saving.set(true);
    const values = this.form.getRawValue();
    const distanceMeters = values.distanceMode === 'areas' ? 0 : Math.round((values.distanceKm ?? 1) * 1000);

    const item = this.data.item;
    const levelVal = this.isLevelBased ? (values.level ?? item.level) : 9000;
    const levelDef = LEVEL_OPTIONS.find(l => l.value === levelVal);
    // For level-based alarms, gmax is derived from the level (7/8 = gmax).
    // For pokemon-based alarms, gmax is an independent toggle (e.g. "only Gigantamax Charizard").
    const gmaxVal = this.isLevelBased ? (levelDef?.gmax ? 1 : 0) : values.gmax ? 1 : 0;

    const update: MaxBattleUpdate = {
      clean: values.clean ? 1 : 0,
      distance: distanceMeters,
      evolution: 9000,
      form: item.form,
      gmax: gmaxVal,
      level: levelVal,
      move: 9000,
      ping: values.ping || '',
      pokemonId: item.pokemonId,
      stationId: null,
      template: values.template || '',
    };
    this.maxBattleService.update(this.data.item.uid, update).subscribe({
      error: () => {
        this.snackBar.open('Failed to update alarm', 'OK', { duration: 3000 });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open('Max Battle alarm updated', 'OK', { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }
}
