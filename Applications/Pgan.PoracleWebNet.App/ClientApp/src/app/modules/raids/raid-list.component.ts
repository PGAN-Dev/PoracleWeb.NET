import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom, forkJoin } from 'rxjs';

import { RaidAddDialogComponent } from './raid-add-dialog.component';
import { RaidEditDialogComponent, RaidEditDialogData } from './raid-edit-dialog.component';
import { Raid, Egg, LinkSide } from '../../core/models';
import { resolveLevel } from '../../core/models/raid-level.models';
import { AlarmMoveService } from '../../core/services/alarm-move.service';
import { AreaNameService } from '../../core/services/area-name.service';
import { AreaService } from '../../core/services/area.service';
import { EggService } from '../../core/services/egg.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { LinkedAccountService } from '../../core/services/linked-account.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { RaidLevelService } from '../../core/services/raid-level.service';
import { RaidService } from '../../core/services/raid.service';
import { ScannerService } from '../../core/services/scanner.service';
import { AlarmCardComponent } from '../../shared/components/alarm-card/alarm-card.component';
import { AlarmEmptyStateComponent } from '../../shared/components/alarm-empty-state/alarm-empty-state.component';
import { AlarmListToolbarComponent, AlarmSortOption } from '../../shared/components/alarm-list-toolbar/alarm-list-toolbar.component';
import { AlarmMoreMenuComponent } from '../../shared/components/alarm-more-menu/alarm-more-menu.component';
import { BulkActionBarComponent } from '../../shared/components/bulk-action-bar/bulk-action-bar.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';
import { QuietChipComponent } from '../../shared/components/quiet-chip/quiet-chip.component';
import { RsvpPillComponent } from '../../shared/components/rsvp-pill/rsvp-pill.component';
import { WhereChipComponent } from '../../shared/components/where-chip/where-chip.component';
import { WhereSheetComponent, WhereSheetData, whereSheetConfig } from '../../shared/components/where-sheet/where-sheet.component';
import { LevelLabelPipe } from '../../shared/pipes/level-label.pipe';
import { addOnArrival } from '../../shared/utils/add-on-arrival';
import { AddDialogData, isEditExisting, ruleSummary } from '../../shared/utils/add-outcome';
import {
  byDestination,
  byNumber,
  byText,
  DestinationFilter,
  queryAlarms,
  sharedDestination,
  visibleSelection,
} from '../../shared/utils/alarm-list-query';
import { orderAlarms } from '../../shared/utils/alarm-order';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { runBulkMove } from '../../shared/utils/bulk-move';
import { NO_COSTUME } from '../../shared/utils/costumes';
import { combineDistanceResults, distanceUpdateMessage, DistanceUpdateResult, skippedAny } from '../../shared/utils/distance-update';
import { teamEdge, tierEdge } from '../../shared/utils/rule-colours';
import { serverReason } from '../../shared/utils/server-reason';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlarmEmptyStateComponent,
    AlarmCardComponent,
    AlarmListToolbarComponent,
    AlarmMoreMenuComponent,
    BulkActionBarComponent,
    QuietChipComponent,
    MatButtonModule,
    MatIconModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    MatTabsModule,
    TranslatePipe,
    WhereChipComponent,
    RsvpPillComponent,
    LevelLabelPipe,
  ],
  selector: 'app-raid-list',
  standalone: true,
  styleUrl: './raid-list.component.scss',
  templateUrl: './raid-list.component.html',
})
export class RaidListComponent implements OnInit {
  private readonly areaNames = inject(AreaNameService);
  private readonly areaService = inject(AreaService);

  private readonly destroyRef = inject(DestroyRef);

  private readonly dialog = inject(MatDialog);
  private readonly eggService = inject(EggService);
  private readonly i18n = inject(I18nService);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly moves = inject(AlarmMoveService);
  private readonly raidLevelService = inject(RaidLevelService);
  private readonly raidService = inject(RaidService);
  private readonly scannerService = inject(ScannerService);
  private readonly snackBar = inject(MatSnackBar);
  /** `?add=1` from the dashboard or onboarding opens the add dialog once. */
  private readonly takeAddRequest = addOnArrival();
  private readonly translate = inject(TranslateService);

  /** Which tab is in front: 0 raids, 1 eggs. Bulk actions are scoped to it. See #642. */
  readonly activeTab = signal(0);
  // Keyed "raid:12" / "egg:12", not by the bare uid. Raid and egg uids come from separate
  // auto-increment sequences and do collide; with one set of integers covering both grids, ticking
  // one card ticked the other, and the bulk actions sent both to the raid endpoint -- deleting or
  // resizing the raid twice and leaving the egg alone. See #540.
  readonly selectedIds = signal(new Set<string>());
  /** A linked person can filter by the app each alarm is delivered to. */
  readonly destinationFilter = signal<DestinationFilter>('all');
  readonly eggs = signal<Egg[]>([]);
  readonly gymNames = signal<Record<string, string>>({});
  readonly search = signal('');
  /** "default" keeps the stable order the list loads in (see orderAlarms), so nothing moves once names load. */
  readonly sortBy = signal('default');

