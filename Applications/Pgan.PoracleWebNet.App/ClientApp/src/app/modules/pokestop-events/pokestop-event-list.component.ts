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
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { PokestopEventAddDialogComponent } from './pokestop-event-add-dialog.component';
import { PokestopEventEditDialogComponent } from './pokestop-event-edit-dialog.component';
import { PokestopEvent } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { I18nService } from '../../core/services/i18n.service';
import { PokestopEventService } from '../../core/services/pokestop-event.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';
import { WhereChipComponent } from '../../shared/components/where-chip/where-chip.component';
import { WhereSheetComponent, WhereSheetData } from '../../shared/components/where-sheet/where-sheet.component';
import { orderAlarms } from '../../shared/utils/alarm-order';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { isAutoDelete } from '../../shared/utils/clean-flags';
import { pokestopEventInfo } from '../../shared/utils/pokestop-events';

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
  private readonly pokestopEventService = inject(PokestopEventService);
  private readonly snackBar = inject(MatSnackBar);

  readonly events = signal<PokestopEvent[]>([]);
  readonly loading = signal(true);
  /** Only used to word the inherited scope honestly; empty produces the more cautious wording. */
  readonly profileAreas = signal<string[]>([]);
  readonly selectedIds = signal(new Set<number>());
  readonly selectMode = signal(false);

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE'),
        message: this.i18n.instant('ALARM.SELECTED_COUNT', { count: this.selectedIds().size }),
        title: this.i18n.instant('POKESTOP_EVENTS.PAGE_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    const result = await firstValueFrom(ref.afterClosed());
    if (!result) return;

    // Settled one at a time: a stale uid -- and every write here re-keys the row -- must not throw
    // out of the loop and leave deletes that already happened unreported. See #603.
    let deleted = 0;
    for (const uid of [...this.selectedIds()]) {
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
    this.snackBar.open(this.i18n.instant('POKESTOP_EVENTS.SNACK_BULK_DELETED', { count: deleted }), this.i18n.instant('COMMON.OK'), {
      duration: 3000,
    });
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance === null || distance === undefined) return;

    const uids = [...this.selectedIds()];
    // A rejection that cleared nothing and reloaded nothing is indistinguishable from a successful
    // no-op, so say what the server said. See #641.
    try {
      await firstValueFrom(this.pokestopEventService.updateBulkDistance(uids, distance));
    } catch (err) {
      const message = (err as { error?: { error?: string } })?.error?.error;
      this.snackBar.open(message ?? this.i18n.instant('POKESTOP_EVENTS.SNACK_FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
        duration: 5000,
      });
      return;
    }
    this.selectedIds.set(new Set());
    this.selectMode.set(false);
    this.loadItems();
    this.snackBar.open(this.i18n.instant('POKESTOP_EVENTS.SNACK_BULK_DISTANCE', { count: uids.length }), this.i18n.instant('COMMON.OK'), {
      duration: 3000,
    });
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
            error: () =>
              this.snackBar.open(this.i18n.instant('POKESTOP_EVENTS.SNACK_FAILED_DELETE'), this.i18n.instant('COMMON.OK'), {
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
            error: () =>
              this.snackBar.open(this.i18n.instant('POKESTOP_EVENTS.SNACK_FAILED_DELETE'), this.i18n.instant('COMMON.OK'), {
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
      .open(WhereSheetComponent, { width: '520px', autoFocus: false, data })
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;

        this.pokestopEventService.update(item.uid, scopeToFields(scope)).subscribe({
          error: () => this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), { duration: 4000 }),
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
    return pokestopEventInfo(item.displayType)?.imgUrl ?? '';
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

  loadItems(): void {
    this.loading.set(true);
    this.pokestopEventService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: items => {
          this.events.set(orderAlarms(items, e => [e.displayType]));
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.loadProfileAreas();
    this.loadItems();
  }

  openAddDialog(): void {
    this.dialog
      .open(PokestopEventAddDialogComponent, { width: '600px', data: this.events(), maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadItems();
      });
  }

  selectAll(): void {
    this.selectedIds.set(new Set(this.events().map(i => i.uid)));
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
      if (distance === null || distance === undefined) return;

      this.pokestopEventService.updateAllDistance(distance).subscribe({
        error: () =>
          this.snackBar.open(this.i18n.instant('POKESTOP_EVENTS.SNACK_FAILED_DISTANCE'), this.i18n.instant('COMMON.OK'), {
            duration: 3000,
          }),
        next: () => {
          this.snackBar.open(this.i18n.instant('POKESTOP_EVENTS.SNACK_ALL_DISTANCE'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
          this.loadItems();
        },
      });
    });
  }

  private loadProfileAreas(): void {
    this.areaService.getSelected().subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
  }
}
