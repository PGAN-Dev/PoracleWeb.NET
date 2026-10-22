import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { PokestopEventAddDialogComponent, PokestopEventAddDialogData } from './pokestop-event-add-dialog.component';
import { PokestopEventEditDialogComponent } from './pokestop-event-edit-dialog.component';
import { PokestopEvent } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { PokestopEventService } from '../../core/services/pokestop-event.service';
import { AlarmCardComponent } from '../../shared/components/alarm-card/alarm-card.component';
import { AlarmEmptyStateComponent } from '../../shared/components/alarm-empty-state/alarm-empty-state.component';
import { AlarmListToolbarComponent, AlarmSortOption } from '../../shared/components/alarm-list-toolbar/alarm-list-toolbar.component';
import { BulkActionBarComponent } from '../../shared/components/bulk-action-bar/bulk-action-bar.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';
import { WhereChipComponent } from '../../shared/components/where-chip/where-chip.component';
import { WhereSheetComponent, WhereSheetData, whereSheetConfig } from '../../shared/components/where-sheet/where-sheet.component';
import { addOnArrival } from '../../shared/utils/add-on-arrival';
import { isEditExisting } from '../../shared/utils/add-outcome';
import { byNumber, byText, queryAlarms, visibleSelection } from '../../shared/utils/alarm-list-query';
import { orderAlarms } from '../../shared/utils/alarm-order';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { isAutoDelete } from '../../shared/utils/clean-flags';
import { distanceUpdateMessage, DistanceUpdateResult, skippedAny } from '../../shared/utils/distance-update';
import { pokestopEventInfo } from '../../shared/utils/pokestop-events';
import { serverReason } from '../../shared/utils/server-reason';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlarmEmptyStateComponent,
    AlarmListToolbarComponent,
    BulkActionBarComponent,
    AlarmCardComponent,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    TranslatePipe,
    WhereChipComponent,
  ],
  selector: 'app-pokestop-event-list',
  standalone: true,
  styleUrl: './pokestop-event-list.component.scss',
  templateUrl: './pokestop-event-list.component.html',
})
export class PokestopEventListComponent implements OnInit {
  private readonly areaService = inject(AreaService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);
  private readonly icons = inject(IconService);
  private readonly pokestopEventService = inject(PokestopEventService);
  private readonly snackBar = inject(MatSnackBar);
  /** `?add=1` from the dashboard or onboarding opens the add dialog once. */
  private readonly takeAddRequest = addOnArrival();

  readonly selectedIds = signal(new Set<number>());
  readonly events = signal<PokestopEvent[]>([]);
  readonly search = signal('');
  /** "default" keeps the stable order the list loads in (see orderAlarms), so nothing moves once names load. */
  readonly sortBy = signal('default');
  /** The rules the search leaves, in the chosen order; ties keep their stable order. See alarm-list-query. */
  readonly visibleEvents = computed(() => {
    const sorts: Record<string, (e: PokestopEvent, b: PokestopEvent) => number> = {
      name: byText(e => this.eventLabel(e)),
      newest: byNumber(e => -e.uid),
    };
    return queryAlarms(this.events(), this.search(), e => [this.eventLabel(e)], sorts[this.sortBy()] ?? null);
  });

  /** The selected rules still on screen: bulk Delete and Change radius never reach a card the search has hidden. */
  readonly actionIds = computed(() =>
    visibleSelection(
      this.selectedIds(),
      this.visibleEvents().map(item => item.uid),
    ),
  );

  readonly loading = signal(true);
  /** Only used to word the inherited scope honestly; empty produces the more cautious wording. */
  readonly profileAreas = signal<string[]>([]);
  readonly selectMode = signal(false);

