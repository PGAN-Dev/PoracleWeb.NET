import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { QuestAddDialogComponent } from './quest-add-dialog.component';
import { QuestEditDialogComponent } from './quest-edit-dialog.component';
import { SummaryScheduleDialogComponent, SummaryScheduleDialogData } from './summary-schedule-dialog/summary-schedule-dialog.component';
import { Quest, LinkSide } from '../../core/models';
import { AlarmMoveService, summariseMoves } from '../../core/services/alarm-move.service';
import { AreaNameService } from '../../core/services/area-name.service';
import { AreaService } from '../../core/services/area.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { LinkedAccountService } from '../../core/services/linked-account.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { QuestService } from '../../core/services/quest.service';
import { SummaryScheduleService } from '../../core/services/summary-schedule.service';
import { TestAlertService } from '../../core/services/test-alert.service';
import { AlarmCardComponent } from '../../shared/components/alarm-card/alarm-card.component';
import { AlarmEmptyStateComponent } from '../../shared/components/alarm-empty-state/alarm-empty-state.component';
import { AlarmListToolbarComponent, AlarmSortOption } from '../../shared/components/alarm-list-toolbar/alarm-list-toolbar.component';
import { BulkActionBarComponent } from '../../shared/components/bulk-action-bar/bulk-action-bar.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';
import { WhereChipComponent } from '../../shared/components/where-chip/where-chip.component';
import { WhereSheetComponent, WhereSheetData, whereSheetConfig } from '../../shared/components/where-sheet/where-sheet.component';
import { addOnArrival } from '../../shared/utils/add-on-arrival';
import { AddDialogData, isEditExisting, ruleSummary } from '../../shared/utils/add-outcome';
import { byDestination, byNumber, byText, DestinationFilter, queryAlarms, visibleSelection } from '../../shared/utils/alarm-list-query';
import { orderAlarms } from '../../shared/utils/alarm-order';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { distanceUpdateMessage, DistanceUpdateResult, skippedAny } from '../../shared/utils/distance-update';
import { questRewardIcon } from '../../shared/utils/quest-reward-icon';
import { serverReason } from '../../shared/utils/server-reason';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlarmEmptyStateComponent,
    WhereChipComponent,
    AlarmListToolbarComponent,
    BulkActionBarComponent,
    AlarmCardComponent,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    TranslatePipe,
  ],
  selector: 'app-quest-list',
  standalone: true,
  styleUrl: './quest-list.component.scss',
  templateUrl: './quest-list.component.html',
})
export class QuestListComponent implements OnInit {
  private static readonly FALLBACK =
    "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%23999'%3E%3Cpath d='M11 18h2v-2h-2v2zm1-16C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm0-14c-2.21 0-4 1.79-4 4h2c0-1.1.9-2 2-2s2 .9 2 2c0 2-3 1.75-3 5h2c0-2.25 3-2.5 3-5 0-2.21-1.79-4-4-4z'/%3E%3C/svg%3E";

  private readonly areaNames = inject(AreaNameService);
  private readonly areaService = inject(AreaService);

  /** True while this alarm type is switched off: the page reads and deletes, but cannot create or edit. */

  private readonly destroyRef = inject(DestroyRef);

  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly moves = inject(AlarmMoveService);
  private readonly questService = inject(QuestService);
  private readonly snackBar = inject(MatSnackBar);
  /** `?add=1` from the dashboard or onboarding opens the add dialog once. */
  private readonly takeAddRequest = addOnArrival();
  private readonly translate = inject(TranslateService);
  readonly selectedIds = signal(new Set<number>());
  /** A linked person can filter by the app each alarm is delivered to. */
  readonly destinationFilter = signal<DestinationFilter>('all');
  readonly quests = signal<Quest[]>([]);
  readonly search = signal('');
  /** "default" keeps the stable order the list loads in (see orderAlarms), so nothing moves once names load. */
  readonly sortBy = signal('default');
  /** The rules the search leaves, in the chosen order; ties keep their stable order. See alarm-list-query. */
  readonly visibleQuests = computed(() => {
    const sorts: Record<string, (q: Quest, b: Quest) => number> = {
      name: byText(q => this.getQuestTitle(q)),
      newest: byNumber(q => -q.uid),
      reward: byNumber(q => q.rewardType),
    };
    return queryAlarms(
      byDestination(this.quests(), this.destinationFilter()),
      this.search(),
      q => [this.getQuestTitle(q), this.getRewardTypeLabel(q.rewardType)],
      sorts[this.sortBy()] ?? null,
    );
  });

