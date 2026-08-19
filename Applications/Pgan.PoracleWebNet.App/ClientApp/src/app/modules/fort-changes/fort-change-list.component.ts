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

import { FortChangeAddDialogComponent } from './fort-change-add-dialog.component';
import { FortChangeEditDialogComponent } from './fort-change-edit-dialog.component';
import { FortChange } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { FortChangeService } from '../../core/services/fort-change.service';
import { I18nService } from '../../core/services/i18n.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';
import { WhereChipComponent } from '../../shared/components/where-chip/where-chip.component';
import { WhereSheetComponent, WhereSheetData } from '../../shared/components/where-sheet/where-sheet.component';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';

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
  selector: 'app-fort-change-list',
  standalone: true,
  styleUrl: './fort-change-list.component.scss',
  templateUrl: './fort-change-list.component.html',
})
export class FortChangeListComponent implements OnInit {
  private readonly areaService = inject(AreaService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly fortChangeService = inject(FortChangeService);
  private readonly i18n = inject(I18nService);
  private readonly snackBar = inject(MatSnackBar);
  readonly fortChanges = signal<FortChange[]>([]);
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
        title: this.i18n.instant('FORT_CHANGES.PAGE_TITLE'),
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
          await firstValueFrom(this.fortChangeService.delete(uid));
          deleted++;
        } catch {
          // Already gone, which is what the user asked for.
        }
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadItems();
      this.snackBar.open(this.i18n.instant('FORT_CHANGES.SNACK_BULK_DELETED', { count: deleted }), this.i18n.instant('COMMON.OK'), {
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
        await firstValueFrom(this.fortChangeService.updateBulkDistance(uids, distance));
      } catch (err) {
        const message = (err as { error?: { error?: string } })?.error?.error;
        this.snackBar.open(message ?? this.i18n.instant('FORT_CHANGES.SNACK_FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
          duration: 5000,
        });
        return;
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadItems();
      this.snackBar.open(this.i18n.instant('FORT_CHANGES.SNACK_BULK_DISTANCE', { count: uids.length }), this.i18n.instant('COMMON.OK'), {
        duration: 3000,
      });
    }
  }

  deleteAll(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
          message: this.i18n.instant('FORT_CHANGES.CONFIRM_DELETE_ALL_MSG'),
          title: this.i18n.instant('FORT_CHANGES.PAGE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.fortChangeService.deleteAll().subscribe({
            error: () =>
              this.snackBar.open(this.i18n.instant('FORT_CHANGES.SNACK_FAILED_DELETE'), this.i18n.instant('COMMON.OK'), { duration: 3000 }),
            next: () => {
              this.snackBar.open(this.i18n.instant('FORT_CHANGES.SNACK_DELETED_ALL'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadItems();
            },
          });
      });
  }

  deleteItem(item: FortChange): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE'),
          message: this.i18n.instant('FORT_CHANGES.CONFIRM_DELETE_MSG', { type: this.formatFortType(item.fortType) }),
          title: this.i18n.instant('FORT_CHANGES.PAGE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.fortChangeService.delete(item.uid).subscribe({
            error: () =>
              this.snackBar.open(this.i18n.instant('FORT_CHANGES.SNACK_FAILED_DELETE'), this.i18n.instant('COMMON.OK'), { duration: 3000 }),
            next: () => {
              this.snackBar.open(this.i18n.instant('FORT_CHANGES.SNACK_DELETED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadItems();
            },
          });
      });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editItem(item: FortChange): void {
    this.dialog
      .open(FortChangeEditDialogComponent, { width: '600px', data: item, maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadItems();
      });
  }

  /** Change one alarm's delivery scope from its card, without opening the whole edit dialog. */
  editScope(item: FortChange): void {
    const data: WhereSheetData = {
      profileAreas: this.profileAreas(),
      scope: scopeOf(item.overrideLocationLabel, item.overrideAreas, item.distance),
    };

    this.dialog
      .open(WhereSheetComponent, { width: '520px', autoFocus: false, data })
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;

        this.fortChangeService.update(item.uid, scopeToFields(scope)).subscribe({
          error: () => this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), { duration: 4000 }),
          next: () => {
            this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVED'), this.i18n.instant('COMMON.OK'), { duration: 2500 });
            this.loadItems();
          },
        });
      });
  }

  formatChangeTypes(types: string[]): string {
    if (!types || types.length === 0) return this.i18n.instant('FORT_CHANGES.ALL_CHANGES');
    return types
      .map(t => {
        switch (t) {
          case 'name':
            return this.i18n.instant('FORT_CHANGES.LABEL_NAME');
          case 'location':
            return this.i18n.instant('FORT_CHANGES.LABEL_LOCATION');
          case 'image_url':
            return this.i18n.instant('FORT_CHANGES.LABEL_IMAGE');
          case 'removal':
            return this.i18n.instant('FORT_CHANGES.LABEL_REMOVAL');
          case 'new':
            return this.i18n.instant('FORT_CHANGES.LABEL_NEW');
          default:
            return t;
        }
      })
      .join(', ');
  }

  formatDistance(meters: number): string {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${meters} m`;
  }

  formatFortType(type: string | null): string {
    switch (type) {
      case 'pokestop':
        return this.i18n.instant('FORT_CHANGES.FORT_POKESTOP');
      case 'gym':
        return this.i18n.instant('FORT_CHANGES.FORT_GYM');
      default:
        return this.i18n.instant('FORT_CHANGES.FORT_EVERYTHING');
    }
  }

  loadItems(): void {
    this.loading.set(true);
    this.fortChangeService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: items => {
          this.fortChanges.set(items);
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
      .open(FortChangeAddDialogComponent, { width: '600px', maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadItems();
      });
  }

  selectAll(): void {
    const ids = new Set(this.fortChanges().map(i => i.uid));
    this.selectedIds.set(ids);
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
        this.fortChangeService.updateAllDistance(distance).subscribe({
          error: () =>
            this.snackBar.open(this.i18n.instant('FORT_CHANGES.SNACK_FAILED_DISTANCE'), this.i18n.instant('COMMON.OK'), { duration: 3000 }),
          next: () => {
            this.snackBar.open(this.i18n.instant('FORT_CHANGES.SNACK_ALL_DISTANCE'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
            this.loadItems();
          },
        });
      }
    });
  }

  private loadProfileAreas(): void {
    this.areaService.getSelected().subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
  }
}