  readonly sortOptions: AlarmSortOption[] = [
    { label: 'ALARM.SORT_DEFAULT', value: 'default' },
    { label: 'ALARM.SORT_NAME', value: 'name' },
    { label: 'ALARM.SORT_NEWEST', value: 'newest' },
  ];

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE'),
        message: this.i18n.instant('ALARM.SELECTED_COUNT', { count: this.actionIds().length }),
        title: this.i18n.instant('POKESTOP_EVENTS.PAGE_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    const result = await firstValueFrom(ref.afterClosed());
    if (!result) return;

    // Settled one at a time: a stale uid -- and every write here re-keys the row -- must not throw
    // out of the loop and leave deletes that already happened unreported. See #603.
    let deleted = 0;
    for (const uid of this.actionIds()) {
      try {
        await firstValueFrom(this.pokestopEventService.delete(uid));
        deleted++;
      } catch {
        // Already gone, which is what the user asked for.
      }
    }
    this.selectedIds.set(new Set());
    this.selectMode.set(false);
    this.loadItems();
    this.snackBar.open(this.i18n.plural('ALARM.SNACK_BULK_DELETED', deleted), this.i18n.instant('COMMON.OK'), {
      duration: 3000,
    });
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px', data: { count: this.actionIds().length } });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance === null || distance === undefined) return;

    const uids = this.actionIds();
    // A rejection that cleared nothing and reloaded nothing is indistinguishable from a successful
    // no-op, so say what the server said. See #641.
    let result: DistanceUpdateResult | undefined;
    try {
      result = await firstValueFrom(this.pokestopEventService.updateBulkDistance(uids, distance));
    } catch (err) {
      this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
        duration: 5000,
      });
      return;
    }
    this.selectedIds.set(new Set());
    this.selectMode.set(false);
    this.loadItems();
    this.snackBar.open(
      distanceUpdateMessage(result, this.i18n.plural('ALARM.SNACK_BULK_DISTANCE', result?.updated ?? uids.length), this.i18n),
      this.i18n.instant('COMMON.OK'),
      {
        duration: skippedAny(result) ? 6000 : 3000,
      },
    );
  }

  deleteAll(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
          message: this.i18n.instant('POKESTOP_EVENTS.CONFIRM_DELETE_ALL_MSG'),
          title: this.i18n.instant('POKESTOP_EVENTS.PAGE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.pokestopEventService.deleteAll().subscribe({
            error: (err: unknown) =>
              this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE_ALL'), this.i18n.instant('COMMON.OK'), {
                duration: 3000,
              }),
            next: () => {
              this.snackBar.open(this.i18n.instant('POKESTOP_EVENTS.SNACK_DELETED_ALL'), this.i18n.instant('COMMON.OK'), {
                duration: 3000,
              });
              this.loadItems();
            },
          });
      });
  }

  deleteItem(item: PokestopEvent): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE'),
          message: this.i18n.instant('POKESTOP_EVENTS.CONFIRM_DELETE_MSG', { event: this.eventLabel(item) }),
          title: this.i18n.instant('POKESTOP_EVENTS.PAGE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.pokestopEventService.delete(item.uid).subscribe({
            error: (err: unknown) =>
              this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE'), this.i18n.instant('COMMON.OK'), {
                duration: 3000,
              }),
            next: () => {
              this.snackBar.open(this.i18n.instant('POKESTOP_EVENTS.SNACK_DELETED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadItems();
            },
          });
      });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editItem(item: PokestopEvent): void {
    this.dialog
      .open(PokestopEventEditDialogComponent, { width: '600px', data: item, maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadItems();
      });
  }

  /** Change one alarm's delivery scope from its card, without opening the whole edit dialog. */
  editScope(item: PokestopEvent): void {
    const data: WhereSheetData = {
      profileAreas: this.profileAreas(),
      scope: scopeOf(item.overrideLocationLabel, item.overrideAreas, item.distance),
    };

    this.dialog
      .open(WhereSheetComponent, whereSheetConfig(data))
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;

        this.pokestopEventService.update(item.uid, scopeToFields(scope)).subscribe({
          error: (err: unknown) =>
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('WHERE.SCOPE_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), {
              duration: 4000,
            }),
          next: () => {
            this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVED'), this.i18n.instant('COMMON.OK'), { duration: 2500 });
            this.loadItems();
          },
        });
      });
  }

  eventColor(item: PokestopEvent): string {
    return pokestopEventInfo(item.displayType)?.color ?? '#03aeb6';
  }

  eventIcon(item: PokestopEvent): string {
    return pokestopEventInfo(item.displayType)?.icon ?? 'celebration';
  }

  eventImage(item: PokestopEvent): string {
    const path = pokestopEventInfo(item.displayType)?.imgPath;
    return path ? this.icons.getPackUrl(path) : '';
  }

  /**
   * The event's name, falling back to whatever PoracleNG stored. An event this build has no entry
   * for still has a row, and showing its raw name beats showing nothing.
   */
  eventLabel(item: PokestopEvent): string {
    const info = pokestopEventInfo(item.displayType);
    return info ? this.i18n.instant(info.displayKey) : (item.eventName ?? String(item.displayType));
  }

  /** Angular templates cannot parse bitwise `&`, so the clean bitmask is read here. */
  isAutoDelete(clean: number): boolean {
    return isAutoDelete(clean);
  }

  /** `then` runs once the list has answered, loaded or not. */
  loadItems(then?: () => void): void {
    this.loading.set(true);
    this.pokestopEventService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
          then?.();
        },
        next: items => {
          this.events.set(orderAlarms(items, e => [e.displayType]));
          this.loading.set(false);
          then?.();
        },
      });
  }

  ngOnInit(): void {
    this.loadProfileAreas();
    // Unlike the other nine, this dialog is handed the list itself (to mark events already tracked and
    // name a clash), so on arrival it waits for the list rather than opening on an empty one.
    this.loadItems(this.takeAddRequest() ? () => this.openAddDialog() : undefined);
  }

  openAddDialog(): void {
    this.dialog
      .open(PokestopEventAddDialogComponent, {
        width: '600px',
        data: { events: this.events(), profileAreas: this.profileAreas() } satisfies PokestopEventAddDialogData,
        maxHeight: '90vh',
      })
      .afterClosed()
      .subscribe(r => {
        if (isEditExisting(r)) this.openExisting(r.editExisting.uid, r.editExisting.trackingType);
        else if (r) this.loadItems();
      });
  }

  selectAll(): void {
    // What is on screen: a search that hides a rule also keeps it out of Select All. See #642.
    this.selectedIds.set(new Set(this.visibleEvents().map(i => i.uid)));
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

  private loadProfileAreas(): void {
    this.areaService.getSelected().subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
  }

  /** An add dialog was refused because of this rule and the user chose to edit it instead. */
  private openExisting(uid: number, _trackingType: null | string): void {
    const item = this.events().find(e => e.uid === uid);
    if (item) this.editItem(item);
  }
}