  /** The eggs the search leaves. An egg's name is its tier, so "name" orders them by tier too. */
  readonly visibleEggs = computed(() => {
    const sorts: Record<string, (a: Egg, b: Egg) => number> = {
      name: byNumber(e => e.level),
      newest: byNumber(e => -e.uid),
      tier: byNumber(e => e.level),
    };
    return queryAlarms(
      byDestination(this.eggs(), this.destinationFilter()),
      this.search(),
      e => [`${this.getRaidLevelName(e.level)} ${this.i18n.instant('RAIDS.EGG_SUFFIX')}`, this.gymNames()[e.gymId ?? '']],
      sorts[this.sortBy()] ?? null,
    );
  });

  readonly raids = signal<Raid[]>([]);

  /** The raids the search leaves, in the chosen order; ties keep their stable order. */
  readonly visibleRaids = computed(() => {
    const sorts: Record<string, (a: Raid, b: Raid) => number> = {
      name: byText(r => this.getRaidTitle(r)),
      newest: byNumber(r => -r.uid),
      tier: byNumber(r => r.level),
    };
    return queryAlarms(
      byDestination(this.raids(), this.destinationFilter()),
      this.search(),
      r => [this.getRaidTitle(r), r.level === 9000 ? null : this.getRaidLevelName(r.level), this.gymNames()[r.gymId ?? '']],
      sorts[this.sortBy()] ?? null,
    );
  });

  /** The selected rules on screen, on the tab in front: bulk Delete and Change radius reach nothing hidden. */
  readonly actionIds = computed(() =>
    visibleSelection(
      this.selectedIds(),
      // Only the tab in front: a raid ticked before switching to Eggs is off screen, so it is neither
      // counted nor acted on there. The selection survives, so switching back brings it back.
      this.activeTab() === 0
        ? this.visibleRaids().map(r => this.selectionKey('raid', r.uid))
        : this.visibleEggs().map(e => this.selectionKey('egg', e.uid)),
    ),
  );

  /** True for a linked person, who gets the delivery filter, the card glyph and the bulk move. */
  readonly canChooseDestination = inject(LinkedAccountService).canChoose;

  readonly loading = signal(true);

  /** Only used to word the inherited scope honestly; empty produces the more cautious wording. */
  readonly profileAreas = signal<string[]>([]);
  /** The app every selected rule on the tab in front is already on, which the bulk Deliver to menu offers disabled. */
  readonly selectionApp = computed(() =>
    this.activeTab() === 0
      ? sharedDestination(this.visibleRaids(), r => this.selectionKey('raid', r.uid), this.actionIds())
      : sharedDestination(this.visibleEggs(), e => this.selectionKey('egg', e.uid), this.actionIds()),
  );

  readonly selectMode = signal(false);

  readonly skeletonCards = Array.from({ length: 6 });

  readonly sortOptions: AlarmSortOption[] = [
    { label: 'ALARM.SORT_DEFAULT', value: 'default' },
    { label: 'ALARM.SORT_NAME', value: 'name' },
    { label: 'ALARM.SORT_TIER', value: 'tier' },
    { label: 'ALARM.SORT_NEWEST', value: 'newest' },
  ];

  /** Raid cards take the team as their edge, egg cards the tier's egg colour. */
  readonly teamEdge = teamEdge;

