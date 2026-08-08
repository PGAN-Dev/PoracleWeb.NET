import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom, forkJoin } from 'rxjs';

import { RaidAddDialogComponent } from './raid-add-dialog.component';
import { RaidEditDialogComponent, RaidEditDialogData } from './raid-edit-dialog.component';
import { Raid, Egg } from '../../core/models';
import { resolveLevel } from '../../core/models/raid-level.models';
import { EggService } from '../../core/services/egg.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { RaidLevelService } from '../../core/services/raid-level.service';
import { RaidService } from '../../core/services/raid.service';
import { ScannerService } from '../../core/services/scanner.service';
import { TestAlertService } from '../../core/services/test-alert.service';
import { AlarmInfoComponent } from '../../shared/components/alarm-info/alarm-info.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';
import { RsvpPillComponent } from '../../shared/components/rsvp-pill/rsvp-pill.component';
import { LevelLabelPipe } from '../../shared/pipes/level-label.pipe';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    MatTabsModule,
    TranslatePipe,
    AlarmInfoComponent,
    RsvpPillComponent,
    LevelLabelPipe,
  ],
  selector: 'app-raid-list',
  standalone: true,
  styleUrl: './raid-list.component.scss',
  templateUrl: './raid-list.component.html',
})
export class RaidListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly eggService = inject(EggService);
  private readonly i18n = inject(I18nService);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly raidLevelService = inject(RaidLevelService);
  private readonly raidService = inject(RaidService);
  private readonly scannerService = inject(ScannerService);
  private readonly snackBar = inject(MatSnackBar);
  readonly eggs = signal<Egg[]>([]);

  readonly gymNames = signal<Record<string, string>>({});
  readonly loading = signal(true);
  readonly raids = signal<Raid[]>([]);
  // Keyed "raid:12" / "egg:12", not by the bare uid. Raid and egg uids come from separate
  // auto-increment sequences and do collide; with one set of integers covering both grids, ticking
  // one card ticked the other, and the bulk actions sent both to the raid endpoint -- deleting or
  // resizing the raid twice and leaving the egg alone. See #540.
  readonly selectedIds = signal(new Set<string>());

  readonly selectMode = signal(false);
  readonly skeletonCards = Array.from({ length: 6 });
  readonly testAlertService = inject(TestAlertService);
  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('RAIDS.CONFIRM_DELETE_SELECTED'),
        message: this.i18n.instant('RAIDS.CONFIRM_BULK_DELETE_MSG', { count: this.selectedIds().size }),
        title: this.i18n.instant('RAIDS.CONFIRM_BULK_DELETE_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    const result = await firstValueFrom(ref.afterClosed());
    if (result) {
      const keys = [...this.selectedIds()];
      for (const uid of this.uidsOfKind(keys, 'raid')) {
        await firstValueFrom(this.raidService.delete(uid));
      }
      for (const uid of this.uidsOfKind(keys, 'egg')) {
        await firstValueFrom(this.eggService.delete(uid));
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadData();
      this.snackBar.open(this.i18n.instant('RAIDS.SNACK_BULK_DELETED', { count: keys.length }), this.i18n.instant('TOAST.OK'), {
        duration: 3000,
      });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const keys = [...this.selectedIds()];
      const selectedRaidUids = this.uidsOfKind(keys, 'raid');
      const selectedEggUids = this.uidsOfKind(keys, 'egg');
      if (selectedRaidUids.length > 0) await firstValueFrom(this.raidService.updateBulkDistance(selectedRaidUids, distance));
      if (selectedEggUids.length > 0) await firstValueFrom(this.eggService.updateBulkDistance(selectedEggUids, distance));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadData();
      this.snackBar.open(this.i18n.instant('RAIDS.SNACK_BULK_DISTANCE', { count: keys.length }), this.i18n.instant('TOAST.OK'), {
        duration: 3000,
      });
    }
  }

  deleteAll(): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
        message: this.i18n.instant('RAIDS.CONFIRM_DELETE_ALL_MSG'),
        title: this.i18n.instant('RAIDS.CONFIRM_DELETE_ALL_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        forkJoin([this.raidService.deleteAll(), this.eggService.deleteAll()]).subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('RAIDS.SNACK_FAILED_DELETE_ALL'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('RAIDS.SNACK_DELETED_ALL'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
            this.loadData();
          },
        });
      }
    });
  }

  deleteEgg(egg: Egg): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE'),
        message: this.i18n.instant('RAIDS.CONFIRM_DELETE_EGG_MSG', { name: this.getRaidLevelName(egg.level) }),
        title: this.i18n.instant('RAIDS.CONFIRM_DELETE_EGG_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.eggService.delete(egg.uid).subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('RAIDS.SNACK_FAILED_DELETE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('RAIDS.SNACK_EGG_DELETED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
            this.loadData();
          },
        });
      }
    });
  }

  deleteRaid(raid: Raid): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE'),
        message: this.i18n.instant('RAIDS.CONFIRM_DELETE_RAID_MSG', { name: this.getRaidTitle(raid) }),
        title: this.i18n.instant('RAIDS.CONFIRM_DELETE_RAID_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.raidService.delete(raid.uid).subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('RAIDS.SNACK_FAILED_DELETE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('RAIDS.SNACK_DELETED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
            this.loadData();
          },
        });
      }
    });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editEgg(egg: Egg): void {
    const ref = this.dialog.open(RaidEditDialogComponent, {
      width: '600px',
      data: { item: egg, type: 'egg' } as RaidEditDialogData,
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadData();
    });
  }

  editRaid(raid: Raid): void {
    const ref = this.dialog.open(RaidEditDialogComponent, {
      width: '600px',
      data: { item: raid, type: 'raid' } as RaidEditDialogData,
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadData();
    });
  }

  formatDistance(meters: number): string {
    if (meters >= 1000) {
      return `${(meters / 1000).toFixed(1)} km`;
    }
    return `${meters} m`;
  }

  getEggImage(level: number): string {
    return this.iconService.getRaidEggUrl(level);
  }

  getGymIcon(team: number): string {
    const icon = team === 4 ? 0 : team;
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/gym/${icon}.png`;
  }

  getLevelColor(level: number): string {
    switch (level) {
      case 1:
        return '#FF9800';
      case 2:
        return '#FF9800';
      case 3:
        return '#F44336';
      case 4:
        return '#F44336';
      case 5:
        return '#9C27B0';
      case 6:
        return '#4A148C';
      default:
        return '#9E9E9E';
    }
  }

  getLevelStars(level: number): number[] {
    // Stars are only meaningful for the literal "N Star Raid" tier (levels 1-5
    // per the WatWowMap masterfile). Levels 6+ (Mega, Mega Legendary, Ultra
    // Beast, Elite, Primal, Shadow, Super Mega, Coordinated, customs) carry a
    // semantic name that the title already conveys — a stars row would be
    // misleading (e.g. "Elite Raid" is not a 9-star tier).
    if (level < 1 || level > 5) return [];
    return Array.from({ length: level }, (_, i) => i);
  }

  getRaidImage(raid: Raid): string {
    if (raid.pokemonId && raid.pokemonId !== 9000) {
      return this.iconService.getPokemonUrl(raid.pokemonId);
    }
    return this.iconService.getRaidEggUrl(raid.level);
  }

  getRaidLevelName(level: number): string {
    // Prefer the live raid-level list — when the API extends the canonical
    // set (e.g. raid_20 ships in the masterfile), cards stay in sync with the
    // selector dialog. Falls back to the baked-in resolveLevel + custom shape
    // when the API hasn't loaded yet or the level is genuinely unknown.
    const liveOpt = this.raidLevelService.byValue().get(level);
    const opt = liveOpt ?? resolveLevel(level);
    if (opt.category === 'custom') {
      return this.i18n.instant(opt.labelKey) + ' ' + opt.value;
    }
    return this.i18n.instant(opt.labelKey);
  }

  getRaidTitle(raid: Raid): string {
    if (raid.pokemonId && raid.pokemonId !== 9000) {
      return this.masterData.getPokemonName(raid.pokemonId);
    }
    return raid.level === 9000
      ? this.i18n.instant('RAIDS.ALL_RAIDS')
      : this.i18n.instant('RAIDS.ALL_LEVEL_RAIDS', { level: this.getRaidLevelName(raid.level) });
  }

  getTeamColor(team: number): string {
    switch (team) {
      case 1:
        return '#2196F3';
      case 2:
        return '#F44336';
      case 3:
        return '#FFEB3B';
      default:
        return 'inherit';
    }
  }

  getTeamName(team: number): string {
    switch (team) {
      case 1:
        return this.i18n.instant('RAIDS.TEAM_MYSTIC');
      case 2:
        return this.i18n.instant('RAIDS.TEAM_VALOR');
      case 3:
        return this.i18n.instant('RAIDS.TEAM_INSTINCT');
      default:
        return this.i18n.instant('RAIDS.TEAM_ANY');
    }
  }

  /** True when the auto-delete bit (clean bit 1) is set, ignoring the edit-in-place / summary bits. */
  isAutoDelete(clean: number): boolean {
    return (clean & 1) !== 0;
  }

  loadData(): void {
    this.loading.set(true);
    forkJoin([this.raidService.getAll(), this.eggService.getAll()])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
        },
        next: ([raids, eggs]) => {
          this.raids.set(raids);
          this.eggs.set(eggs);
          this.loading.set(false);
          this.resolveGymNames([...raids, ...eggs]);
        },
      });
  }

  ngOnInit(): void {
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.raidLevelService.load();
    this.loadData();
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  openAddDialog(): void {
    const ref = this.dialog.open(RaidAddDialogComponent, {
      width: '600px',
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadData();
    });
  }

  selectAll(): void {
    const keys = new Set<string>();
    this.raids().forEach(r => keys.add(this.selectionKey('raid', r.uid)));
    this.eggs().forEach(e => keys.add(this.selectionKey('egg', e.uid)));
    this.selectedIds.set(keys);
  }

  selectionKey(kind: 'raid' | 'egg', uid: number): string {
    return `${kind}:${uid}`;
  }

  sendTestAlert(type: string, alarm: { uid: number }): void {
    this.testAlertService.sendTestAlert(type, alarm.uid);
  }

  toggleSelect(key: string): void {
    const current = new Set(this.selectedIds());
    if (current.has(key)) {
      current.delete(key);
    } else {
      current.add(key);
    }
    this.selectedIds.set(current);
  }

  toggleSelectMode(): void {
    this.selectMode.update(v => !v);
    if (!this.selectMode()) {
      this.selectedIds.set(new Set());
    }
  }

  updateAllDistance(): void {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    ref.afterClosed().subscribe(distance => {
      if (distance !== null && distance !== undefined) {
        forkJoin([this.raidService.updateAllDistance(distance), this.eggService.updateAllDistance(distance)]).subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('RAIDS.SNACK_FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('RAIDS.SNACK_ALL_DISTANCE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
            this.loadData();
          },
        });
      }
    });
  }

  private resolveGymNames(items: (Raid | Egg)[]): void {
    const ids = [...new Set(items.filter(i => i.gymId).map(i => i.gymId!))];
    if (ids.length === 0) return;
    const lookups = Object.fromEntries(ids.map(id => [id, this.scannerService.getGymById(id)]));
    forkJoin(lookups).subscribe(results => {
      const names: Record<string, string> = {};
      for (const [id, result] of Object.entries(results)) {
        if (result?.name) names[id] = result.name;
      }
      this.gymNames.set(names);
    });
  }

  private uidsOfKind(keys: string[], kind: 'raid' | 'egg'): number[] {
    return keys.filter(k => k.startsWith(`${kind}:`)).map(k => Number(k.slice(kind.length + 1)));
  }
}
