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

import { InvasionAddDialogComponent } from './invasion-add-dialog.component';
import { InvasionEditDialogComponent } from './invasion-edit-dialog.component';
import {
  EVENT_TYPE_INFO,
  getGruntDisplayName,
  getGruntIconUrl,
  isEventType as checkEventType,
  isGenderFixed as checkGenderFixed,
} from './invasion.constants';
import { Invasion } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { I18nService } from '../../core/services/i18n.service';
import { InvasionService } from '../../core/services/invasion.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { TestAlertService } from '../../core/services/test-alert.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';
import { WhereChipComponent } from '../../shared/components/where-chip/where-chip.component';
import { WhereSheetComponent, WhereSheetData } from '../../shared/components/where-sheet/where-sheet.component';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { isAutoDelete as cleanIsAutoDelete } from '../../shared/utils/clean-flags';

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
  private readonly invasionService = inject(InvasionService);
  private readonly masterData = inject(MasterDataService);
  private readonly snackBar = inject(MatSnackBar);
  readonly invasions = signal<Invasion[]>([]);
  readonly loading = signal(true);
  /** Only used to word the inherited scope honestly; empty produces the more cautious wording. */
  readonly profileAreas = signal<string[]>([]);
  readonly selectedIds = signal(new Set<number>());

  readonly selectMode = signal(false);

  readonly testAlertService = inject(TestAlertService);

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('INVASIONS.CONFIRM_DELETE_SELECTED'),
        message: this.i18n.instant('INVASIONS.CONFIRM_BULK_DELETE_MSG', { count: this.selectedIds().size }),
        title: this.i18n.instant('INVASIONS.CONFIRM_BULK_DELETE_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    const result = await firstValueFrom(ref.afterClosed());
    if (result) {
      const ids = [...this.selectedIds()];
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
      this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_BULK_DELETED', { count: deleted }), this.i18n.instant('TOAST.OK'), {
        duration: 3000,
      });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const uids = [...this.selectedIds()];
      // The server refuses a radius that would take over an alarm the user did not select, and names
      // the one in the way. Unguarded, that rejection cleared nothing, reloaded nothing and showed
      // nothing -- indistinguishable from a successful no-op. See #641.
      try {
        await firstValueFrom(this.invasionService.updateBulkDistance(uids, distance));
      } catch (err) {
        const message = (err as { error?: { error?: string } })?.error?.error;
        this.snackBar.open(message ?? this.i18n.instant('INVASIONS.SNACK_FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
          duration: 5000,
        });
        return;
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadInvasions();
      this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_BULK_DISTANCE', { count: uids.length }), this.i18n.instant('TOAST.OK'), {
        duration: 3000,
      });
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
            error: () =>
              this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_FAILED_DELETE_ALL'), this.i18n.instant('TOAST.OK'), { duration: 3000 }),
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
            name: getGruntDisplayName(invasion.gruntType, invasion.gender, key => this.i18n.instant(key)),
          }),
          title: this.i18n.instant('INVASIONS.CONFIRM_DELETE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.invasionService.delete(invasion.uid).subscribe({
            error: () =>
              this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_FAILED_DELETE'), this.i18n.instant('TOAST.OK'), { duration: 3000 }),
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
      .open(InvasionEditDialogComponent, { width: '600px', data: invasion, maxHeight: '90vh' })
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
      .open(WhereSheetComponent, { width: '520px', autoFocus: false, data })
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;

        this.invasionService.update(item.uid, scopeToFields(scope)).subscribe({
          error: () => this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), { duration: 4000 }),
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

  getCardAccent(gruntType: string | null): string | null {
    const info = EVENT_TYPE_INFO[gruntType ?? ''];
    return info ? info.color : null;
  }

  getDisplayName(gruntType: string | null, gender?: number): string {
    return getGruntDisplayName(gruntType, gender, key => this.i18n.instant(key));
  }

  getEventColor(gruntType: string | null): string {
    return EVENT_TYPE_INFO[gruntType ?? '']?.color ?? '';
  }

  getEventIcon(gruntType: string | null): string {
    return EVENT_TYPE_INFO[gruntType ?? '']?.icon ?? '';
  }

  getEventImgUrl(gruntType: string | null): string {
    return EVENT_TYPE_INFO[gruntType ?? '']?.imgUrl ?? '';
  }

  getGruntIcon(gruntType: string | null, gender?: number): string {
    return getGruntIconUrl(gruntType, gender);
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
          this.invasions.set(inv);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.loadProfileAreas();
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadInvasions();
  }

  openAddDialog(): void {
    this.dialog
      .open(InvasionAddDialogComponent, { width: '600px', maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadInvasions();
      });
  }

  selectAll(): void {
    const ids = new Set(this.invasions().map(i => i.uid));
    this.selectedIds.set(ids);
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

  updateAllDistance(): void {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    ref.afterClosed().subscribe(distance => {
      if (distance !== null && distance !== undefined) {
        this.invasionService.updateAllDistance(distance).subscribe({
          error: () =>
            this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), { duration: 3000 }),
          next: () => {
            this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_ALL_DISTANCE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
            this.loadInvasions();
          },
        });
      }
    });
  }

  private loadProfileAreas(): void {
    this.areaService.getSelected().subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
  }
}
