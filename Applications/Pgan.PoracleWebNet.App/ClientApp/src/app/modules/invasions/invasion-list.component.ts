import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';

import { InvasionAddDialogComponent } from './invasion-add-dialog.component';
import { InvasionEditDialogComponent } from './invasion-edit-dialog.component';
import { Invasion } from '../../core/models';
import { InvasionService } from '../../core/services/invasion.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';

const UICONS_BASE = 'https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons';
const GRUNT_TYPE_ID: Record<string, number> = {
  bug: 7,
  dark: 17,
  dragon: 16,
  electric: 13,
  fairy: 18,
  fighting: 2,
  fire: 10,
  flying: 3,
  ghost: 8,
  grass: 12,
  ground: 5,
  ice: 15,
  metal: 9,
  normal: 1,
  poison: 4,
  psychic: 14,
  rock: 6,
  water: 11,
};
const GRUNT_INVASION_ID: Record<string, number> = {
  decoy: 50,
  giovanni: 44,
  mixed: 41,
};
const EVENT_TYPE_INFO: Record<string, { color: string; displayName: string; icon: string; imgUrl?: string }> = {
  'gold-stop': { color: '#F9E418', displayName: 'Gold Stop', icon: 'paid' },
  kecleon: { color: '#B3CA78', displayName: 'Kecleon', icon: 'visibility_off', imgUrl: `${UICONS_BASE}/pokemon/352.png` },
  showcase: { color: '#03AEB6', displayName: 'Showcase', icon: 'emoji_events' },
};
const DISPLAY_NAMES: Record<string, string> = {
  decoy: 'Decoy Grunt',
  everything: 'All Invasions',
  giovanni: 'Giovanni',
  'gold-stop': 'Gold Stop',
  kecleon: 'Kecleon',
  metal: 'Steel',
  mixed: 'Rocket Leader',
  showcase: 'Showcase',
};

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
    MatProgressSpinnerModule,
  ],
  selector: 'app-invasion-list',
  standalone: true,
  styleUrl: './invasion-list.component.scss',
  templateUrl: './invasion-list.component.html',
})
export class InvasionListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly invasionService = inject(InvasionService);
  private readonly masterData = inject(MasterDataService);
  private readonly snackBar = inject(MatSnackBar);
  readonly invasions = signal<Invasion[]>([]);
  readonly loading = signal(true);
  readonly selectedIds = signal(new Set<number>());
  readonly selectMode = signal(false);

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete Selected',
        message: `Delete ${this.selectedIds().size} alarms?`,
        title: 'Delete Selected Alarms',
        warn: true,
      } as ConfirmDialogData,
    });
    const result = await firstValueFrom(ref.afterClosed());
    if (result) {
      const ids = [...this.selectedIds()];
      for (const uid of ids) await firstValueFrom(this.invasionService.delete(uid));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadInvasions();
      this.snackBar.open(`Deleted ${ids.length} alarms`, 'OK', { duration: 3000 });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const uids = [...this.selectedIds()];
      await firstValueFrom(this.invasionService.updateBulkDistance(uids, distance));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadInvasions();
      this.snackBar.open(`Updated distance for ${uids.length} alarms`, 'OK', {
        duration: 3000,
      });
    }
  }

  deleteAll(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: 'Delete All',
          message: 'Delete ALL invasion alarms? This cannot be undone.',
          title: 'Delete All Invasion Alarms',
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.invasionService.deleteAll().subscribe({
            error: () => this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 }),
            next: () => {
              this.snackBar.open('All invasion alarms deleted', 'OK', { duration: 3000 });
              this.loadInvasions();
            },
          });
      });
  }

  deleteInvasion(invasion: Invasion): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: 'Delete',
          message: `Delete alarm for ${invasion.gruntType || 'this grunt'}?`,
          title: 'Delete Invasion Alarm',
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.invasionService.delete(invasion.uid).subscribe({
            error: () => this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 }),
            next: () => {
              this.snackBar.open('Invasion alarm deleted', 'OK', { duration: 3000 });
              this.loadInvasions();
            },
          });
      });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editInvasion(invasion: Invasion): void {
    this.dialog
      .open(InvasionEditDialogComponent, { width: '600px', data: invasion, maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadInvasions();
      });
  }

  formatDistance(meters: number): string {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${meters} m`;
  }

  getCardAccent(gruntType: string | null): string | null {
    const info = EVENT_TYPE_INFO[gruntType ?? ''];
    return info ? info.color : null;
  }

  getDisplayName(gruntType: string | null): string {
    if (!gruntType) return 'All Invasions';
    const mapped = DISPLAY_NAMES[gruntType];
    if (mapped) return mapped;
    return gruntType.charAt(0).toUpperCase() + gruntType.slice(1);
  }

  getEventColor(gruntType: string | null): string {
    return EVENT_TYPE_INFO[gruntType ?? '']?.color ?? '';
  }

  getEventIcon(gruntType: string | null): string {
    return EVENT_TYPE_INFO[gruntType ?? '']?.icon ?? '';
  }

  getEventImgUrl(gruntType: string | null): string {
    return EVENT_TYPE_INFO[gruntType ?? '']?.imgUrl ?? '';
  }

  getGruntIcon(gruntType: string | null): string {
    const type = gruntType ?? '';
    const typeId = GRUNT_TYPE_ID[type];
    if (typeId) return `${UICONS_BASE}/type/${typeId}.png`;
    const invasionId = GRUNT_INVASION_ID[type];
    if (invasionId) return `${UICONS_BASE}/invasion/${invasionId}.png`;
    return '';
  }

  isEventType(gruntType: string | null): boolean {
    return (gruntType ?? '') in EVENT_TYPE_INFO;
  }

  loadInvasions(): void {
    this.loading.set(true);
    this.invasionService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: inv => {
          this.invasions.set(inv);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadInvasions();
  }

  openAddDialog(): void {
    this.dialog
      .open(InvasionAddDialogComponent, { width: '600px', maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadInvasions();
      });
  }

  selectAll(): void {
    const ids = new Set(this.invasions().map(i => i.uid));
    this.selectedIds.set(ids);
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
        this.invasionService.updateAllDistance(distance).subscribe({
          error: () => this.snackBar.open('Failed to update distances', 'OK', { duration: 3000 }),
          next: () => {
            this.snackBar.open('All distances updated', 'OK', { duration: 3000 });
            this.loadInvasions();
          },
        });
      }
    });
  }
}