  /** The selected rules still on screen: bulk Delete and Change radius never reach a card the search has hidden. */
  readonly actionIds = computed(() =>
    visibleSelection(
      this.selectedIds(),
      this.visibleQuests().map(item => item.uid),
    ),
  );

  /** True for a linked person, who gets the delivery filter, the card glyph and the bulk move. */
  readonly canChooseDestination = inject(LinkedAccountService).canChoose;

  readonly loading = signal(true);
  /** Only used to word the inherited scope honestly; empty produces the more cautious wording. */
  readonly profileAreas = signal<string[]>([]);
  readonly selectMode = signal(false);

  readonly skeletonCards = Array.from({ length: 6 });

  readonly sortOptions: AlarmSortOption[] = [
    { label: 'ALARM.SORT_DEFAULT', value: 'default' },
    { label: 'ALARM.SORT_NAME', value: 'name' },
    { label: 'ALARM.SORT_REWARD_TYPE', value: 'reward' },
    { label: 'ALARM.SORT_NEWEST', value: 'newest' },
  ];

  readonly summaryService = inject(SummaryScheduleService);

  readonly testAlertService = inject(TestAlertService);

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
      // Settled one at a time: a stale uid -- the row re-keyed by an edit, or removed in another tab --
      // threw out of the loop, so deletes that had already happened went unreported and the list never
      // reloaded. See #603.
      let deleted = 0;
      for (const uid of ids) {
        try {
          await firstValueFrom(this.questService.delete(uid));
          deleted++;
        } catch {
          // Already gone, which is what the user asked for.
        }
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadQuests();
      this.snackBar.open(this.i18n.plural('ALARM.SNACK_BULK_DELETED', deleted), this.i18n.instant('TOAST.OK'), {
        duration: 3000,
      });
    }
  }

  /** Moves the selected alarms to the other app, then reports every outcome in one line. */
  async bulkMove(destination: LinkSide): Promise<void> {
    const uids = this.actionIds();
    try {
      const response = await firstValueFrom(this.moves.moveMany('quest', uids, destination));
      this.snackBar.open(summariseMoves(response.results, destination, this.i18n), this.i18n.instant('COMMON.OK'), { duration: 8000 });
    } catch (err) {
      this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.MOVE_FAILED_ALL'), this.i18n.instant('COMMON.OK'), {
        duration: 6000,
      });
    }
    // Some of a long selection may have moved before a later request failed, so the list always reloads.
    this.selectedIds.set(new Set());
    this.selectMode.set(false);
    this.loadQuests();
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px', data: { count: this.actionIds().length } });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const uids = this.actionIds();
      // The server refuses a radius that would take over an alarm the user did not select, and names
      // the one in the way. Unguarded, that rejection cleared nothing, reloaded nothing and showed
      // nothing -- indistinguishable from a successful no-op. See #641.
      let result: DistanceUpdateResult | undefined;
      try {
        result = await firstValueFrom(this.questService.updateBulkDistance(uids, distance));
      } catch (err) {
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
          duration: 5000,
        });
        return;
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadQuests();
      this.snackBar.open(
        distanceUpdateMessage(result, this.i18n.plural('ALARM.SNACK_BULK_DISTANCE', result?.updated ?? uids.length), this.i18n),
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
        message: this.i18n.instant('QUESTS.CONFIRM_DELETE_ALL_MSG'),
        title: this.i18n.instant('QUESTS.CONFIRM_DELETE_ALL_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.questService.deleteAll().subscribe({
          error: (err: unknown) => {
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE_ALL'), this.i18n.instant('TOAST.OK'), {
              duration: 3000,
            });
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
          error: (err: unknown) => {
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE'), this.i18n.instant('TOAST.OK'), {
              duration: 3000,
            });
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
    const ref = this.dialog.open(QuestEditDialogComponent, { width: '600px', autoFocus: 'first-heading', data: quest, maxHeight: '90vh' });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadQuests();
    });
  }

  /** Change one alarm's delivery scope from its card, without opening the whole edit dialog. */
  editScope(item: Quest): void {
    const data: WhereSheetData = {
      profileAreas: this.profileAreas(),
      scope: scopeOf(item.overrideLocationLabel, item.overrideAreas, item.distance),
    };

    this.dialog
      .open(WhereSheetComponent, whereSheetConfig(data))
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;

        this.questService.update(item.uid, scopeToFields(scope)).subscribe({
          error: (err: unknown) =>
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('WHERE.SCOPE_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), {
              duration: 4000,
            }),
          next: () => {
            this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVED'), this.i18n.instant('COMMON.OK'), { duration: 2500 });
            this.loadQuests();
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

  getQuestImage(quest: Quest): string {
    return questRewardIcon(quest, this.iconService);
  }

  getQuestTitle(quest: Quest): string {
    const reward = this.describeReward(quest);

    // The minimum only applies to the rewards that come in quantities, and saying "1x" for a rule that
    // asks for one of something is noise.
    return quest.amount > 1 ? this.i18n.instant('QUESTS.AMOUNT_PREFIX', { count: quest.amount, reward }) : reward;
  }

  getRewardTypeLabel(rewardType: number): string {
    switch (rewardType) {
      case 7:
        return this.i18n.instant('QUESTS.REWARD_POKEMON');
      case 2:
        return this.i18n.instant('QUESTS.REWARD_ITEM');
      case 3:
        return this.i18n.instant('QUESTS.STARDUST');
      case 12:
        return this.i18n.instant('QUESTS.REWARD_MEGA_ENERGY');
      case 4:
        return this.i18n.instant('QUESTS.REWARD_CANDY');
      case 8:
        return this.i18n.instant('QUESTS.POKECOINS');
      default:
        return this.i18n.instant('QUESTS.REWARD_TYPE_PREFIX', { type: rewardType });
    }
  }

  /** True when the auto-delete bit (clean bit 1) is set, ignoring the edit-in-place / summary bits. */
  isAutoDelete(clean: number): boolean {
    return (clean & 1) !== 0;
  }

  /** True when the summary bit (clean bit 4) is set. */
  isSummary(clean: number): boolean {
    return (clean & 4) !== 0;
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
          this.quests.set(orderAlarms(quests, q => [q.rewardType, q.reward, q.amount]));
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.loadProfileAreas();
    this.summaryService.loadCapability();
    this.masterData
      .loadData()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.loadQuests();
      });
    if (this.takeAddRequest()) this.openAddDialog();
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
      autoFocus: 'first-heading',
      data: { existingRule: (uid, type) => this.describeExisting(uid, type) } satisfies AddDialogData,
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (isEditExisting(result)) this.openExisting(result.editExisting.uid, result.editExisting.trackingType);
      else if (result) this.loadQuests();
    });
  }

  openSummaryDialog(): void {
    this.dialog.open(SummaryScheduleDialogComponent, {
      maxWidth: '95vw',
      width: '560px',
      data: { alertType: 'quest' } as SummaryScheduleDialogData,
      maxHeight: '90vh',
    });
  }

  selectAll(): void {
    // Adds what is on screen: a search that hides a rule also keeps it out of Select All (#642), and a tick
    // the search is hiding stays, so clearing the search brings it back.
    this.selectedIds.set(new Set([...this.selectedIds(), ...this.visibleQuests().map(i => i.uid)]));
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

  /** Names one of this list's rules for an add dialog's refusal, or null when the list holds no such rule. */
  private describeExisting(uid: number, trackingType: null | string): null | string {
    if (trackingType !== null && trackingType !== 'quest') return null;
    const quest = this.quests().find(q => q.uid === uid);
    return quest
      ? ruleSummary(this.getQuestTitle(quest), quest, this.profileAreas(), this.translate, area => this.areaNames.label(area))
      : null;
  }

  private describeReward(quest: Quest): string {
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
      // reward 0 is the dialog default, meaning any item. getItemName has no id 0, so this used to
      // render as "Item #0" -- the Pokemon branch above already has the equivalent "any" case.
      return quest.reward > 0 ? this.masterData.getItemName(quest.reward) : this.i18n.instant('QUESTS.ANY_ITEM');
    }
    if (quest.rewardType === 3) {
      return quest.reward > 0
        ? this.i18n.instant('QUESTS.STARDUST_AMOUNT', { amount: quest.reward })
        : this.i18n.instant('QUESTS.STARDUST');
    }
    // Ungated on purpose: a pokecoin rule set with the bot, or left behind by a PoracleNG downgrade,
    // still has to be readable and deletable here. Only creating one asks whether the server can.
    if (quest.rewardType === 8) {
      return quest.reward > 0
        ? this.i18n.instant('QUESTS.POKECOINS_AMOUNT', { amount: quest.reward })
        : this.i18n.instant('QUESTS.POKECOINS');
    }
    return this.getRewardTypeLabel(quest.rewardType);
  }

  private loadProfileAreas(): void {
    this.areaService.getSelected().subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
  }

  /** An add dialog was refused because of this rule and the user chose to edit it instead. */
  private openExisting(uid: number, _trackingType: null | string): void {
    const quest = this.quests().find(q => q.uid === uid);
    if (quest) this.editQuest(quest);
  }
}