  readonly tierEdge = tierEdge;

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('ALARM.DELETE_SELECTED', { count: this.actionIds().length }),
        message: this.i18n.instant('ALARM.CONFIRM_BULK_DELETE_BODY'),
        title: this.i18n.plural('ALARM.CONFIRM_BULK_DELETE_TITLE', this.actionIds().length),
        warn: true,
      } as ConfirmDialogData,
    });
    const result = await firstValueFrom(ref.afterClosed());
    if (result) {
      const keys = this.actionIds();
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
      this.snackBar.open(this.i18n.plural('ALARM.SNACK_BULK_DELETED', deleted), this.i18n.instant('TOAST.OK'), {
        duration: 3000,
      });
    }
  }

  /** Moves the selected raids and eggs to the other app (two types, so two requests), then reports every outcome in one line. */
  async bulkMove(destination: LinkSide): Promise<void> {
    const keys = this.actionIds();
    await runBulkMove(
      { i18n: this.i18n, moves: this.moves, snackBar: this.snackBar },
      [
        { uids: this.uidsOfKind(keys, 'raid'), type: 'raid' },
        { uids: this.uidsOfKind(keys, 'egg'), type: 'egg' },
      ],
      destination,
      () => {
        this.selectedIds.set(new Set());
        this.selectMode.set(false);
        this.loadData();
      },
    );
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px', data: { count: this.actionIds().length } });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const keys = this.actionIds();
      const selectedRaidUids = this.uidsOfKind(keys, 'raid');
      const selectedEggUids = this.uidsOfKind(keys, 'egg');
      // The server refuses a radius that would take over an alarm the user did not select, and names
      // the one in the way. Unguarded, that rejection cleared nothing, reloaded nothing and showed
      // nothing -- indistinguishable from a successful no-op. See #641.
      let raids: DistanceUpdateResult | undefined;
      let eggs: DistanceUpdateResult | undefined;
      try {
        if (selectedRaidUids.length > 0) raids = await firstValueFrom(this.raidService.updateBulkDistance(selectedRaidUids, distance));
        if (selectedEggUids.length > 0) eggs = await firstValueFrom(this.eggService.updateBulkDistance(selectedEggUids, distance));
      } catch (err) {
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
          duration: 5000,
        });
        return;
      }
      const result = combineDistanceResults(raids, eggs);
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadData();
      this.snackBar.open(
        distanceUpdateMessage(result, this.i18n.plural('ALARM.SNACK_BULK_DISTANCE', result?.updated ?? keys.length), this.i18n),
        this.i18n.instant('TOAST.OK'),
        {
          duration: skippedAny(result) ? 6000 : 3000,
        },
      );
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
          error: (err: unknown) => {
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE_ALL'), this.i18n.instant('TOAST.OK'), {
              duration: 3000,
            });
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
          error: (err: unknown) => {
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE'), this.i18n.instant('TOAST.OK'), {
              duration: 3000,
            });
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
          error: (err: unknown) => {
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE'), this.i18n.instant('TOAST.OK'), {
              duration: 3000,
            });
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
      autoFocus: 'first-heading',
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
      autoFocus: 'first-heading',
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
      .open(WhereSheetComponent, whereSheetConfig(data))
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;

        const service = type === 'egg' ? this.eggService : this.raidService;
        service.update(item.uid, scopeToFields(scope)).subscribe({
          error: (err: unknown) =>
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('WHERE.SCOPE_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), {
              duration: 4000,
            }),
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

  /**
   * The costume pill's text: "No costume" for 0, the costume's name otherwise. Never called for
   * 9000 -- "any costume" is the default and gets no pill, like every other unfiltered field.
   */
  getCostumePillText(costume: number): string {
    return costume === NO_COSTUME ? this.i18n.instant('POKEMON.NO_COSTUME_PILL') : this.masterData.getCostumeName(costume);
  }

  getEggImage(level: number): string {
    return this.iconService.getRaidEggUrl(level);
  }

  getGymIcon(team: number): string {
    const icon = team === 4 ? 0 : team;
    return this.iconService.getGymUrl(icon);
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
          this.raids.set(orderAlarms(raids, r => [r.pokemonId, r.level, r.form, r.gymId]));
          this.eggs.set(orderAlarms(eggs, e => [e.level, e.team, e.gymId]));
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
    if (this.takeAddRequest()) this.openAddDialog();
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  openAddDialog(): void {
    const ref = this.dialog.open(RaidAddDialogComponent, {
      width: '600px',
      autoFocus: 'first-heading',
      data: { existingRule: (uid, type) => this.describeExisting(uid, type) } satisfies AddDialogData,
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (isEditExisting(result)) this.openExisting(result.editExisting.uid, result.editExisting.trackingType);
      else if (result) this.loadData();
    });
  }

  selectAll(): void {
    // The visible tab only. Selecting both meant a user on the Raids tab pressed Select All, saw a
    // count that included eggs they could not see, and Delete took those too -- reported as a plain
    // "deleted N alarms". Every other list scopes this to what is rendered. See #642. It adds to the
    // selection rather than replacing it, so the other tab's ticks are still there when the user goes back.
    const keys = new Set<string>(this.selectedIds());
    if (this.activeTab() === 0) {
      this.visibleRaids().forEach(r => keys.add(this.selectionKey('raid', r.uid)));
    } else {
      this.visibleEggs().forEach(e => keys.add(this.selectionKey('egg', e.uid)));
    }
    this.selectedIds.set(keys);
  }

  selectionKey(kind: 'raid' | 'egg', uid: number): string {
    return `${kind}:${uid}`;
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

  /** Names one of this list's rules for an add dialog's refusal, or null when the list holds no such rule. */
  private describeExisting(uid: number, trackingType: null | string): null | string {
    if (trackingType === 'egg') {
      const egg = this.eggs().find(e => e.uid === uid);
      const title = egg ? `${this.getRaidLevelName(egg.level)} ${this.i18n.instant('RAIDS.EGG_SUFFIX')}` : '';
      return egg ? ruleSummary(title, egg, this.profileAreas(), this.translate, area => this.areaNames.label(area)) : null;
    }
    if (trackingType !== null && trackingType !== 'raid') return null;
    const raid = this.raids().find(r => r.uid === uid);
    return raid
      ? ruleSummary(this.getRaidTitle(raid), raid, this.profileAreas(), this.translate, area => this.areaNames.label(area))
      : null;
  }

  private loadProfileAreas(): void {
    this.areaService.getSelected().subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
  }

  /** An add dialog was refused because of this rule and the user chose to edit it instead. */
  private openExisting(uid: number, trackingType: null | string): void {
    if (trackingType === 'egg') {
      const egg = this.eggs().find(e => e.uid === uid);
      if (egg) this.editEgg(egg);
      return;
    }
    const raid = this.raids().find(r => r.uid === uid);
    if (raid) this.editRaid(raid);
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
