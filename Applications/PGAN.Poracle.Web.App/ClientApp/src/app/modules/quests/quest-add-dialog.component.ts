import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { forkJoin } from 'rxjs';

import { AuthService } from '../../core/services/auth.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { QuestService } from '../../core/services/quest.service';
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
    MatRadioModule,
    MatSnackBarModule,
    PokemonSelectorComponent,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-quest-add-dialog',
  standalone: true,
  styleUrl: './quest-add-dialog.component.scss',
  templateUrl: './quest-add-dialog.component.html',
})
export class QuestAddDialogComponent {
  private static readonly FALLBACK_ICON =
    "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%23999'%3E%3Cpath d='M11 18h2v-2h-2v2zm1-16C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm0-14c-2.21 0-4 1.79-4 4h2c0-1.1.9-2 2-2s2 .9 2 2c0 2-3 1.75-3 5h2c0-2.25 3-2.5 3-5 0-2.21-1.79-4-4-4z'/%3E%3C/svg%3E";

  private readonly fb = inject(FormBuilder);
  private readonly masterData = inject(MasterDataService);
  private readonly questService = inject(QuestService);

  private readonly snackBar = inject(MatSnackBar);

  commonForm = this.fb.group({
    clean: [false],
    distanceKm: [1],
    distanceMode: ['areas' as 'areas' | 'distance'],
    ping: [''],
    template: [''],
  });

  readonly dialogRef = inject(MatDialogRef<QuestAddDialogComponent>);
  readonly iconService = inject(IconService);

  readonly isWebhook = inject(AuthService).isImpersonating();

  itemForm = this.fb.group({
    reward: [0],
  });

  /** Quest-relevant items (balls, berries, potions, revives, TMs, etc.) */
  readonly questItems = signal<{ id: number; name: string }[]>([]);
  saving = signal(false);
  selectedCandyPokemonIds = signal<number[]>([]);

  selectedMegaPokemonIds = signal<number[]>([]);

  selectedPokemonIds = signal<number[]>([]);

  tabIndex = 0;

  constructor() {
    this.masterData.loadData().subscribe(() => {
      // Filter to quest-relevant items (exclude tickets, passes, storage, etc.)
      const excluded = new Set([
        800, 801, 802, 803, 901, 902, 903, 904, 905, 1001, 1002, 1003, 1401, 1402, 1405, 1406, 1407, 1408, 1410, 1411,
      ]);
      this.questItems.set(
        this.masterData.getAllItems().filter(i => !excluded.has(i.id) && !i.name.includes('Ticket') && !i.name.includes('Pass Points')),
      );
    });
  }

  canSave(): boolean {
    switch (this.tabIndex) {
      case 0:
        return this.selectedPokemonIds().length > 0;
      case 1:
        return true;
      case 2:
        return this.selectedMegaPokemonIds().length > 0;
      case 3:
        return this.selectedCandyPokemonIds().length > 0;
      default:
        return false;
    }
  }

  onCandyPokemonSelected(ids: number[]): void {
    this.selectedCandyPokemonIds.set(ids);
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

  onMegaPokemonSelected(ids: number[]): void {
    this.selectedMegaPokemonIds.set(ids);
  }

  onOptionIconError(event: Event): void {
    const img = event.target as HTMLImageElement;
    img.src = QuestAddDialogComponent.FALLBACK_ICON;
  }

  onPokemonSelected(ids: number[]): void {
    this.selectedPokemonIds.set(ids);
  }

  save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);
    const common = this.commonForm.getRawValue();
    const distanceMeters = common.distanceMode === 'areas' ? 0 : Math.round((common.distanceKm ?? 1) * 1000);

    const creates: ReturnType<typeof this.questService.create>[] = [];

    switch (this.tabIndex) {
      case 0:
        for (const pokemonId of this.selectedPokemonIds()) {
          creates.push(
            this.questService.create({
              clean: common.clean ? 1 : 0,
              distance: distanceMeters,
              ping: common.ping || null,
              pokemonId,
              profileNo: 1,
              reward: pokemonId,
              rewardType: 7,
              shiny: 0,
              template: common.template || null,
            }),
          );
        }
        break;
      case 1:
        creates.push(
          this.questService.create({
            clean: common.clean ? 1 : 0,
            distance: distanceMeters,
            ping: common.ping || null,
            pokemonId: 0,
            profileNo: 1,
            reward: this.itemForm.controls.reward.value ?? 0,
            rewardType: 2,
            shiny: 0,
            template: common.template || null,
          }),
        );
        break;
      case 2:
        for (const pokemonId of this.selectedMegaPokemonIds()) {
          creates.push(
            this.questService.create({
              clean: common.clean ? 1 : 0,
              distance: distanceMeters,
              ping: common.ping || null,
              pokemonId,
              profileNo: 1,
              reward: pokemonId,
              rewardType: 12,
              shiny: 0,
              template: common.template || null,
            }),
          );
        }
        break;
      case 3:
        for (const pokemonId of this.selectedCandyPokemonIds()) {
          creates.push(
            this.questService.create({
              clean: common.clean ? 1 : 0,
              distance: distanceMeters,
              ping: common.ping || null,
              pokemonId,
              profileNo: 1,
              reward: pokemonId,
              rewardType: 4,
              shiny: 0,
              template: common.template || null,
            }),
          );
        }
        break;
    }

    forkJoin(creates).subscribe({
      error: () => {
        this.snackBar.open('Failed to create alarm(s)', 'OK', { duration: 3000 });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open(`${creates.length} quest alarm(s) created`, 'OK', { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }
}
