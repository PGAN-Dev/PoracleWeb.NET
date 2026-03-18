import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';

import { Nest, NestUpdate } from '../../core/models';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { NestService } from '../../core/services/nest.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

@Component({
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSlideToggleModule,
    MatIconModule,
    MatRadioModule,
    MatTabsModule,
    MatSnackBarModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-nest-edit-dialog',
  standalone: true,
  styleUrl: './nest-edit-dialog.component.scss',
  templateUrl: './nest-edit-dialog.component.html',
})
export class NestEditDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly nestService = inject(NestService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Nest>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<NestEditDialogComponent>);
  form = this.fb.group({
    clean: [this.data.clean === 1],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    distanceMode: [this.data.distance === 0 ? 'areas' : ('distance' as 'areas' | 'distance')],
    minSpawnAvg: [this.data.minSpawnAvg],
    ping: [this.data.ping ?? ''],
    template: [this.data.template ?? ''],
  });

  pokemonName = this.masterData.getPokemonName(this.data.pokemonId);
  saving = signal(false);
  getPokemonImage(): string {
    return this.iconService.getPokemonUrl(this.data.pokemonId);
  }

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0);
    else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1);
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  save(): void {
    this.saving.set(true);
    const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    this.nestService
      .update(this.data.uid, {
        clean: v.clean ? 1 : 0,
        distance: dist,
        minSpawnAvg: v.minSpawnAvg ?? 0,
        ping: v.ping || null,
        template: v.template || null,
      } as NestUpdate)
      .subscribe({
        error: () => {
          this.snackBar.open('Failed to update alarm', 'OK', { duration: 3000 });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open('Nest alarm updated', 'OK', { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
