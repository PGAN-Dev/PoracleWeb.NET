import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RaidService } from '../../core/services/raid.service';
import { EggService } from '../../core/services/egg.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { Raid, Egg } from '../../core/models';
import { RaidAddDialogComponent } from './raid-add-dialog.component';
import { RaidEditDialogComponent, RaidEditDialogData } from './raid-edit-dialog.component';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { forkJoin } from 'rxjs';
import { AlarmInfoComponent } from '../../shared/components/alarm-info/alarm-info.component';

@Component({
  selector: 'app-raid-list',
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
    MatTabsModule,
    MatProgressSpinnerModule,
    AlarmInfoComponent,
  ],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>Raid & Egg Alarms</h1>
        <p class="page-description">Get notified about raid bosses and egg hatches at nearby gyms. Add by raid level for all bosses at that tier, or by specific Pokemon.</p>
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
          (click)="openAddDialog()"
          [style.background-color]="'#f44336'"
          [style.color]="'white'"
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
      <mat-tab-group>
        <!-- Raids Tab -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>shield</mat-icon>
            <span class="tab-label">Raids ({{ raids().length }})</span>
          </ng-template>
          <div class="alarm-grid">
            @for (raid of raids(); track raid.uid) {
              <mat-card class="alarm-card">
                <div class="card-top" [style.border-top-color]="getLevelColor(raid.level)">
                  <img
                    [src]="getRaidImage(raid)"
                    [alt]="getRaidTitle(raid)"
                    class="item-img"
                    (error)="onImageError($event)"
                  />
                  <div class="item-info">
                    <h3>{{ getRaidTitle(raid) }}</h3>
                    <div class="level-stars">
                      @for (s of getLevelStars(raid.level); track s) {
                        <mat-icon class="star-icon">star</mat-icon>
                      }
                    </div>
                  </div>
                  <div class="card-top-actions">
                    @if (raid.clean === 1) {
                      <span class="clean-dot" matTooltip="Clean mode enabled"></span>
                    }
                    @if (raid.template) {
                      <span class="template-chip" matTooltip="Template: {{ raid.template }}">{{ raid.template }}</span>
                    }
                  </div>
                </div>
                <mat-card-content>
                  <div class="stat-grid">
                    <div class="stat">
                      <span class="stat-label">Team</span>
                      <span class="stat-value" [style.color]="getTeamColor(raid.team)">{{ getTeamName(raid.team) }}</span>
                    </div>
                  </div>
                  <app-alarm-info
                    [distance]="raid.distance"
                    [clean]="raid.clean"
                    [template]="raid.template"
                    [ping]="raid.ping"
                  />
                </mat-card-content>
                <mat-card-actions align="end">
                  <button mat-icon-button (click)="editRaid(raid)" matTooltip="Edit">
                    <mat-icon>edit</mat-icon>
                  </button>
                  <button mat-icon-button (click)="deleteRaid(raid)" matTooltip="Delete" color="warn">
                    <mat-icon>delete</mat-icon>
                  </button>
                </mat-card-actions>
              </mat-card>
            } @empty {
              <div class="empty-state">
                <svg viewBox="0 0 80 100" width="58" height="72" class="empty-icon">
                  <ellipse cx="40" cy="55" rx="32" ry="40" fill="#f44336" opacity="0.12" stroke="#f44336" stroke-width="3"/>
                  <path d="M12 45 Q25 55 40 42 Q55 55 68 45" fill="none" stroke="#f44336" stroke-width="3" stroke-linecap="round"/>
                  <circle cx="40" cy="30" r="4" fill="#f44336" opacity="0.4"/>
                  <circle cx="30" cy="22" r="2.5" fill="#f44336" opacity="0.3"/>
                  <circle cx="50" cy="25" r="3" fill="#f44336" opacity="0.35"/>
                </svg>
                <h2 class="empty-title">No Raid Alarms Configured</h2>
                <p class="empty-subtitle">Get notified about raid bosses and egg hatches at nearby gyms</p>
                <button mat-flat-button style="background-color: #f44336; color: white" (click)="openAddDialog()">
                  <mat-icon>add</mat-icon> Add Raid
                </button>
              </div>
            }
          </div>
        </mat-tab>

        <!-- Eggs Tab -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>egg</mat-icon>
            <span class="tab-label">Eggs ({{ eggs().length }})</span>
          </ng-template>
          <div class="alarm-grid">
            @for (egg of eggs(); track egg.uid) {
              <mat-card class="alarm-card">
                <div class="card-top" [style.border-top-color]="getLevelColor(egg.level)">
                  <img
                    [src]="getEggImage(egg.level)"
                    [alt]="'Level ' + egg.level + ' Egg'"
                    class="item-img"
                    (error)="onImageError($event)"
                  />
                  <div class="item-info">
                    <h3>Level {{ egg.level }} Egg</h3>
                    <div class="level-stars">
                      @for (s of getLevelStars(egg.level); track s) {
                        <mat-icon class="star-icon">star</mat-icon>
                      }
                    </div>
                  </div>
                  <div class="card-top-actions">
                    @if (egg.clean === 1) {
                      <span class="clean-dot" matTooltip="Clean mode enabled"></span>
                    }
                    @if (egg.template) {
                      <span class="template-chip" matTooltip="Template: {{ egg.template }}">{{ egg.template }}</span>
                    }
                  </div>
                </div>
                <mat-card-content>
                  <div class="stat-grid">
                    <div class="stat">
                      <span class="stat-label">Team</span>
                      <span class="stat-value" [style.color]="getTeamColor(egg.team)">{{ getTeamName(egg.team) }}</span>
                    </div>
                  </div>
                  <app-alarm-info
                    [distance]="egg.distance"
                    [clean]="egg.clean"
                    [template]="egg.template"
                    [ping]="egg.ping"
                  />
                </mat-card-content>
                <mat-card-actions align="end">
                  <button mat-icon-button (click)="editEgg(egg)" matTooltip="Edit">
                    <mat-icon>edit</mat-icon>
                  </button>
                  <button mat-icon-button (click)="deleteEgg(egg)" matTooltip="Delete" color="warn">
                    <mat-icon>delete</mat-icon>
                  </button>
                </mat-card-actions>
              </mat-card>
            } @empty {
              <div class="empty-state">
                <svg viewBox="0 0 80 100" width="58" height="72" class="empty-icon">
                  <ellipse cx="40" cy="55" rx="32" ry="40" fill="#f44336" opacity="0.12" stroke="#f44336" stroke-width="3"/>
                  <path d="M12 45 Q25 55 40 42 Q55 55 68 45" fill="none" stroke="#f44336" stroke-width="3" stroke-linecap="round"/>
                  <circle cx="40" cy="30" r="4" fill="#f44336" opacity="0.4"/>
                  <circle cx="30" cy="22" r="2.5" fill="#f44336" opacity="0.3"/>
                  <circle cx="50" cy="25" r="3" fill="#f44336" opacity="0.35"/>
                </svg>
                <h2 class="empty-title">No Egg Alarms Configured</h2>
                <p class="empty-subtitle">Get notified about raid bosses and egg hatches at nearby gyms</p>
                <button mat-flat-button style="background-color: #f44336; color: white" (click)="openAddDialog()">
                  <mat-icon>add</mat-icon> Add Egg
                </button>
              </div>
            }
          </div>
        </mat-tab>
      </mat-tab-group>
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
      .tab-label {
        margin-left: 8px;
      }
      .alarm-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
        gap: 16px;
        padding: 16px 24px 24px;
      }
      .alarm-card {
        position: relative;
        border: 1px solid var(--card-border, rgba(0, 0, 0, 0.12));
        border-left: 4px solid #f44336;
        border-radius: 12px;
        box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
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
        border-top: 4px solid #9c27b0;
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
      .level-stars {
        display: flex;
        gap: 0;
      }
      .star-icon {
        font-size: 16px;
        width: 16px;
        height: 16px;
        color: #ffc107;
      }
      .stat-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 8px;
        margin-top: 8px;
      }
      .stat {
        display: flex;
        flex-direction: column;
      }
      .stat-label {
        font-size: 11px;
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
        text-transform: uppercase;
        letter-spacing: 0.5px;
      }
      .stat-value {
        font-size: 14px;
        font-weight: 500;
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
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        padding: 80px 24px;
        text-align: center;
      }
      .empty-icon {
        margin-bottom: 16px;
        opacity: 0.8;
      }
      .empty-title {
        font-size: 20px;
        font-weight: 500;
        margin: 0 0 8px;
      }
      .empty-subtitle {
        font-size: 14px;
        color: var(--text-secondary, rgba(0,0,0,0.54));
        margin: 0 0 24px;
        max-width: 400px;
      }
    `,
  ],
})
export class RaidListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly raidService = inject(RaidService);
  private readonly eggService = inject(EggService);
  private readonly masterData = inject(MasterDataService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly raids = signal<Raid[]>([]);
  readonly eggs = signal<Egg[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadData();
  }

  loadData(): void {
    this.loading.set(true);
    forkJoin([this.raidService.getAll(), this.eggService.getAll()]).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ([raids, eggs]) => {
        this.raids.set(raids);
        this.eggs.set(eggs);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      },
    });
  }

  getRaidImage(raid: Raid): string {
    if (raid.pokemonId && raid.pokemonId !== 9000) {
      return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/pokemon/${raid.pokemonId}.png`;
    }
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/egg/${raid.level}.png`;
  }

  getRaidTitle(raid: Raid): string {
    if (raid.pokemonId && raid.pokemonId !== 9000) {
      return this.masterData.getPokemonName(raid.pokemonId);
    }
    return `Level ${raid.level} Raid`;
  }

  getEggImage(level: number): string {
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/egg/${level}.png`;
  }

  getLevelColor(level: number): string {
    switch (level) {
      case 1: return '#FF9800';
      case 2: return '#FF9800';
      case 3: return '#F44336';
      case 4: return '#F44336';
      case 5: return '#9C27B0';
      case 6: return '#4A148C';
      default: return '#9E9E9E';
    }
  }

  getLevelStars(level: number): number[] {
    return Array.from({ length: level }, (_, i) => i);
  }

  getTeamName(team: number): string {
    switch (team) {
      case 1: return 'Mystic';
      case 2: return 'Valor';
      case 3: return 'Instinct';
      default: return 'Any';
    }
  }

  getTeamColor(team: number): string {
    switch (team) {
      case 1: return '#2196F3';
      case 2: return '#F44336';
      case 3: return '#FFEB3B';
      default: return 'inherit';
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
    const ref = this.dialog.open(RaidAddDialogComponent, {
      width: '600px',
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe((result) => {
      if (result) this.loadData();
    });
  }

  editRaid(raid: Raid): void {
    const ref = this.dialog.open(RaidEditDialogComponent, {
      width: '600px',
      maxHeight: '90vh',
      data: { type: 'raid', item: raid } as RaidEditDialogData,
    });
    ref.afterClosed().subscribe((result) => {
      if (result) this.loadData();
    });
  }

  editEgg(egg: Egg): void {
    const ref = this.dialog.open(RaidEditDialogComponent, {
      width: '600px',
      maxHeight: '90vh',
      data: { type: 'egg', item: egg } as RaidEditDialogData,
    });
    ref.afterClosed().subscribe((result) => {
      if (result) this.loadData();
    });
  }

  deleteRaid(raid: Raid): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Delete Raid Alarm',
        message: `Are you sure you want to delete the alarm for ${this.getRaidTitle(raid)}?`,
        confirmText: 'Delete',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.raidService.delete(raid.uid).subscribe({
          next: () => {
            this.snackBar.open('Raid alarm deleted', 'OK', { duration: 3000 });
            this.loadData();
          },
          error: () => {
            this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 });
          },
        });
      }
    });
  }

  deleteEgg(egg: Egg): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Delete Egg Alarm',
        message: `Are you sure you want to delete the Level ${egg.level} egg alarm?`,
        confirmText: 'Delete',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.eggService.delete(egg.uid).subscribe({
          next: () => {
            this.snackBar.open('Egg alarm deleted', 'OK', { duration: 3000 });
            this.loadData();
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
        title: 'Delete All Raid & Egg Alarms',
        message: 'Are you sure you want to delete ALL raid and egg alarms? This action cannot be undone.',
        confirmText: 'Delete All',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        forkJoin([this.raidService.deleteAll(), this.eggService.deleteAll()]).subscribe({
          next: () => {
            this.snackBar.open('All raid & egg alarms deleted', 'OK', { duration: 3000 });
            this.loadData();
          },
          error: () => {
            this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 });
          },
        });
      }
    });
  }
}
