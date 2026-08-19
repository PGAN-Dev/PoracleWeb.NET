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
import { AreaService } from '../../core/services/area.service';
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
import { WhereSheetComponent, WhereSheetData } from '../../shared/components/where-sheet/where-sheet.component';
import { LevelLabelPipe } from '../../shared/pipes/level-label.pipe';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';

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
  private readonly areaService = inject(AreaService);
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

  /** Which tab is in front: 0 raids, 1 eggs. Bulk actions are scoped to it. See #642. */
  readonly activeTab = signal(0);
  readonly eggs = signal<Egg[]>([]);
  readonly gymNames = signal<Record<string, string>>({});
  readonly loading = signal(true);

  /** Only used to word the inherited scope honestly; empty produces the more cautious wording. */
  readonly profileAreas = signal<string[]>([]);
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
      let deleted = 0;
      for (const uid of this.uidsOfKind(keys, 'raid')) {
        try {
          await firstValueFrom(this.raidService.delete(uid));
          deleted++;
        } catch {
          // Already gone. See #603.
        }
      }
      for (const uid of this.uidsOfKind(keys, 'egg')) {
        try {
          await firstValueFrom(this.eggService.delete(uid));
          deleted++;
        } catch {
          // Already gone. See #603.
        }
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadData();
      this.snackBar.open(this.i18n.instant('RAIDS.SNACK_BULK_DELETED', { count: deleted }), this.i18n.instant('TOAST.OK'), {
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
      // The server refuses a radius that would take over an alarm the user did not select, and names
      // the one in the way. Unguarded, that rejection cleared nothing, reloaded nothing and showed
      // nothing -- indistinguishable from a successful no-op. See #641.
      try {
        if (selectedRaidUids.length > 0) await firstValueFrom(this.raidService.updateBulkDistance(selectedRaidUids, distance));
        if (selectedEggUids.length > 0) await firstValueFrom(this.eggService.updateBulkDistance(selectedEggUids, distance));
      } catch (err) {
        const message = (err as { error?: { error?: string } })?.error?.error;
        this.snackBar.open(message ?? this.i18n.instant('RAIDS.SNACK_FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
          duration: 5000,
        });
        return;
      }
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

  /**
   * Change a raid's delivery scope from its card. Eggs share this list and the same sheet, so the
   * tracking type is passed rather than duplicating the method.
   */
  editScope(item: Egg | Raid, type: 'egg' | 'raid'): void {
    const data: WhereSheetData = {
      profileAreas: this.profileAreas(),
      scope: scopeOf(item.overrideLocationLabel, item.overrideAreas, item.distance),
    };

    this.dialog
      .open(WhereSheetComponent, { width: '520px', autoFocus: false, data })
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;

        const service = type === 'egg' ? this.eggService : this.raidService;
        service.update(item.uid, scopeToFields(scope)).subscribe({
          error: () => this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), { duration: 4000 }),
          next: () => {
            this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVED'), this.i18n.instant('COMMON.OK'), { duration: 2500 });
            this.loadData();
          },
        });
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
    this.loadProfileAreas();
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
    // The visible tab only. Selecting both meant a user on the Raids tab pressed Select All, saw a
    // count that included eggs they could not see, and Delete took those too -- reported as a plain
    // "deleted N alarms". Every other list scopes this to what is rendered. See #642.
    const keys = new Set<string>();
    if (this.activeTab() === 0) {
      this.raids().forEach(r => keys.add(this.selectionKey('raid', r.uid)));
    } else {
      this.eggs().forEach(e => keys.add(this.selectionKey('egg', e.uid)));
    }
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

  private loadProfileAreas(): void {
    this.areaService.getSelected().subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
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
