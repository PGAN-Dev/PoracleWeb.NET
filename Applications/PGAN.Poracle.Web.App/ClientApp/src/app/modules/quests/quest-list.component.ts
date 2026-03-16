import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { QuestService } from '../../core/services/quest.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { Quest } from '../../core/models';
import { QuestAddDialogComponent } from './quest-add-dialog.component';
import { QuestEditDialogComponent } from './quest-edit-dialog.component';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { AlarmInfoComponent } from '../../shared/components/alarm-info/alarm-info.component';

@Component({
  selector: 'app-quest-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    AlarmInfoComponent,
  ],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>Quest Alarms</h1>
        <p class="page-description">Track field research tasks by reward type: Pokemon encounters, items, mega energy, or candy.</p>
      </div>
      <div class="header-actions">
        <button
          mat-icon-button
          [matMenuTriggerFor]="bulkMenu"
          matTooltip="Bulk Actions"
        >
          <mat-icon>more_vert</mat-icon>
        </button>
        <mat-menu #bulkMenu="matMenu">
          <button mat-menu-item (click)="deleteAll()">
            <mat-icon color="warn">delete_sweep</mat-icon> Delete All
          </button>
        </mat-menu>
        <button
          mat-fab
          color="primary"
          (click)="openAddDialog()"
          matTooltip="Add Quest Alarm"
        >
          <mat-icon>add</mat-icon>
        </button>
      </div>
    </div>

    @if (loading()) {
      <div class="loading-container">
        <mat-spinner diameter="48"></mat-spinner>
        <p class="loading-text">Loading alarms...</p>
      </div>
    } @else {
      <div class="alarm-grid">
        @for (quest of quests(); track quest.uid) {
          <mat-card class="alarm-card">
            <div class="card-top" [style.border-top-color]="getRewardColor(quest.rewardType)">
              <img
                [src]="getQuestImage(quest)"
                [alt]="getQuestTitle(quest)"
                class="item-img"
                (error)="onImageError($event)"
              />
              <div class="item-info">
                <h3>{{ getQuestTitle(quest) }}</h3>
                <span class="reward-badge" [style.background]="getRewardColor(quest.rewardType)">
                  {{ getRewardTypeLabel(quest.rewardType) }}
                </span>
              </div>
              <div class="card-top-actions">
                @if (quest.clean === 1) {
                  <span class="clean-dot" matTooltip="Clean mode enabled"></span>
                }
                @if (quest.template) {
                  <span class="template-chip" matTooltip="Template: {{ quest.template }}">{{ quest.template }}</span>
                }
              </div>
            </div>
            <mat-card-content>
              <app-alarm-info
                [distance]="quest.distance"
                [clean]="quest.clean"
                [template]="quest.template"
                [ping]="quest.ping"
              />
            </mat-card-content>
            <mat-card-actions align="end">
              <button mat-icon-button (click)="editQuest(quest)" matTooltip="Edit">
                <mat-icon>edit</mat-icon>
              </button>
              <button mat-icon-button (click)="deleteQuest(quest)" matTooltip="Delete" color="warn">
                <mat-icon>delete</mat-icon>
              </button>
            </mat-card-actions>
          </mat-card>
        } @empty {
          <div class="empty-state">
            <mat-icon class="empty-icon">assignment</mat-icon>
            <h2>No Quest Alarms Configured</h2>
            <p>Tap + to add your first quest alarm</p>
            <button mat-fab extended color="primary" (click)="openAddDialog()">
              <mat-icon>add</mat-icon> Add Quest
            </button>
          </div>
        }
      </div>
    }
  `,
  styles: [
    `
      .page-header {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        padding: 16px 24px;
        gap: 16px;
      }
      .page-header-text {
        flex: 1;
        min-width: 0;
      }
      .page-header h1 {
        margin: 0;
        font-size: 24px;
        font-weight: 400;
      }
      .page-description {
        margin: 4px 0 0;
        color: var(--text-secondary, rgba(0,0,0,0.54));
        font-size: 13px;
        line-height: 1.5;
        border-left: 3px solid #1976d2;
        padding-left: 12px;
      }
      .header-actions {
        display: flex;
        align-items: center;
        gap: 8px;
      }
      .loading-container {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        padding: 64px;
        gap: 16px;
      }
      .loading-text {
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
        font-size: 14px;
      }
      .alarm-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
        gap: 16px;
        padding: 0 24px 24px;
      }
      .alarm-card {
        position: relative;
        transition:
          transform 0.2s,
          box-shadow 0.2s;
      }
      .alarm-card:hover {
        transform: translateY(-2px);
        box-shadow: 0 4px 16px rgba(0, 0, 0, 0.12);
      }
      .card-top {
        display: flex;
        align-items: center;
        gap: 12px;
        padding: 16px 16px 0;
        border-top: 4px solid #FF9800;
      }
      .card-top-actions {
        margin-left: auto;
        display: flex;
        align-items: center;
        gap: 6px;
      }
      .clean-dot {
        width: 10px;
        height: 10px;
        border-radius: 50%;
        background: #4caf50;
        display: inline-block;
        flex-shrink: 0;
      }
      .template-chip {
        display: inline-block;
        background: #e8eaf6;
        color: #3f51b5;
        padding: 2px 8px;
        border-radius: 12px;
        font-size: 10px;
        font-weight: 500;
        max-width: 80px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .item-img {
        width: 64px;
        height: 64px;
        object-fit: contain;
      }
      .item-info h3 {
        margin: 0;
        font-size: 18px;
      }
      .reward-badge {
        display: inline-block;
        color: white;
        padding: 2px 8px;
        border-radius: 12px;
        font-size: 11px;
        margin-top: 4px;
      }
      .distance-chip-row {
        margin-top: 12px;
      }
      .distance-chip {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 4px 12px;
        border-radius: 16px;
        font-size: 12px;
        font-weight: 500;
      }
      .distance-chip mat-icon {
        font-size: 16px;
        width: 16px;
        height: 16px;
      }
      .area-mode {
        background: #e8f5e9;
        color: #2e7d32;
      }
      .distance-mode {
        background: #e3f2fd;
        color: #1565c0;
      }
      .ping-info {
        display: flex;
        align-items: center;
        gap: 4px;
        margin-top: 8px;
        font-size: 13px;
        color: var(--text-muted, rgba(0, 0, 0, 0.64));
      }
      .ping-info mat-icon {
        font-size: 16px;
        width: 16px;
        height: 16px;
      }
      .empty-state {
        grid-column: 1 / -1;
        text-align: center;
        padding: 64px 16px;
      }
      .empty-icon {
        font-size: 64px;
        width: 64px;
        height: 64px;
        color: var(--text-hint, rgba(0, 0, 0, 0.24));
      }
      .empty-state h2 {
        margin: 16px 0 8px;
        font-weight: 400;
      }
      .empty-state p {
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
        margin-bottom: 24px;
      }
    `,
  ],
})
export class QuestListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly questService = inject(QuestService);
  private readonly masterData = inject(MasterDataService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly quests = signal<Quest[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadQuests();
  }

  loadQuests(): void {
    this.loading.set(true);
    this.questService.getAll().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (quests) => {
        this.quests.set(quests);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      },
    });
  }

  getQuestImage(quest: Quest): string {
    if (quest.rewardType === 7 && quest.pokemonId > 0) {
      return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/pokemon/${quest.pokemonId}.png`;
    }
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/reward/quest/${quest.rewardType}.png`;
  }

  getQuestTitle(quest: Quest): string {
    if (quest.rewardType === 7 && quest.pokemonId > 0) {
      return this.masterData.getPokemonName(quest.pokemonId);
    }
    if (quest.rewardType === 12 && quest.pokemonId > 0) {
      return `${this.masterData.getPokemonName(quest.pokemonId)} Mega Energy`;
    }
    if (quest.rewardType === 4 && quest.pokemonId > 0) {
      return `${this.masterData.getPokemonName(quest.pokemonId)} Candy`;
    }
    if (quest.rewardType === 2) {
      return this.masterData.getItemName(quest.reward);
    }
    return `Quest Reward #${quest.reward}`;
  }

  getRewardTypeLabel(rewardType: number): string {
    switch (rewardType) {
      case 7: return 'Pokemon';
      case 2: return 'Item';
      case 12: return 'Mega Energy';
      case 4: return 'Candy';
      default: return `Type ${rewardType}`;
    }
  }

  getRewardColor(rewardType: number): string {
    switch (rewardType) {
      case 7: return '#4CAF50';
      case 2: return '#2196F3';
      case 12: return '#9C27B0';
      case 4: return '#FF9800';
      default: return '#9E9E9E';
    }
  }

  formatDistance(meters: number): string {
    if (meters >= 1000) {
      return `${(meters / 1000).toFixed(1)} km`;
    }
    return `${meters} m`;
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  openAddDialog(): void {
    const ref = this.dialog.open(QuestAddDialogComponent, {
      width: '600px',
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe((result) => {
      if (result) this.loadQuests();
    });
  }

  editQuest(quest: Quest): void {
    const ref = this.dialog.open(QuestEditDialogComponent, {
      width: '600px',
      maxHeight: '90vh',
      data: quest,
    });
    ref.afterClosed().subscribe((result) => {
      if (result) this.loadQuests();
    });
  }

  deleteQuest(quest: Quest): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Delete Quest Alarm',
        message: `Are you sure you want to delete the alarm for ${this.getQuestTitle(quest)}?`,
        confirmText: 'Delete',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.questService.delete(quest.uid).subscribe({
          next: () => {
            this.snackBar.open('Quest alarm deleted', 'OK', { duration: 3000 });
            this.loadQuests();
          },
          error: () => {
            this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 });
          },
        });
      }
    });
  }

  deleteAll(): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Delete All Quest Alarms',
        message: 'Are you sure you want to delete ALL quest alarms? This action cannot be undone.',
        confirmText: 'Delete All',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.questService.deleteAll().subscribe({
          next: () => {
            this.snackBar.open('All quest alarms deleted', 'OK', { duration: 3000 });
            this.loadQuests();
          },
          error: () => {
            this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 });
          },
        });
      }
    });
  }
}
