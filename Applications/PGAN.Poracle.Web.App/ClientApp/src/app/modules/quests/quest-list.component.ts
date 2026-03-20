import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { QuestAddDialogComponent } from './quest-add-dialog.component';
import { QuestEditDialogComponent } from './quest-edit-dialog.component';
import { Quest } from '../../core/models';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { QuestService } from '../../core/services/quest.service';
import { AlarmInfoComponent } from '../../shared/components/alarm-info/alarm-info.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    AlarmInfoComponent,
  ],
  selector: 'app-quest-list',
  standalone: true,
  styleUrl: './quest-list.component.scss',
  templateUrl: './quest-list.component.html',
})
export class QuestListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly questService = inject(QuestService);
  private readonly snackBar = inject(MatSnackBar);

  readonly loading = signal(true);
  readonly quests = signal<Quest[]>([]);
  readonly skeletonCards = Array.from({ length: 6 });

  deleteAll(): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete All',
        message: 'Are you sure you want to delete ALL quest alarms? This action cannot be undone.',
        title: 'Delete All Quest Alarms',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.questService.deleteAll().subscribe({
          error: () => {
            this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 });
          },
          next: () => {
            this.snackBar.open('All quest alarms deleted', 'OK', { duration: 3000 });
            this.loadQuests();
          },
        });
      }
    });
  }

  deleteQuest(quest: Quest): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete',
        message: `Are you sure you want to delete the alarm for ${this.getQuestTitle(quest)}?`,
        title: 'Delete Quest Alarm',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.questService.delete(quest.uid).subscribe({
          error: () => {
            this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 });
          },
          next: () => {
            this.snackBar.open('Quest alarm deleted', 'OK', { duration: 3000 });
            this.loadQuests();
          },
        });
      }
    });
  }

  editQuest(quest: Quest): void {
    const ref = this.dialog.open(QuestEditDialogComponent, {
      width: '600px',
      data: quest,
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadQuests();
    });
  }

  formatDistance(meters: number): string {
    if (meters >= 1000) {
      return `${(meters / 1000).toFixed(1)} km`;
    }
    return `${meters} m`;
  }

  getQuestImage(quest: Quest): string {
    // Pokemon encounter: ID may be in pokemonId or reward field
    const pokemonId = quest.pokemonId > 0 ? quest.pokemonId : quest.reward;
    if ((quest.rewardType === 7 || quest.rewardType === 12 || quest.rewardType === 4) && pokemonId > 0) {
      return this.iconService.getPokemonUrl(pokemonId);
    }
    if (quest.rewardType === 7 && pokemonId === 0) {
      return ''; // fallback icon in template
    }
    // Item reward — show the item icon
    if (quest.rewardType === 2 && quest.reward > 0) {
      return this.iconService.getItemUrl(quest.reward);
    }
    // Stardust
    if (quest.rewardType === 3) {
      return this.iconService.getRewardUrl('stardust', quest.reward || 0);
    }
    return this.iconService.getRewardUrl('quest', quest.rewardType);
  }

  getQuestTitle(quest: Quest): string {
    // Pokemon encounter: ID may be in pokemonId or reward field
    const pokemonId = quest.pokemonId > 0 ? quest.pokemonId : quest.reward;
    if (quest.rewardType === 7 && pokemonId > 0) {
      return this.masterData.getPokemonName(pokemonId);
    }
    if (quest.rewardType === 7 && pokemonId === 0) {
      return 'Any Pokemon Encounter';
    }
    if (quest.rewardType === 12 && pokemonId > 0) {
      return `${this.masterData.getPokemonName(pokemonId)} Mega Energy`;
    }
    if (quest.rewardType === 4 && pokemonId > 0) {
      return `${this.masterData.getPokemonName(pokemonId)} Candy`;
    }
    if (quest.rewardType === 2) {
      return this.masterData.getItemName(quest.reward);
    }
    if (quest.rewardType === 3) {
      return quest.reward > 0 ? `${quest.reward}+ Stardust` : 'Stardust';
    }
    return this.getRewardTypeLabel(quest.rewardType);
  }

  getRewardColor(rewardType: number): string {
    switch (rewardType) {
      case 7:
        return '#4CAF50';
      case 2:
        return '#2196F3';
      case 12:
        return '#9C27B0';
      case 4:
        return '#FF9800';
      default:
        return '#9E9E9E';
    }
  }

  getRewardTypeLabel(rewardType: number): string {
    switch (rewardType) {
      case 7:
        return 'Pokemon';
      case 2:
        return 'Item';
      case 12:
        return 'Mega Energy';
      case 4:
        return 'Candy';
      default:
        return `Type ${rewardType}`;
    }
  }

  loadQuests(): void {
    this.loading.set(true);
    this.questService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
        },
        next: quests => {
          this.quests.set(quests);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.loadQuests();
    });
  }

  private static readonly FALLBACK =
    "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%23999'%3E%3Cpath d='M11 18h2v-2h-2v2zm1-16C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm0-14c-2.21 0-4 1.79-4 4h2c0-1.1.9-2 2-2s2 .9 2 2c0 2-3 1.75-3 5h2c0-2.25 3-2.5 3-5 0-2.21-1.79-4-4-4z'/%3E%3C/svg%3E";

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  onImgFallback(event: Event): void {
    (event.target as HTMLImageElement).src = QuestListComponent.FALLBACK;
  }

  openAddDialog(): void {
    const ref = this.dialog.open(QuestAddDialogComponent, {
      width: '600px',
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadQuests();
    });
  }

  updateAllDistance(): void {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    ref.afterClosed().subscribe(distance => {
      if (distance !== null && distance !== undefined) {
        this.questService.updateAllDistance(distance).subscribe({
          error: () => {
            this.snackBar.open('Failed to update distances', 'OK', { duration: 3000 });
          },
          next: () => {
            this.snackBar.open('All distances updated', 'OK', { duration: 3000 });
            this.loadQuests();
          },
        });
      }
    });
  }
}
