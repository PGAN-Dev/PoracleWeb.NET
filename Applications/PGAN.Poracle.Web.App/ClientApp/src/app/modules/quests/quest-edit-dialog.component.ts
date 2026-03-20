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

import { Quest, QuestUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { QuestService } from '../../core/services/quest.service';
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
    MatSnackBarModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-quest-edit-dialog',
  standalone: true,
  styleUrl: './quest-edit-dialog.component.scss',
  templateUrl: './quest-edit-dialog.component.html',
})
export class QuestEditDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly questService = inject(QuestService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Quest>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<QuestEditDialogComponent>);
  form = this.fb.group({
    clean: [this.data.clean === 1],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    distanceMode: [this.data.distance === 0 ? 'areas' : ('distance' as 'areas' | 'distance')],
    ping: [this.data.ping ?? ''],
    template: [this.data.template ?? ''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  saving = signal(false);

  getImage(): string {
    if (this.data.rewardType === 7 && this.data.pokemonId > 0) {
      return this.iconService.getPokemonUrl(this.data.pokemonId);
    }
    return this.iconService.getRewardUrl('quest', this.data.rewardType);
  }

  getRewardTypeLabel(): string {
    switch (this.data.rewardType) {
      case 7:
        return 'Pokemon Encounter';
      case 2:
        return 'Item';
      case 12:
        return 'Mega Energy';
      case 4:
        return 'Candy';
      default:
        return `Type ${this.data.rewardType}`;
    }
  }

  getTitle(): string {
    if (this.data.rewardType === 7 && this.data.pokemonId > 0) {
      return this.masterData.getPokemonName(this.data.pokemonId);
    }
    if (this.data.rewardType === 12 && this.data.pokemonId > 0) {
      return `${this.masterData.getPokemonName(this.data.pokemonId)} Mega Energy`;
    }
    if (this.data.rewardType === 4 && this.data.pokemonId > 0) {
      return `${this.masterData.getPokemonName(this.data.pokemonId)} Candy`;
    }
    return `Quest Reward #${this.data.reward}`;
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

    const update: QuestUpdate = {
      clean: values.clean ? 1 : 0,
      distance: distanceMeters,
      ping: values.ping || null,
      template: values.template || null,
    };

    this.questService.update(this.data.uid, update).subscribe({
      error: () => {
        this.snackBar.open('Failed to update alarm', 'OK', { duration: 3000 });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open('Quest alarm updated', 'OK', { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }
}
