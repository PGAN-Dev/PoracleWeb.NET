import { Component, OnInit, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MonsterService } from '../../core/services/monster.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { Monster } from '../../core/models';
import { PokemonAddDialogComponent } from './pokemon-add-dialog.component';
import { PokemonEditDialogComponent } from './pokemon-edit-dialog.component';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';

@Component({
  selector: 'app-pokemon-list',
  standalone: true,
  imports: [
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    MatChipsModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>Pokemon Alarms</h1>
        <p class="page-description">Track wild Pokemon spawns with custom IV, CP, level, and PVP filters. Use <mat-icon class="inline-icon">map</mat-icon> Areas for geofence-based alerts, or set a <mat-icon class="inline-icon">straighten</mat-icon> Distance radius from your location.</p>
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
          <button mat-menu-item (click)="updateAllDistance()">
            <mat-icon>straighten</mat-icon> Update All Distance
          </button>
          <button mat-menu-item (click)="deleteAll()">
            <mat-icon color="warn">delete_sweep</mat-icon> Delete All
          </button>
        </mat-menu>
        <button
          mat-fab
          color="primary"
          (click)="openAddDialog()"
          matTooltip="Add Pokemon Alarm"
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
        @for (monster of monsters(); track monster.uid) {
          <mat-card class="alarm-card" [class.all-pokemon-card]="monster.pokemonId === 0">
            <div class="card-top" [style.border-top-color]="getTypeColor(monster.pokemonId)">
              @if (monster.pokemonId === 0) {
                <div class="all-pokemon-icon">
                  <mat-icon>select_all</mat-icon>
                </div>
              } @else {
                <img
                  [src]="getPokemonImage(monster.pokemonId, monster.form)"
                  [alt]="'Pokemon #' + monster.pokemonId"
                  class="pokemon-img"
                  (error)="onImageError($event, monster.pokemonId)"
                />
              }
              <div class="pokemon-info">
                <h3>{{ getPokemonName(monster.pokemonId) }}</h3>
                @if (monster.pokemonId > 0) {
                  <span class="pokemon-id">#{{ monster.pokemonId }}</span>
                }
                @if (monster.form > 0) {
                  <span class="form-badge">Form {{ monster.form }}</span>
                }
              </div>
              <div class="card-top-actions">
                @if (monster.clean === 1) {
                  <span class="clean-dot" matTooltip="Clean mode enabled"></span>
                }
                @if (monster.template) {
                  <span class="template-chip" matTooltip="Template: {{ monster.template }}">{{ monster.template }}</span>
                }
              </div>
            </div>
            <mat-card-content>
              <div class="stat-grid">
                <div class="stat">
                  <span class="stat-label">IV</span>
                  <span class="stat-value">{{ getIvDisplay(monster.minIv, monster.maxIv) }}</span>
                </div>
                <div class="stat">
                  <span class="stat-label">CP</span>
                  <span class="stat-value">{{ monster.minCp }}-{{ monster.maxCp }}</span>
                </div>
                <div class="stat">
                  <span class="stat-label">Level</span>
                  <span class="stat-value">{{ monster.minLevel }}-{{ monster.maxLevel }}</span>
                </div>
                <div class="stat">
                  <span class="stat-label">Gender</span>
                  <span class="stat-value">{{ getGenderDisplay(monster.gender) }}</span>
                </div>
              </div>
              <div class="distance-chip-row">
                @if (monster.distance === 0) {
                  <span class="distance-chip area-mode">
                    <mat-icon>map</mat-icon> Using Areas
                  </span>
                } @else {
                  <span class="distance-chip distance-mode">
                    <mat-icon>straighten</mat-icon> {{ formatDistance(monster.distance) }}
                  </span>
                }
              </div>
              @if (monster.pvpRankingLeague > 0) {
                <div class="pvp-row">
                  <span class="pvp-badge">
                    <mat-icon>emoji_events</mat-icon>
                    {{ getLeagueName(monster.pvpRankingLeague) }} League
                  </span>
                  <span class="pvp-rank">Rank {{ monster.pvpRankingBest }}-{{ monster.pvpRankingWorst }}</span>
                </div>
              }
              @if (monster.ping) {
                <div class="ping-info">
                  <mat-icon>notifications</mat-icon>
                  <span>{{ monster.ping }}</span>
                </div>
              }
            </mat-card-content>
            <mat-card-actions align="end">
              <button mat-icon-button (click)="editMonster(monster)" matTooltip="Edit">
                <mat-icon>edit</mat-icon>
              </button>
              <button
                mat-icon-button
                (click)="deleteMonster(monster)"
                matTooltip="Delete"
                color="warn"
              >
                <mat-icon>delete</mat-icon>
              </button>
            </mat-card-actions>
          </mat-card>
        } @empty {
          <div class="empty-state">
            <mat-icon class="empty-icon">catching_pokemon</mat-icon>
            <h2>No Pokemon Alarms Configured</h2>
            <p>Tap + to add your first Pokemon alarm</p>
            <button mat-fab extended color="primary" (click)="openAddDialog()">
              <mat-icon>add</mat-icon> Add Pokemon
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
      .page-description .inline-icon {
        font-size: 14px;
        width: 14px;
        height: 14px;
        vertical-align: middle;
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
      .all-pokemon-card {
        border: 2px dashed rgba(0, 0, 0, 0.2);
      }
      .card-top {
        display: flex;
        align-items: center;
        gap: 12px;
        padding: 16px 16px 0;
        border-top: 4px solid #4caf50;
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
      .pokemon-img {
        width: 64px;
        height: 64px;
        object-fit: contain;
      }
      .all-pokemon-icon {
        width: 64px;
        height: 64px;
        display: flex;
        align-items: center;
        justify-content: center;
        background: #f5f5f5;
        border-radius: 50%;
      }
      .all-pokemon-icon mat-icon {
        font-size: 36px;
        width: 36px;
        height: 36px;
        color: #9e9e9e;
      }
      .pokemon-info h3 {
        margin: 0;
        font-size: 18px;
      }
      .pokemon-id {
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
        font-size: 13px;
      }
      .form-badge {
        display: inline-block;
        background: #e0e0e0;
        padding: 2px 8px;
        border-radius: 12px;
        font-size: 11px;
        margin-left: 8px;
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
      .pvp-row {
        display: flex;
        align-items: center;
        gap: 8px;
        margin-top: 8px;
        background: #f5f5f5;
        padding: 8px;
        border-radius: 8px;
      }
      .pvp-badge {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        font-size: 12px;
        font-weight: 500;
        color: #f57f17;
      }
      .pvp-badge mat-icon {
        font-size: 16px;
        width: 16px;
        height: 16px;
      }
      .pvp-rank {
        font-size: 12px;
        color: var(--text-muted, rgba(0, 0, 0, 0.64));
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
export class PokemonListComponent implements OnInit {
  private readonly monsterService = inject(MonsterService);
  private readonly masterData = inject(MasterDataService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly monsters = signal<Monster[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    // Ensure masterdata is loaded
    this.masterData.loadData().subscribe();
    this.loadMonsters();
  }

  loadMonsters(): void {
    this.loading.set(true);
    this.monsterService.getAll().subscribe({
      next: (monsters) => {
        this.monsters.set(monsters);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      },
    });
  }

  getPokemonName(id: number): string {
    if (id === 0) return 'All Pokemon';
    return this.masterData.getPokemonName(id);
  }

  getPokemonImage(pokemonId: number, form: number): string {
    if (pokemonId === 0) return '';
    const formSuffix = form > 0 ? `_f${form}` : '';
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/pokemon/${pokemonId}${formSuffix}.png`;
  }

  onImageError(event: Event, pokemonId: number): void {
    const img = event.target as HTMLImageElement;
    const fallback = `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/pokemon/${pokemonId}.png`;
    if (!img.src.endsWith(`/${pokemonId}.png`)) {
      img.src = fallback;
    } else {
      img.style.display = 'none';
    }
  }

  getTypeColor(pokemonId: number): string {
    // Color by generation range
    if (pokemonId === 0) return '#9E9E9E';
    if (pokemonId <= 151) return '#4CAF50'; // Gen 1
    if (pokemonId <= 251) return '#FFD600'; // Gen 2
    if (pokemonId <= 386) return '#2196F3'; // Gen 3
    if (pokemonId <= 493) return '#9C27B0'; // Gen 4
    if (pokemonId <= 649) return '#FF5722'; // Gen 5
    if (pokemonId <= 721) return '#E91E63'; // Gen 6
    if (pokemonId <= 809) return '#00BCD4'; // Gen 7
    if (pokemonId <= 905) return '#795548'; // Gen 8
    return '#607D8B'; // Gen 9+
  }

  getLeagueName(league: number): string {
    switch (league) {
      case 500:
        return 'Little';
      case 1500:
        return 'Great';
      case 2500:
        return 'Ultra';
      default:
        return `${league}`;
    }
  }

  getIvDisplay(minIv: number, maxIv: number): string {
    if (minIv === -1) return 'No IV Filter';
    return `${minIv}-${maxIv}%`;
  }

  getGenderDisplay(gender: number): string {
    switch (gender) {
      case 1: return '\u2642 Male';
      case 2: return '\u2640 Female';
      case 3: return 'Genderless';
      default: return 'All';
    }
  }

  formatDistance(meters: number): string {
    if (meters >= 1000) {
      return `${(meters / 1000).toFixed(1)} km`;
    }
    return `${meters} m`;
  }

  openAddDialog(): void {
    const ref = this.dialog.open(PokemonAddDialogComponent, {
      width: '600px',
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe((result) => {
      if (result) this.loadMonsters();
    });
  }

  editMonster(monster: Monster): void {
    const ref = this.dialog.open(PokemonEditDialogComponent, {
      width: '600px',
      maxHeight: '90vh',
      data: monster,
    });
    ref.afterClosed().subscribe((result) => {
      if (result) this.loadMonsters();
    });
  }

  deleteMonster(monster: Monster): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Delete Pokemon Alarm',
        message: `Are you sure you want to delete the alarm for ${this.getPokemonName(monster.pokemonId)}?`,
        confirmText: 'Delete',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.monsterService.delete(monster.uid).subscribe({
          next: () => {
            this.snackBar.open('Pokemon alarm deleted', 'OK', { duration: 3000 });
            this.loadMonsters();
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
        title: 'Delete All Pokemon Alarms',
        message:
          'Are you sure you want to delete ALL Pokemon alarms? This action cannot be undone.',
        confirmText: 'Delete All',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.monsterService.deleteAll().subscribe({
          next: () => {
            this.snackBar.open('All Pokemon alarms deleted', 'OK', {
              duration: 3000,
            });
            this.loadMonsters();
          },
          error: () => {
            this.snackBar.open('Failed to delete alarms', 'OK', {
              duration: 3000,
            });
          },
        });
      }
    });
  }

  updateAllDistance(): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Update All Distance',
        message:
          'This will update the distance for all Pokemon alarms. Enter the new distance in the field below.',
        confirmText: 'Update',
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        // Default to 0 for now; a more advanced approach would use a custom dialog
        this.monsterService.updateAllDistance(0).subscribe({
          next: () => {
            this.snackBar.open('All distances updated', 'OK', { duration: 3000 });
            this.loadMonsters();
          },
          error: () => {
            this.snackBar.open('Failed to update distances', 'OK', {
              duration: 3000,
            });
          },
        });
      }
    });
  }
}
