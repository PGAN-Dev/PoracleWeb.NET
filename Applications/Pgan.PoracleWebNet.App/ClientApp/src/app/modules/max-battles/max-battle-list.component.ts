import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { MaxBattleAddDialogComponent } from './max-battle-add-dialog.component';
import { MaxBattleEditDialogComponent, MaxBattleEditDialogData } from './max-battle-edit-dialog.component';
import { MaxBattle } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MaxBattleService } from '../../core/services/max-battle.service';
import { AlarmCardComponent } from '../../shared/components/alarm-card/alarm-card.component';
import { AlarmEmptyStateComponent } from '../../shared/components/alarm-empty-state/alarm-empty-state.component';
import { AlarmListToolbarComponent, AlarmSortOption } from '../../shared/components/alarm-list-toolbar/alarm-list-toolbar.component';
import { BulkActionBarComponent } from '../../shared/components/bulk-action-bar/bulk-action-bar.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';
import { QuietChipComponent } from '../../shared/components/quiet-chip/quiet-chip.component';
import { WhereChipComponent } from '../../shared/components/where-chip/where-chip.component';
import { WhereSheetComponent, WhereSheetData, whereSheetConfig } from '../../shared/components/where-sheet/where-sheet.component';
import { addOnArrival } from '../../shared/utils/add-on-arrival';
import { AddDialogData, isEditExisting, ruleSummary } from '../../shared/utils/add-outcome';
import { byNumber, byText, queryAlarms, visibleSelection } from '../../shared/utils/alarm-list-query';
import { orderAlarms } from '../../shared/utils/alarm-order';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { distanceUpdateMessage, DistanceUpdateResult, skippedAny } from '../../shared/utils/distance-update';
import { serverReason } from '../../shared/utils/server-reason';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlarmEmptyStateComponent,
    WhereChipComponent,
    AlarmListToolbarComponent,
    BulkActionBarComponent,
    AlarmCardComponent,
    QuietChipComponent,
    MatButtonModule,
    MatIconModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    TranslatePipe,
  ],
  selector: 'app-max-battle-list',
  standalone: true,
  styleUrl: './max-battle-list.component.scss',
  templateUrl: './max-battle-list.component.html',
})
export class MaxBattleListComponent implements OnInit {
  private readonly areaService = inject(AreaService);

  private readonly destroyRef = inject(DestroyRef);

  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly maxBattleService = inject(MaxBattleService);
  private readonly snackBar = inject(MatSnackBar);
  /** `?add=1` from the dashboard or onboarding opens the add dialog once. */
  private readonly takeAddRequest = addOnArrival();
  private readonly translate = inject(TranslateService);

  readonly selectedIds = signal(new Set<number>());
  readonly maxBattles = signal<MaxBattle[]>([]);
  readonly search = signal('');
  /** "default" keeps the stable order the list loads in (see orderAlarms), so nothing moves once names load. */
  readonly sortBy = signal('default');
  /** The rules the search leaves, in the chosen order; ties keep their stable order. See alarm-list-query. */
  readonly visibleMaxBattles = computed(() => {
    const sorts: Record<string, (m: MaxBattle, b: MaxBattle) => number> = {
      name: byText(m => this.getTitle(m)),
      newest: byNumber(m => -m.uid),
      tier: byNumber(m => m.level),
    };
    return queryAlarms(
      this.maxBattles(),
      this.search(),
      m => [this.getTitle(m), m.level === 9000 ? null : this.getLevelLabel(m.level), m.move === 9000 ? null : this.getMoveName(m.move)],
      sorts[this.sortBy()] ?? null,
    );
  });

  /** The selected rules still on screen: bulk Delete and Change radius never reach a card the search has hidden. */
  readonly actionIds = computed(() =>
    visibleSelection(
      this.selectedIds(),
      this.visibleMaxBattles().map(item => item.uid),
    ),
  );

