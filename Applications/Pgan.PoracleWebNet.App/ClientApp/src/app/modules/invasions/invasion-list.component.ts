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

import { InvasionAddDialogComponent } from './invasion-add-dialog.component';
import { InvasionEditDialogComponent } from './invasion-edit-dialog.component';
import {
  EVENT_TYPE_INFO,
  getGruntDisplayName,
  getGruntIconPath,
  isEventType as checkEventType,
  isGenderFixed as checkGenderFixed,
} from './invasion.constants';
import { Invasion } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { InvasionService } from '../../core/services/invasion.service';
import { MasterDataService } from '../../core/services/masterdata.service';
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
import { byNumber, byText, queryAlarms, visibleSelection } from '../../shared/utils/alarm-list-query';
import { orderAlarms } from '../../shared/utils/alarm-order';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { isAutoDelete as cleanIsAutoDelete } from '../../shared/utils/clean-flags';
import { distanceUpdateMessage, DistanceUpdateResult, skippedAny } from '../../shared/utils/distance-update';
import { swapToFallbackIcon } from '../../shared/utils/icon-fallback';
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
  selector: 'app-invasion-list',
  standalone: true,
  styleUrl: './invasion-list.component.scss',
  templateUrl: './invasion-list.component.html',
})
export class InvasionListComponent implements OnInit {
  private readonly areaService = inject(AreaService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly dialog = inject(MatDialog);

  private readonly i18n = inject(I18nService);
  private readonly icons = inject(IconService);
  private readonly invasionService = inject(InvasionService);
  private readonly masterData = inject(MasterDataService);
  private readonly snackBar = inject(MatSnackBar);
  /** `?add=1` from the dashboard or onboarding opens the add dialog once. */
  private readonly takeAddRequest = addOnArrival();
  private readonly translate = inject(TranslateService);
  readonly selectedIds = signal(new Set<number>());
  readonly invasions = signal<Invasion[]>([]);
  readonly search = signal('');
  /** "default" keeps the stable order the list loads in (see orderAlarms), so nothing moves once names load. */
  readonly sortBy = signal('default');
  /** The rules the search leaves, in the chosen order; ties keep their stable order. See alarm-list-query. */
  readonly visibleInvasions = computed(() => {
    const sorts: Record<string, (i: Invasion, b: Invasion) => number> = {
      name: byText(i => this.getDisplayName(i.gruntType, i.gender)),
      newest: byNumber(i => -i.uid),
    };
    return queryAlarms(
      this.invasions(),
      this.search(),
      i => [this.getDisplayName(i.gruntType, i.gender), i.gruntType],
      sorts[this.sortBy()] ?? null,
    );
  });

  /** The selected rules still on screen: bulk Delete and Change radius never reach a card the search has hidden. */
  readonly actionIds = computed(() =>
    visibleSelection(
      this.selectedIds(),
      this.visibleInvasions().map(item => item.uid),
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
          await firstValueFrom(this.invasionService.delete(uid));
          deleted++;
        } catch {
          // Already gone, which is what the user asked for.
        }
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadInvasions();
      this.snackBar.open(this.i18n.plural('ALARM.SNACK_BULK_DELETED', deleted), this.i18n.instant('TOAST.OK'), {
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
        result = await firstValueFrom(this.invasionService.updateBulkDistance(uids, distance));
      } catch (err) {
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
          duration: 5000,
        });
        return;
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadInvasions();
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
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
          message: this.i18n.instant('INVASIONS.CONFIRM_DELETE_ALL_MSG'),
          title: this.i18n.instant('INVASIONS.CONFIRM_DELETE_ALL_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.invasionService.deleteAll().subscribe({
            error: (err: unknown) =>
              this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE_ALL'), this.i18n.instant('TOAST.OK'), {
                duration: 3000,
              }),
            next: () => {
              this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_DELETED_ALL'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
              this.loadInvasions();
            },
          });
      });
  }

  deleteInvasion(invasion: Invasion): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE'),
          message: this.i18n.instant('INVASIONS.CONFIRM_DELETE_MSG', {
            name: getGruntDisplayName(
              invasion.gruntType,
              invasion.gender,
              key => this.i18n.instant(key),
              this.masterData.getGruntName(invasion.gruntType, invasion.gender),
              this.masterData.isGruntNameUntranslated(invasion.gruntType, invasion.gender),
            ),
          }),
          title: this.i18n.instant('INVASIONS.CONFIRM_DELETE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.invasionService.delete(invasion.uid).subscribe({
            error: (err: unknown) =>
              this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE'), this.i18n.instant('TOAST.OK'), {
                duration: 3000,
              }),
            next: () => {
              this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_DELETED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
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
      .open(InvasionEditDialogComponent, { width: '600px', autoFocus: 'first-heading', data: invasion, maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadInvasions();
      });
  }

  /** Change one alarm's delivery scope from its card, without opening the whole edit dialog. */
  editScope(item: Invasion): void {
    const data: WhereSheetData = {
      profileAreas: this.profileAreas(),
      scope: scopeOf(item.overrideLocationLabel, item.overrideAreas, item.distance),
    };

    this.dialog
      .open(WhereSheetComponent, whereSheetConfig(data))
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;

        this.invasionService.update(item.uid, scopeToFields(scope)).subscribe({
          error: (err: unknown) =>
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('WHERE.SCOPE_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), {
              duration: 4000,
            }),
          next: () => {
            this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVED'), this.i18n.instant('COMMON.OK'), { duration: 2500 });
            this.loadInvasions();
          },
        });
      });
  }

  formatDistance(meters: number): string {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${meters} m`;
  }

  getDisplayName(gruntType: string | null, gender?: number): string {
    return getGruntDisplayName(
      gruntType,
      gender,
      key => this.i18n.instant(key),
      this.masterData.getGruntName(gruntType, gender),
      this.masterData.isGruntNameUntranslated(gruntType, gender),
    );
  }

  getEventColor(gruntType: string | null): string {
    return EVENT_TYPE_INFO[gruntType ?? '']?.color ?? '';
  }

  getEventIcon(gruntType: string | null): string {
    return EVENT_TYPE_INFO[gruntType ?? '']?.icon ?? '';
  }

  getEventImgUrl(gruntType: string | null): string {
    const path = EVENT_TYPE_INFO[gruntType ?? '']?.imgPath;
    return path ? this.icons.getPackUrl(path) : '';
  }

  getGruntIcon(gruntType: string | null, gender?: number): string {
    return this.icons.getPackUrl(getGruntIconPath(gruntType, gender));
  }

  hideGenderLabel(gruntType: string | null): boolean {
    return checkGenderFixed(gruntType);
  }

  /** True when the auto-delete bit (clean bit 1) is set, ignoring the edit-in-place / summary bits. */
  isAutoDelete(clean: number): boolean {
    return cleanIsAutoDelete(clean);
  }

  isEventType(gruntType: string | null): boolean {
    return checkEventType(gruntType);
  }

  loadInvasions(): void {
    this.loading.set(true);
    this.invasionService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: inv => {
          // Ordered on what the card is titled by, like the other nine lists, so an edit that re-keys a rule
          // leaves its card where it was.
          this.invasions.set(orderAlarms(inv, i => [i.gruntType, i.gender]));
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.loadProfileAreas();
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadInvasions();
    if (this.takeAddRequest()) this.openAddDialog();
  }

  /** A pack without this grunt's artwork (Decoy is `invasion/46.png`) gets a generic figure, not a gap. */
  onGruntIconError(event: Event): void {
    swapToFallbackIcon(event);
  }

  openAddDialog(): void {
    this.dialog
      .open(InvasionAddDialogComponent, {
        width: '600px',
        autoFocus: 'first-heading',
        data: { existingRule: (uid, type) => this.describeExisting(uid, type) } satisfies AddDialogData,
        maxHeight: '90vh',
      })
      .afterClosed()
      .subscribe(r => {
        if (isEditExisting(r)) this.openExisting(r.editExisting.uid, r.editExisting.trackingType);
        else if (r) this.loadInvasions();
      });
  }

  selectAll(): void {
    // Adds what is on screen: a search that hides a rule also keeps it out of Select All (#642), and a tick
    // the search is hiding stays, so clearing the search brings it back.
    this.selectedIds.set(new Set([...this.selectedIds(), ...this.visibleInvasions().map(i => i.uid)]));
  }

  sendTestAlert(invasion: Invasion): void {
    this.testAlertService.sendTestAlert('invasion', invasion.uid);
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
    // Pokestop events share the invasion table (one uid space) and are listed here where they have no page.
    if (trackingType !== null && trackingType !== 'invasion' && trackingType !== 'incident') return null;
    const invasion = this.invasions().find(i => i.uid === uid);
    return invasion
      ? ruleSummary(this.getDisplayName(invasion.gruntType, invasion.gender), invasion, this.profileAreas(), this.translate)
      : null;
  }

  private loadProfileAreas(): void {
    this.areaService.getSelected().subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
  }

  /** An add dialog was refused because of this rule and the user chose to edit it instead. */
  private openExisting(uid: number, _trackingType: null | string): void {
    const invasion = this.invasions().find(i => i.uid === uid);
    if (invasion) this.editInvasion(invasion);
  }
}
