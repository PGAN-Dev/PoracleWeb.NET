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
import { TranslateModule } from '@ngx-translate/core';

import { Quest, QuestUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
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
    MatTabsModule,
    MatSnackBarModule,
    TranslateModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-quest-edit-dialog',
  standalone: true,
  styleUrl: './quest-edit-dialog.component.scss',
  templateUrl: './quest-edit-dialog.component.html',
})
export class QuestEditDialogComponent {
  private static readonly FALLBACK =
    "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%23999'%3E%3Cpath d='M11 18h2v-2h-2v2zm1-16C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm0-14c-2.21 0-4 1.79-4 4h2c0-1.1.9-2 2-2s2 .9 2 2c0 2-3 1.75-3 5h2c0-2.25 3-2.5 3-5 0-2.21-1.79-4-4-4z'/%3E%3C/svg%3E";

  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
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

  private get questPokemonId(): number {
    return this.data.pokemonId > 0 ? this.data.pokemonId : this.data.reward;
  }

  getImage(): string {
    const pid = this.questPokemonId;
    if ((this.data.rewardType === 7 || this.data.rewardType === 12 || this.data.rewardType === 4) && pid > 0) {
      return this.iconService.getPokemonUrl(pid);
    }
    if (this.data.rewardType === 7 && pid === 0) {
      return '';
    }
    if (this.data.rewardType === 2 && this.data.reward > 0) {
      return this.iconService.getItemUrl(this.data.reward);
    }
    return this.iconService.getRewardUrl('quest', this.data.rewardType);
  }

  getRewardTypeLabel(): string {
    switch (this.data.rewardType) {
      case 7:
        return this.i18n.instant('QUESTS.REWARD_POKEMON_ENCOUNTER');
      case 2:
        return this.i18n.instant('QUESTS.REWARD_ITEM');
      case 3:
        return this.i18n.instant('QUESTS.STARDUST');
      case 12:
        return this.i18n.instant('QUESTS.REWARD_MEGA_ENERGY');
      case 4:
        return this.i18n.instant('QUESTS.REWARD_CANDY');
      default:
        return this.i18n.instant('QUESTS.REWARD_TYPE_PREFIX', { type: this.data.rewardType });
    }
  }

  getTitle(): string {
    const pid = this.questPokemonId;
    if (this.data.rewardType === 7 && pid > 0) {
      return this.masterData.getPokemonName(pid);
    }
    if (this.data.rewardType === 7 && pid === 0) {
      return this.i18n.instant('QUESTS.ANY_POKEMON_ENCOUNTER');
    }
    if (this.data.rewardType === 12 && pid > 0) {
      return this.i18n.instant('QUESTS.MEGA_ENERGY_SUFFIX', { name: this.masterData.getPokemonName(pid) });
    }
    if (this.data.rewardType === 4 && pid > 0) {
      return this.i18n.instant('QUESTS.CANDY_SUFFIX', { name: this.masterData.getPokemonName(pid) });
    }
    if (this.data.rewardType === 3) {
      return this.data.reward > 0
        ? this.i18n.instant('QUESTS.STARDUST_AMOUNT', { amount: this.data.reward })
        : this.i18n.instant('QUESTS.STARDUST');
    }
    if (this.data.rewardType === 2) {
      return this.masterData.getItemName(this.data.reward);
    }
    return this.getRewardTypeLabel();
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

  onImgFallback(event: Event): void {
    (event.target as HTMLImageElement).src = QuestEditDialogComponent.FALLBACK;
  }

  save(): void {
    this.saving.set(true);
    const values = this.form.getRawValue();
    const distanceMeters = values.distanceMode === 'areas' ? 0 : Math.round((values.distanceKm ?? 1) * 1000);

    const update: QuestUpdate = {
      clean: values.clean ? 1 : 0,
      distance: distanceMeters,
      ping: values.ping || null,
      pokemonId: this.data.pokemonId,
      reward: this.data.reward,
      rewardType: this.data.rewardType,
      shiny: this.data.shiny,
      template: values.template || null,
    };

    this.questService.update(this.data.uid, update).subscribe({
      error: () => {
        this.snackBar.open(this.i18n.instant('QUESTS.SNACK_FAILED_UPDATE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open(this.i18n.instant('QUESTS.SNACK_UPDATED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }
}
