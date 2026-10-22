import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { LureAddDialogComponent } from './lure-add-dialog.component';
import { LureEditDialogComponent } from './lure-edit-dialog.component';
import { Lure } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { LureService } from '../../core/services/lure.service';
import { TestAlertService } from '../../core/services/test-alert.service';
import { AlarmCardComponent } from '../../shared/components/alarm-card/alarm-card.component';
import { AlarmEmptyStateComponent } from '../../shared/components/alarm-empty-state/alarm-empty-state.component';
import { AlarmListToolbarComponent, AlarmSortOption } from '../../shared/components/alarm-list-toolbar/alarm-list-toolbar.component';
import { BulkActionBarComponent } from '../../shared/components/bulk-action-bar/bulk-action-bar.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';
import { WhereChipComponent } from '../../shared/components/where-chip/where-chip.component';
import { WhereSheetComponent, WhereSheetData, whereSheetConfig } from '../../shared/components/where-sheet/where-sheet.component';
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
  selector: 'app-lure-list',
  standalone: true,
  styleUrl: './lure-list.component.scss',
  templateUrl: './lure-list.component.html',
})
export class LureListComponent implements OnInit {
  private readonly areaService = inject(AreaService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly dialog = inject(MatDialog);

  private readonly i18n = inject(I18nService);
  private readonly icons = inject(IconService);
  private readonly lureService = inject(LureService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  readonly selectedIds = signal(new Set<number>());
  readonly lures = signal<Lure[]>([]);
  readonly search = signal('');
  /** "default" keeps the stable order the list loads in (see orderAlarms), so nothing moves once names load. */
  readonly sortBy = signal('default');
  /** The rules the search leaves, in the chosen order; ties keep their stable order. See alarm-list-query. */
  readonly visibleLures = computed(() => {
    const sorts: Record<string, (l: Lure, b: Lure) => number> = {
      name: byText(l => this.getLureName(l.lureId)),
      newest: byNumber(l => -l.uid),
    };
    return queryAlarms(this.lures(), this.search(), l => [this.getLureName(l.lureId)], sorts[this.sortBy()] ?? null);
  });

  /** The selected rules still on screen: bulk Delete and Change radius never reach a card the search has hidden. */
  readonly actionIds = computed(() =>
    visibleSelection(
      this.selectedIds(),
      this.visibleLures().map(item => item.uid),
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

  readonly testAlertService = inject(TestAlertService);

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('POKEMON.CONFIRM_BULK_DELETE_TITLE'),
        message: this.i18n.plural('ALARM.CONFIRM_BULK_DELETE_MSG', this.actionIds().length),
        title: this.i18n.instant('POKEMON.CONFIRM_BULK_DELETE_TITLE'),
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
          await firstValueFrom(this.lureService.delete(uid));
          deleted++;
        } catch {
          // Already gone, which is what the user asked for.
        }
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadLures();
      this.snackBar.open(this.i18n.plural('ALARM.SNACK_BULK_DELETED', deleted), this.i18n.instant('COMMON.OK'), {
        duration: 3000,
      });
    }
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
        result = await firstValueFrom(this.lureService.updateBulkDistance(uids, distance));
      } catch (err) {
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
          duration: 5000,
        });
        return;
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadLures();
      this.snackBar.open(
        distanceUpdateMessage(result, this.i18n.plural('ALARM.SNACK_BULK_DISTANCE', result?.updated ?? uids.length), this.i18n),
        this.i18n.instant('COMMON.OK'),
        {
          duration: skippedAny(result) ? 6000 : 3000,
        },
      );
    }
  }

  deleteAll(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
          message: this.i18n.instant('POKEMON.CONFIRM_DELETE_ALL_MSG'),
          title: this.i18n.instant('LURES.PAGE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.lureService.deleteAll().subscribe({
            error: (err: unknown) =>
              this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE_ALL'), this.i18n.instant('COMMON.OK'), {
                duration: 3000,
              }),
            next: () => {
              this.snackBar.open(this.i18n.instant('LURES.SNACK_DELETED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadLures();
            },
          });
      });
  }

  deleteLure(lure: Lure): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE'),
          message: `${this.i18n.instant('COMMON.DELETE')} ${this.getLureName(lure.lureId)} ${this.i18n.instant('LURES.LURE_SUFFIX')}?`,
          title: this.i18n.instant('LURES.CONFIRM_DELETE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.lureService.delete(lure.uid).subscribe({
            error: (err: unknown) =>
              this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE'), this.i18n.instant('COMMON.OK'), {
                duration: 3000,
              }),
            next: () => {
              this.snackBar.open(this.i18n.instant('LURES.SNACK_DELETED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadLures();
            },
          });
      });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editLure(lure: Lure): void {
    this.dialog
      .open(LureEditDialogComponent, { width: '600px', data: lure, maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadLures();
      });
  }

  /** Change one alarm's delivery scope from its card, without opening the whole edit dialog. */
  editScope(item: Lure): void {
    const data: WhereSheetData = {
      profileAreas: this.profileAreas(),
      scope: scopeOf(item.overrideLocationLabel, item.overrideAreas, item.distance),
    };

    this.dialog
      .open(WhereSheetComponent, whereSheetConfig(data))
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;

        this.lureService.update(item.uid, scopeToFields(scope)).subscribe({
          error: (err: unknown) =>
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('WHERE.SCOPE_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), {
              duration: 4000,
            }),
          next: () => {
            this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVED'), this.i18n.instant('COMMON.OK'), { duration: 2500 });
            this.loadLures();
          },
        });
      });
  }

  formatDistance(meters: number): string {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${meters} m`;
  }

  getLureIcon(lureId: number): string {
    return this.icons.getItemUrl(lureId);
  }

  getLureName(id: number): string {
    switch (id) {
      case 501:
        return this.i18n.instant('LURES.TYPE_NORMAL');
      case 502:
        return this.i18n.instant('LURES.TYPE_GLACIAL');
      case 503:
        return this.i18n.instant('LURES.TYPE_MOSSY');
      case 504:
        return this.i18n.instant('LURES.TYPE_MAGNETIC');
      case 505:
        return this.i18n.instant('LURES.TYPE_RAINY');
      case 506:
        return this.i18n.instant('LURES.TYPE_GOLDEN');
      default:
        return this.i18n.instant('LURES.TYPE_UNKNOWN', { id });
    }
  }

  /** True when the auto-delete bit (clean bit 1) is set, ignoring the edit-in-place / summary bits. */
  isAutoDelete(clean: number): boolean {
    return (clean & 1) !== 0;
  }

  /** True when the edit-in-place bit (clean bit 2) is set, ignoring the auto-delete / summary bits. */
  isEdit(clean: number): boolean {
    return (clean & 2) !== 0;
  }

  loadLures(): void {
    this.loading.set(true);
    this.lureService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: l => {
          this.lures.set(orderAlarms(l, x => [x.lureId]));
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.loadProfileAreas();
    this.loadLures();
  }

  openAddDialog(): void {
    this.dialog
      .open(LureAddDialogComponent, {
        width: '600px',
        data: { existingRule: (uid, type) => this.describeExisting(uid, type) } satisfies AddDialogData,
        maxHeight: '90vh',
      })
      .afterClosed()
      .subscribe(r => {
        if (isEditExisting(r)) this.openExisting(r.editExisting.uid, r.editExisting.trackingType);
        else if (r) this.loadLures();
      });
  }

  selectAll(): void {
    // What is on screen: a search that hides a rule also keeps it out of Select All. See #642.
    this.selectedIds.set(new Set(this.visibleLures().map(i => i.uid)));
  }

  sendTestAlert(lure: Lure): void {
    this.testAlertService.sendTestAlert('lure', lure.uid);
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
    if (trackingType !== null && trackingType !== 'lure') return null;
    const lure = this.lures().find(l => l.uid === uid);
    const title = lure ? `${this.getLureName(lure.lureId)} ${this.i18n.instant('LURES.LURE_SUFFIX')}` : '';
    return lure ? ruleSummary(title, lure, this.profileAreas(), this.translate) : null;
  }

  private loadProfileAreas(): void {
    this.areaService.getSelected().subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
  }

  /** An add dialog was refused because of this rule and the user chose to edit it instead. */
  private openExisting(uid: number, _trackingType: null | string): void {
    const lure = this.lures().find(l => l.uid === uid);
    if (lure) this.editLure(lure);
  }
}