  readonly loading = signal(true);
  /** Only used to word the inherited scope honestly; empty produces the more cautious wording. */
  readonly profileAreas = signal<string[]>([]);
  readonly selectMode = signal(false);

  readonly skeletonCards = Array.from({ length: 6 });

  readonly sortOptions: AlarmSortOption[] = [
    { label: 'ALARM.SORT_DEFAULT', value: 'default' },
    { label: 'ALARM.SORT_NAME', value: 'name' },
    { label: 'ALARM.SORT_TIER', value: 'tier' },
    { label: 'ALARM.SORT_NEWEST', value: 'newest' },
  ];

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
      const ids = this.actionIds();
      // Settled one at a time. A stale uid -- the row re-keyed by an edit, or removed in another tab --
      // threw out of the loop, so the deletes that had already happened went unreported and the list
      // never reloaded: the user saw nothing at all. See #603.
      let deleted = 0;
      for (const uid of ids) {
        try {
          await firstValueFrom(this.maxBattleService.delete(uid));
          deleted++;
        } catch {
          // Already gone, which is the outcome the user asked for.
        }
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadData();
      this.snackBar.open(this.i18n.plural('ALARM.SNACK_BULK_DELETED', deleted), this.i18n.instant('COMMON.OK'), {
        duration: 3000,
      });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px', data: { count: this.actionIds().length } });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const ids = this.actionIds();
      // The server refuses a radius that would take over an alarm the user did not select, and names
      // the one in the way. Unguarded, that rejection cleared nothing, reloaded nothing and showed
      // nothing -- indistinguishable from a successful no-op. See #641.
      let result: DistanceUpdateResult | undefined;
      try {
        result = await firstValueFrom(this.maxBattleService.updateBulkDistance(ids, distance));
      } catch (err) {
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
          duration: 5000,
        });
        return;
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadData();
      this.snackBar.open(
        distanceUpdateMessage(result, this.i18n.plural('ALARM.SNACK_BULK_DISTANCE', result?.updated ?? ids.length), this.i18n),
        this.i18n.instant('COMMON.OK'),
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
        message: this.i18n.instant('MAX_BATTLES.CONFIRM_DELETE_ALL_MSG'),
        title: this.i18n.instant('MAX_BATTLES.PAGE_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.maxBattleService.deleteAll().subscribe({
          error: (err: unknown) => {
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE_ALL'), this.i18n.instant('COMMON.OK'), {
              duration: 3000,
            });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('MAX_BATTLES.SNACK_DELETED_ALL'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
            this.loadData();
          },
        });
      }
    });
  }

  deleteMaxBattle(maxBattle: MaxBattle): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE'),
        message: this.i18n.instant('MAX_BATTLES.CONFIRM_DELETE_MSG', { name: this.getTitle(maxBattle) }),
        title: this.i18n.instant('MAX_BATTLES.PAGE_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.maxBattleService.delete(maxBattle.uid).subscribe({
          error: (err: unknown) => {
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE'), this.i18n.instant('COMMON.OK'), {
              duration: 3000,
            });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('MAX_BATTLES.SNACK_DELETED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
            this.loadData();
          },
        });
      }
    });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editMaxBattle(maxBattle: MaxBattle): void {
    const ref = this.dialog.open(MaxBattleEditDialogComponent, {
      width: '600px',
      autoFocus: 'first-heading',
      data: { item: maxBattle } as MaxBattleEditDialogData,
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadData();
    });
  }

  /** Change one alarm's delivery scope from its card, without opening the whole edit dialog. */
  editScope(item: MaxBattle): void {
    const data: WhereSheetData = {
      profileAreas: this.profileAreas(),
      scope: scopeOf(item.overrideLocationLabel, item.overrideAreas, item.distance),
    };

    this.dialog
      .open(WhereSheetComponent, whereSheetConfig(data))
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;

        this.maxBattleService.update(item.uid, scopeToFields(scope)).subscribe({
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

  getFormName(pokemonId: number, formId: number): string {
    return this.masterData.getFormName(pokemonId, formId);
  }

  getImage(maxBattle: MaxBattle): string {
    if (maxBattle.pokemonId && maxBattle.pokemonId !== 9000) {
      return this.iconService.getPokemonUrl(maxBattle.pokemonId);
    }
    return '';
  }

  /** PoracleNG max battle level labels */
  getLevelLabel(level: number): string {
    switch (level) {
      case 1:
      case 2:
      case 3:
      case 4:
        return this.i18n.instant('MAX_BATTLES.STAR_LABEL', { stars: level });
      case 5:
        return this.i18n.instant('MAX_BATTLES.STAR_LEGENDARY', { stars: 5 });
      case 7:
        return this.i18n.instant('MAX_BATTLES.GIGANTAMAX');
      case 8:
        return this.i18n.instant('MAX_BATTLES.LEGENDARY_GIGANTAMAX');
      default:
        return this.i18n.instant('MAX_BATTLES.LEVEL_NUM', { level });
    }
  }

  getLevelStars(level: number): number[] {
    if (level === 9000 || level > 6) return [];
    return Array.from({ length: level }, (_, i) => i);
  }

  getMoveName(moveId: number): string {
    return this.masterData.getMoveName(moveId);
  }

  getTitle(maxBattle: MaxBattle): string {
    if (maxBattle.pokemonId && maxBattle.pokemonId !== 9000) {
      return this.masterData.getPokemonName(maxBattle.pokemonId);
    }
    return this.i18n.instant('MAX_BATTLES.ANY_POKEMON');
  }

  /** True when the auto-delete bit (clean bit 1) is set, ignoring the edit-in-place / summary bits. */
  isAutoDelete(clean: number): boolean {
    return (clean & 1) !== 0;
  }

  isGmax(level: number): boolean {
    return level === 7 || level === 8;
  }

  loadData(): void {
    this.loading.set(true);
    this.maxBattleService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
        },
        next: maxBattles => {
          this.maxBattles.set(orderAlarms(maxBattles, m => [m.pokemonId, m.level, m.form, m.stationId]));
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.loadProfileAreas();
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadData();
    if (this.takeAddRequest()) this.openAddDialog();
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  openAddDialog(): void {
    const ref = this.dialog.open(MaxBattleAddDialogComponent, {
      width: '600px',
      autoFocus: 'first-heading',
      data: { existingRule: (uid, type) => this.describeExisting(uid, type) } satisfies AddDialogData,
    });
    ref.afterClosed().subscribe(result => {
      if (isEditExisting(result)) this.openExisting(result.editExisting.uid, result.editExisting.trackingType);
      else if (result) this.loadData();
    });
  }

  selectAll(): void {
    // Adds what is on screen: a search that hides a rule also keeps it out of Select All (#642), and a tick
    // the search is hiding stays, so clearing the search brings it back.
    this.selectedIds.set(new Set([...this.selectedIds(), ...this.visibleMaxBattles().map(i => i.uid)]));
  }

  toggleSelect(uid: number): void {
    const current = new Set(this.selectedIds());
    if (current.has(uid)) {
      current.delete(uid);
    } else {
      current.add(uid);
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
    if (trackingType !== null && trackingType !== 'maxbattle') return null;
    const maxBattle = this.maxBattles().find(m => m.uid === uid);
    return maxBattle ? ruleSummary(this.getTitle(maxBattle), maxBattle, this.profileAreas(), this.translate) : null;
  }

  private loadProfileAreas(): void {
    this.areaService.getSelected().subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
  }

  /** An add dialog was refused because of this rule and the user chose to edit it instead. */
  private openExisting(uid: number, _trackingType: null | string): void {
    const maxBattle = this.maxBattles().find(m => m.uid === uid);
    if (maxBattle) this.editMaxBattle(maxBattle);
  }
}
