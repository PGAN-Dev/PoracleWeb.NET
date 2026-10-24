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
import { firstValueFrom, forkJoin } from 'rxjs';

import { GymAddDialogComponent } from './gym-add-dialog.component';
import { GymEditDialogComponent } from './gym-edit-dialog.component';
import { Gym, LinkSide } from '../../core/models';
import { AlarmMoveService } from '../../core/services/alarm-move.service';
import { AreaNameService } from '../../core/services/area-name.service';
import { AreaService } from '../../core/services/area.service';
import { GymService } from '../../core/services/gym.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { LinkedAccountService } from '../../core/services/linked-account.service';
import { ScannerService } from '../../core/services/scanner.service';
import { TestAlertService } from '../../core/services/test-alert.service';
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
import { byDestination, byNumber, byText, DestinationFilter, queryAlarms, visibleSelection } from '../../shared/utils/alarm-list-query';
import { orderAlarms } from '../../shared/utils/alarm-order';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { runBulkMove } from '../../shared/utils/bulk-move';
import { distanceUpdateMessage, DistanceUpdateResult, skippedAny } from '../../shared/utils/distance-update';
import { teamEdge } from '../../shared/utils/rule-colours';
import { serverReason } from '../../shared/utils/server-reason';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlarmEmptyStateComponent,
    AlarmListToolbarComponent,
    BulkActionBarComponent,
    AlarmCardComponent,
    QuietChipComponent,
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
  selector: 'app-gym-list',
  standalone: true,
  styleUrl: './gym-list.component.scss',
  templateUrl: './gym-list.component.html',
})
export class GymListComponent implements OnInit {
  private readonly areaNames = inject(AreaNameService);
  private readonly areaService = inject(AreaService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly dialog = inject(MatDialog);

  private readonly gymService = inject(GymService);
  private readonly i18n = inject(I18nService);
  private readonly icons = inject(IconService);
  private readonly moves = inject(AlarmMoveService);
  private readonly scannerService = inject(ScannerService);
  private readonly snackBar = inject(MatSnackBar);
  /** `?add=1` from the dashboard or onboarding opens the add dialog once. */
  private readonly takeAddRequest = addOnArrival();
  private readonly translate = inject(TranslateService);
  readonly selectedIds = signal(new Set<number>());
  /** A linked person can filter by the app each alarm is delivered to. */
  readonly destinationFilter = signal<DestinationFilter>('all');
  readonly gymNames = signal<Record<string, string>>({});
  readonly gyms = signal<Gym[]>([]);
  readonly search = signal('');
  /** "default" keeps the stable order the list loads in (see orderAlarms), so nothing moves once names load. */
  readonly sortBy = signal('default');
  /** The rules the search leaves, in the chosen order; ties keep their stable order. See alarm-list-query. */
  readonly visibleGyms = computed(() => {
    const sorts: Record<string, (g: Gym, b: Gym) => number> = {
      name: byText(g => `${this.getTeamName(g.team)} ${this.gymNames()[g.gymId ?? ''] ?? ''}`),
      newest: byNumber(g => -g.uid),
    };
    return queryAlarms(
      byDestination(this.gyms(), this.destinationFilter()),
      this.search(),
      g => [this.getTeamName(g.team), this.gymNames()[g.gymId ?? '']],
      sorts[this.sortBy()] ?? null,
    );
  });

  /** The selected rules still on screen: bulk Delete and Change radius never reach a card the search has hidden. */
  readonly actionIds = computed(() =>
    visibleSelection(
      this.selectedIds(),
      this.visibleGyms().map(item => item.uid),
    ),
  );

  /** True for a linked person, who gets the delivery filter, the card glyph and the bulk move. */
  readonly canChooseDestination = inject(LinkedAccountService).canChoose;

  readonly loading = signal(true);
  /** Only used to word the inherited scope honestly; empty produces the more cautious wording. */
  readonly profileAreas = signal<string[]>([]);
  readonly selectMode = signal(false);

  readonly sortOptions: AlarmSortOption[] = [
    { label: 'ALARM.SORT_DEFAULT', value: 'default' },
    { label: 'ALARM.SORT_NAME', value: 'name' },
    { label: 'ALARM.SORT_NEWEST', value: 'newest' },
  ];

  /** The team's colour as the card's edge: Mystic blue, Valor red, Instinct yellow, grey for any. */
  readonly teamEdge = teamEdge;

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
          await firstValueFrom(this.gymService.delete(uid));
          deleted++;
        } catch {
          // Already gone, which is what the user asked for.
        }
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadGyms();
      this.snackBar.open(this.i18n.plural('ALARM.SNACK_BULK_DELETED', deleted), this.i18n.instant('COMMON.OK'), {
        duration: 3000,
      });
    }
  }

  /** Moves the selected alarms to the other app, then reports every outcome in one line. */
  async bulkMove(destination: LinkSide): Promise<void> {
    await runBulkMove(
      { i18n: this.i18n, moves: this.moves, snackBar: this.snackBar },
      [{ uids: this.actionIds(), type: 'gym' }],
      destination,
      () => {
        this.selectedIds.set(new Set());
        this.selectMode.set(false);
        this.loadGyms();
      },
    );
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
        result = await firstValueFrom(this.gymService.updateBulkDistance(uids, distance));
      } catch (err) {
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
          duration: 5000,
        });
        return;
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadGyms();
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
          title: this.i18n.instant('GYMS.PAGE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.gymService.deleteAll().subscribe({
            error: (err: unknown) =>
              this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE_ALL'), this.i18n.instant('COMMON.OK'), {
                duration: 3000,
              }),
            next: () => {
              this.snackBar.open(this.i18n.instant('GYMS.SNACK_DELETED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadGyms();
            },
          });
      });
  }

  deleteGym(gym: Gym): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE'),
          message: `${this.i18n.instant('COMMON.DELETE')} ${this.getTeamName(gym.team)}?`,
          title: this.i18n.instant('GYMS.CONFIRM_DELETE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.gymService.delete(gym.uid).subscribe({
            error: (err: unknown) =>
              this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE'), this.i18n.instant('COMMON.OK'), {
                duration: 3000,
              }),
            next: () => {
              this.snackBar.open(this.i18n.instant('GYMS.SNACK_DELETED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadGyms();
            },
          });
      });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editGym(gym: Gym): void {
    this.dialog
      .open(GymEditDialogComponent, { width: '600px', autoFocus: 'first-heading', data: gym, maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadGyms();
      });
  }

  /** Change one alarm's delivery scope from its card, without opening the whole edit dialog. */
  editScope(item: Gym): void {
    const data: WhereSheetData = {
      profileAreas: this.profileAreas(),
      scope: scopeOf(item.overrideLocationLabel, item.overrideAreas, item.distance),
    };

    this.dialog
      .open(WhereSheetComponent, whereSheetConfig(data))
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;

        this.gymService.update(item.uid, scopeToFields(scope)).subscribe({
          error: (err: unknown) =>
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('WHERE.SCOPE_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), {
              duration: 4000,
            }),
          next: () => {
            this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVED'), this.i18n.instant('COMMON.OK'), { duration: 2500 });
            this.loadGyms();
          },
        });
      });
  }

  formatDistance(meters: number): string {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${meters} m`;
  }

  getGymIcon(team: number): string {
    return this.icons.getGymUrl(team);
  }

  getTeamName(team: number): string {
    switch (team) {
      case 0:
        return this.i18n.instant('GYMS.TEAM_NEUTRAL');
      case 1:
        return this.i18n.instant('GYMS.TEAM_MYSTIC');
      case 2:
        return this.i18n.instant('GYMS.TEAM_VALOR');
      case 3:
        return this.i18n.instant('GYMS.TEAM_INSTINCT');
      case 4:
        // "Any team": the same words raids and eggs use for this value.
        return this.i18n.instant('RAIDS.TEAM_ANY');
      default:
        return this.i18n.instant('GYMS.TEAM_UNKNOWN', { id: team });
    }
  }

  /** True when the auto-delete bit (clean bit 1) is set, ignoring the edit-in-place / summary bits. */
  isAutoDelete(clean: number): boolean {
    return (clean & 1) !== 0;
  }

  loadGyms(): void {
    this.loading.set(true);
    this.gymService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: g => {
          this.gyms.set(orderAlarms(g, x => [x.team, x.gymId]));
          this.loading.set(false);
          this.resolveGymNames(g);
        },
      });
  }

  ngOnInit(): void {
    this.loadProfileAreas();
    this.loadGyms();
    if (this.takeAddRequest()) this.openAddDialog();
  }

  openAddDialog(): void {
    this.dialog
      .open(GymAddDialogComponent, {
        width: '600px',
        autoFocus: 'first-heading',
        data: { existingRule: (uid, type) => this.describeExisting(uid, type) } satisfies AddDialogData,
        maxHeight: '90vh',
      })
      .afterClosed()
      .subscribe(r => {
        if (isEditExisting(r)) this.openExisting(r.editExisting.uid, r.editExisting.trackingType);
        else if (r) this.loadGyms();
      });
  }

  selectAll(): void {
    // Adds what is on screen: a search that hides a rule also keeps it out of Select All (#642), and a tick
    // the search is hiding stays, so clearing the search brings it back.
    this.selectedIds.set(new Set([...this.selectedIds(), ...this.visibleGyms().map(i => i.uid)]));
  }

  sendTestAlert(gym: Gym): void {
    this.testAlertService.sendTestAlert('gym', gym.uid);
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
    if (trackingType !== null && trackingType !== 'gym') return null;
    const gym = this.gyms().find(g => g.uid === uid);
    return gym
      ? ruleSummary(this.getTeamName(gym.team), gym, this.profileAreas(), this.translate, area => this.areaNames.label(area))
      : null;
  }

  private loadProfileAreas(): void {
    this.areaService.getSelected().subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
  }

  /** An add dialog was refused because of this rule and the user chose to edit it instead. */
  private openExisting(uid: number, _trackingType: null | string): void {
    const gym = this.gyms().find(g => g.uid === uid);
    if (gym) this.editGym(gym);
  }

  private resolveGymNames(gyms: Gym[]): void {
    const ids = [...new Set(gyms.filter(g => g.gymId).map(g => g.gymId!))];
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
}
