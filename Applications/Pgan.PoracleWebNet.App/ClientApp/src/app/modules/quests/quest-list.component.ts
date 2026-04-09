import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { QuestAddDialogComponent } from './quest-add-dialog.component';
import { QuestEditDialogComponent } from './quest-edit-dialog.component';
import { Quest } from '../../core/models';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { QuestService } from '../../core/services/quest.service';
import { TestAlertService } from '../../core/services/test-alert.service';
import { AlarmInfoComponent } from '../../shared/components/alarm-info/alarm-info.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatCardModule,
    MatButtonModule,
    MatCheckboxModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    TranslateModule,
    AlarmInfoComponent,
  ],
  selector: 'app-quest-list',
  standalone: true,
  styleUrl: './quest-list.component.scss',
  templateUrl: './quest-list.component.html',
})
export class QuestListComponent implements OnInit {
  private static readonly FALLBACK =
    "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%23999'%3E%3Cpath d='M11 18h2v-2h-2v2zm1-16C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm0-14c-2.21 0-4 1.79-4 4h2c0-1.1.9-2 2-2s2 .9 2 2c0 2-3 1.75-3 5h2c0-2.25 3-2.5 3-5 0-2.21-1.79-4-4-4z'/%3E%3C/svg%3E";

  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly questService = inject(QuestService);

  private readonly snackBar = inject(MatSnackBar);
  readonly loading = signal(true);
  readonly quests = signal<Quest[]>([]);
  readonly selectedIds = signal(new Set<number>());
  readonly selectMode = signal(false);
  readonly skeletonCards = Array.from({ length: 6 });

  readonly testAlertService = inject(TestAlertService);

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('QUESTS.CONFIRM_DELETE_SELECTED'),
        message: this.i18n.instant('QUESTS.CONFIRM_BULK_DELETE_MSG', { count: this.selectedIds().size }),
        title: this.i18n.instant('QUESTS.CONFIRM_BULK_DELETE_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    const result = await firstValueFrom(ref.afterClosed());
    if (result) {
      const ids = [...this.selectedIds()];
      for (const uid of ids) await firstValueFrom(this.questService.delete(uid));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadQuests();
      this.snackBar.open(this.i18n.instant('QUESTS.SNACK_BULK_DELETED', { count: ids.length }), this.i18n.instant('TOAST.OK'), {
        duration: 3000,
      });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const uids = [...this.selectedIds()];
      await firstValueFrom(this.questService.updateBulkDistance(uids, distance));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadQuests();
      this.snackBar.open(this.i18n.instant('QUESTS.SNACK_BULK_DISTANCE', { count: uids.length }), this.i18n.instant('TOAST.OK'), {
        duration: 3000,
      });
    }
  }

  deleteAll(): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
        message: this.i18n.instant('QUESTS.CONFIRM_DELETE_ALL_MSG'),
        title: this.i18n.instant('QUESTS.CONFIRM_DELETE_ALL_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.questService.deleteAll().subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('QUESTS.SNACK_FAILED_DELETE_ALL'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('QUESTS.SNACK_DELETED_ALL'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
            this.loadQuests();
          },
        });
      }
    });
  }

  deleteQuest(quest: Quest): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE'),
        message: this.i18n.instant('QUESTS.CONFIRM_DELETE_MSG', { name: this.getQuestTitle(quest) }),
        title: this.i18n.instant('QUESTS.CONFIRM_DELETE_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.questService.delete(quest.uid).subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('QUESTS.SNACK_FAILED_DELETE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('QUESTS.SNACK_DELETED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
            this.loadQuests();
          },
        });
      }
    });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
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
      return this.i18n.instant('QUESTS.ANY_POKEMON_ENCOUNTER');
    }
    if (quest.rewardType === 12 && pokemonId > 0) {
      return this.i18n.instant('QUESTS.MEGA_ENERGY_SUFFIX', { name: this.masterData.getPokemonName(pokemonId) });
    }
    if (quest.rewardType === 4 && pokemonId > 0) {
      return this.i18n.instant('QUESTS.CANDY_SUFFIX', { name: this.masterData.getPokemonName(pokemonId) });
    }
    if (quest.rewardType === 2) {
      return this.masterData.getItemName(quest.reward);
    }
    if (quest.rewardType === 3) {
      return quest.reward > 0
        ? this.i18n.instant('QUESTS.STARDUST_AMOUNT', { amount: quest.reward })
        : this.i18n.instant('QUESTS.STARDUST');
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
        return this.i18n.instant('QUESTS.REWARD_POKEMON');
      case 2:
        return this.i18n.instant('QUESTS.REWARD_ITEM');
      case 12:
        return this.i18n.instant('QUESTS.REWARD_MEGA_ENERGY');
      case 4:
        return this.i18n.instant('QUESTS.REWARD_CANDY');
      default:
        return this.i18n.instant('QUESTS.REWARD_TYPE_PREFIX', { type: rewardType });
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
    this.masterData
      .loadData()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.loadQuests();
      });
  }

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

  selectAll(): void {
    const ids = new Set(this.quests().map(i => i.uid));
    this.selectedIds.set(ids);
  }

  sendTestAlert(quest: Quest): void {
    this.testAlertService.sendTestAlert('quest', quest.uid);
  }

  toggleSelect(uid: number): void {
    const current = new Set(this.selectedIds());
    current.has(uid) ? current.delete(uid) : current.add(uid);
    this.selectedIds.set(current);
  }

  toggleSelectMode(): void {
    this.selectMode.update(v => !v);
    if (!this.selectMode()) this.selectedIds.set(new Set());
  }

  updateAllDistance(): void {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    ref.afterClosed().subscribe(distance => {
      if (distance !== null && distance !== undefined) {
        this.questService.updateAllDistance(distance).subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('QUESTS.SNACK_FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('QUESTS.SNACK_ALL_DISTANCE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
            this.loadQuests();
          },
        });
      }
    });
  }
}
