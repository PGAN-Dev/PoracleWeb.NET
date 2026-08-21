import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
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

import { NestAddDialogComponent } from './nest-add-dialog.component';
import { NestEditDialogComponent } from './nest-edit-dialog.component';
import { Nest } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { NestService } from '../../core/services/nest.service';
import { SettingsService } from '../../core/services/settings.service';
import { TestAlertService } from '../../core/services/test-alert.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';
import { FeatureReadonlyBannerComponent } from '../../shared/components/feature-readonly-banner/feature-readonly-banner.component';
import { WhereChipComponent } from '../../shared/components/where-chip/where-chip.component';
import { WhereSheetComponent, WhereSheetData } from '../../shared/components/where-sheet/where-sheet.component';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { isAutoDelete as cleanIsAutoDelete } from '../../shared/utils/clean-flags';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FeatureReadonlyBannerComponent,
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
  selector: 'app-nest-list',
  standalone: true,
  styleUrl: './nest-list.component.scss',
  templateUrl: './nest-list.component.html',
})
export class NestListComponent implements OnInit {
  private readonly areaService = inject(AreaService);

  private readonly destroyRef = inject(DestroyRef);

  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly nestService = inject(NestService);
  private readonly settingsService = inject(SettingsService);
  private readonly snackBar = inject(MatSnackBar);
  readonly loading = signal(true);
  readonly nests = signal<Nest[]>([]);
  /** Only used to word the inherited scope honestly; empty produces the more cautious wording. */
  readonly profileAreas = signal<string[]>([]);
  readonly selectedIds = signal(new Set<number>());
  readonly selectMode = signal(false);

  readonly testAlertService = inject(TestAlertService);

  /** True while this alarm type is switched off: the page reads and deletes, but cannot create or edit. */
  readonly writesDisabled = computed(() => this.settingsService.isDisabled('disable_nests'));

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('POKEMON.CONFIRM_BULK_DELETE_TITLE'),
        message: this.i18n.instant('POKEMON.CONFIRM_BULK_DELETE_MSG', { count: this.selectedIds().size }),
        title: this.i18n.instant('POKEMON.CONFIRM_BULK_DELETE_TITLE'),
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
          await firstValueFrom(this.nestService.delete(uid));
          deleted++;
        } catch {
          // Already gone, which is what the user asked for.
        }
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadNests();
      this.snackBar.open(this.i18n.instant('POKEMON.SNACK_BULK_DELETED', { count: deleted }), this.i18n.instant('COMMON.OK'), {
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
        await firstValueFrom(this.nestService.updateBulkDistance(uids, distance));
      } catch (err) {
        const message = (err as { error?: { error?: string } })?.error?.error;
        this.snackBar.open(message ?? this.i18n.instant('NESTS.SNACK_FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
          duration: 5000,
        });
        return;
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadNests();
      this.snackBar.open(this.i18n.instant('POKEMON.SNACK_BULK_DISTANCE', { count: uids.length }), this.i18n.instant('COMMON.OK'), {
        duration: 3000,
      });
    }
  }

  deleteAll(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
          message: this.i18n.instant('POKEMON.CONFIRM_DELETE_ALL_MSG'),
          title: this.i18n.instant('NESTS.PAGE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.nestService.deleteAll().subscribe({
            error: () =>
              this.snackBar.open(this.i18n.instant('NESTS.SNACK_FAILED_DELETE'), this.i18n.instant('COMMON.OK'), { duration: 3000 }),
            next: () => {
              this.snackBar.open(this.i18n.instant('NESTS.SNACK_DELETED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadNests();
            },
          });
      });
  }

  deleteNest(nest: Nest): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE'),
          message: this.i18n.instant('POKEMON.CONFIRM_DELETE_MSG', { name: this.getPokemonName(nest.pokemonId) }),
          title: this.i18n.instant('NESTS.CONFIRM_DELETE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.nestService.delete(nest.uid).subscribe({
            error: () =>
              this.snackBar.open(this.i18n.instant('NESTS.SNACK_FAILED_DELETE'), this.i18n.instant('COMMON.OK'), { duration: 3000 }),
            next: () => {
              this.snackBar.open(this.i18n.instant('NESTS.SNACK_DELETED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadNests();
            },
          });
      });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editNest(nest: Nest): void {
    this.dialog
      .open(NestEditDialogComponent, { width: '600px', data: nest, maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadNests();
      });
  }

  /** Change one alarm's delivery scope from its card, without opening the whole edit dialog. */
  editScope(item: Nest): void {
    if (this.writesDisabled()) {
      // The chip stays visible because it says something worth reading; editing it is a write, and
      // the API refuses those while the type is disabled. Say so rather than no-op silently.
      this.snackBar.open(this.i18n.instant('ALARM.READ_ONLY_TOAST'), this.i18n.instant('TOAST.OK'), { duration: 4000 });
      return;
    }

    const data: WhereSheetData = {
      profileAreas: this.profileAreas(),
      scope: scopeOf(item.overrideLocationLabel, item.overrideAreas, item.distance),
    };

    this.dialog
      .open(WhereSheetComponent, { width: '520px', autoFocus: false, data })
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;

        this.nestService.update(item.uid, scopeToFields(scope)).subscribe({
          error: () => this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), { duration: 4000 }),
          next: () => {
            this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVED'), this.i18n.instant('COMMON.OK'), { duration: 2500 });
            this.loadNests();
          },
        });
      });
  }

  formatDistance(meters: number): string {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${meters} m`;
  }

  getPokemonImage(pokemonId: number): string {
    return this.iconService.getPokemonUrl(pokemonId);
  }

  getPokemonName(id: number): string {
    return this.masterData.getPokemonName(id);
  }

  /** True when the auto-delete bit (clean bit 1) is set, ignoring the edit-in-place / summary bits. */
  isAutoDelete(clean: number): boolean {
    return cleanIsAutoDelete(clean);
  }

  loadNests(): void {
    this.loading.set(true);
    this.nestService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: n => {
          this.nests.set(n);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.loadProfileAreas();
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadNests();
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  openAddDialog(): void {
    this.dialog
      .open(NestAddDialogComponent, { width: '600px', maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadNests();
      });
  }

  selectAll(): void {
    const ids = new Set(this.nests().map(i => i.uid));
    this.selectedIds.set(ids);
  }

  sendTestAlert(nest: Nest): void {
    this.testAlertService.sendTestAlert('nest', nest.uid);
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
        this.nestService.updateAllDistance(distance).subscribe({
          error: () =>
            this.snackBar.open(this.i18n.instant('POKEMON.SNACK_FAILED_DISTANCE'), this.i18n.instant('COMMON.OK'), { duration: 3000 }),
          next: () => {
            this.snackBar.open(this.i18n.instant('POKEMON.SNACK_ALL_DISTANCE'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
            this.loadNests();
          },
        });
      }
    });
  }

  private loadProfileAreas(): void {
    this.areaService.getSelected().subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
  }
}
