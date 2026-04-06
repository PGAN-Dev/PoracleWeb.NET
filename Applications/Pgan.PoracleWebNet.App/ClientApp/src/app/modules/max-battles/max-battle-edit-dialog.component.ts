import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
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
    evolution: [this.data.item.evolution],
    formVal: [this.data.item.form],
    gmax: [this.data.item.gmax === 1],
    level: [this.data.item.level],
    move: [this.data.item.move],
    ping: [this.data.item.ping ?? ''],
    stationId: [this.data.item.stationId ?? ''],
    template: [this.data.item.template ?? ''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();
  readonly levels = [1, 2, 3, 4, 5, 6];

  saving = signal(false);

  getImage(): string {
    const item = this.data.item;
    if (item.pokemonId && item.pokemonId !== 9000) {
      return this.iconService.getPokemonUrl(item.pokemonId);
    }
    return '';
  }

  getTitle(): string {
    const item = this.data.item;
    if (item.pokemonId && item.pokemonId !== 9000) {
      return this.masterData.getPokemonName(item.pokemonId);
    }
    return 'Any Pokemon';
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
    const update: MaxBattleUpdate = {
      clean: values.clean ? 1 : 0,
      distance: distanceMeters,
      evolution: values.evolution ?? 9000,
      form: values.formVal ?? 0,
      gmax: values.gmax ? 1 : 0,
      level: values.level ?? item.level,
      move: values.move ?? 9000,
      ping: values.ping || '',
      pokemonId: item.pokemonId,
      stationId: values.stationId || null,
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
