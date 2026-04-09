import { DatePipe } from '@angular/common';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  NgZone,
  OnDestroy,
  OnInit,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';
import * as L from 'leaflet';
import { firstValueFrom } from 'rxjs';

import { GeofenceData, UserGeofence } from '../../../core/models';
import { AdminGeofenceService } from '../../../core/services/admin-geofence.service';
import { AreaService } from '../../../core/services/area.service';
import { I18nService } from '../../../core/services/i18n.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  GeofenceApprovalDialogComponent,
  GeofenceApprovalDialogData,
  GeofenceApprovalDialogResult,
} from '../../../shared/components/geofence-approval-dialog/geofence-approval-dialog.component';
import { GeofenceDetailDialogComponent } from '../../../shared/components/geofence-detail-dialog/geofence-detail-dialog.component';
import { GEOFENCE_STATUS_COLORS } from '../../../shared/utils/geofence.utils';

export type ViewMode = 'card' | 'list' | 'table';
export type SortField = 'name' | 'status' | 'owner' | 'region' | 'points' | 'created' | 'submitted';
export type SortDirection = 'asc' | 'desc';

export interface RegionGroup {
  count: number;
  geofences: UserGeofence[];
  name: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    MatButtonModule,
    MatButtonToggleModule,
    MatDialogModule,
    MatExpansionModule,
    MatIconModule,
    MatSnackBarModule,
    MatTooltipModule,
    TranslateModule,
  ],
  selector: 'app-geofence-submissions',
  standalone: true,
  styleUrl: './geofence-submissions.component.scss',
  templateUrl: './geofence-submissions.component.html',
})
export class GeofenceSubmissionsComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly adminGeofenceService = inject(AdminGeofenceService);
  private readonly areaService = inject(AreaService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly elementRef = inject(ElementRef);
  private readonly i18n = inject(I18nService);
  private readonly mapInstances = new Map<number, L.Map>();
  private readonly ngZone = inject(NgZone);

  private observer: IntersectionObserver | null = null;
  private readonly referenceGeofences = signal<GeofenceData[]>([]);
  private readonly snackBar = inject(MatSnackBar);

  readonly activeFilter = signal<string>('all');
  readonly allGeofences = signal<UserGeofence[]>([]);
  readonly filteredGeofences = computed(() => {
    const filter = this.activeFilter();
    const all = this.allGeofences();
    const filtered = filter === 'all' ? all : all.filter(g => g.status === filter);
    return this.applySorting(filtered);
  });

  readonly loading = signal(true);

  readonly regionGroups = computed<RegionGroup[]>(() => {
    const geofences = this.filteredGeofences();
    const groupMap = new Map<string, UserGeofence[]>();

    for (const g of geofences) {
      const key = g.groupName?.trim() || '';
      if (!groupMap.has(key)) {
        groupMap.set(key, []);
      }
      groupMap.get(key)!.push(g);
    }

    const groups: RegionGroup[] = [];
    const noRegionKey = '';

    for (const [key, items] of groupMap) {
      if (key !== noRegionKey) {
        groups.push({ name: key, count: items.length, geofences: items });
      }
    }

    // Sort alphabetically
    groups.sort((a, b) => a.name.localeCompare(b.name));

    // "No Region" last
    const noRegionItems = groupMap.get(noRegionKey);
    if (noRegionItems?.length) {
      groups.push({ name: this.i18n.instant('ADMIN.NO_REGION'), count: noRegionItems.length, geofences: noRegionItems });
    }

    return groups;
  });

  readonly sortDirection = signal<SortDirection>('asc');

  readonly sortField = signal<SortField>('name');

  readonly statusCounts = computed(() => {
    const all = this.allGeofences();
    return {
      active: all.filter(g => g.status === 'active').length,
      all: all.length,
      approved: all.filter(g => g.status === 'approved').length,
      pending_review: all.filter(g => g.status === 'pending_review').length,
      rejected: all.filter(g => g.status === 'rejected').length,
    };
  });

  readonly viewMode = signal<ViewMode>('card');

  constructor() {
    // When the filtered list or view mode changes, destroy orphaned maps and re-observe
    effect(() => {
      const visible = this.filteredGeofences();
      const mode = this.viewMode();
      // Allow Angular to render the new DOM first
      setTimeout(() => {
        if (mode !== 'card') {
          // List and table views have no map containers — destroy all maps so they can be re-created
          for (const [id, map] of this.mapInstances) {
            map.remove();
            this.mapInstances.delete(id);
          }
          return;
        }
        const visibleIds = new Set(visible.map(g => g.id));
        // Destroy maps for cards no longer in the DOM
        for (const [id, map] of this.mapInstances) {
          if (!visibleIds.has(id)) {
            map.remove();
            this.mapInstances.delete(id);
          }
        }
        this.observeMapContainers();
      }, 0);
    });
  }

  async adminDelete(geofence: UserGeofence): Promise<void> {
    const name = geofence.displayName;
    const owner = geofence.ownerName ?? geofence.humanId;
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE'),
        message: this.i18n.instant('ADMIN.CONFIRM_DELETE_GEOFENCE', { name, owner }),
        title: this.i18n.instant('ADMIN.ADMIN_DELETE_GEOFENCE'),
        warn: true,
      } as ConfirmDialogData,
    });
    const confirmed = await firstValueFrom(ref.afterClosed());
    if (confirmed) {
      this.adminGeofenceService
        .adminDelete(geofence.id)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          error: () =>
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_DELETE_GEOFENCE'), this.i18n.instant('TOAST.OK'), { duration: 3000 }),
          next: () => {
            this.destroyMap(geofence.id);
            this.allGeofences.update(list => list.filter(g => g.id !== geofence.id));
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_GEOFENCE_DELETED', { name }), this.i18n.instant('TOAST.OK'), {
              duration: 3000,
            });
          },
        });
    }
  }

  getPointCount(geofence: UserGeofence): number {
    return geofence.pointCount ?? geofence.polygon?.length ?? 0;
  }

  getStatusLabel(status: string): string {
    switch (status) {
      case 'pending_review':
        return this.i18n.instant('ADMIN.GEO_STATUS_PENDING');
      case 'active':
        return this.i18n.instant('ADMIN.STATUS_ACTIVE');
      case 'approved':
        return this.i18n.instant('ADMIN.GEO_STATUS_APPROVED');
      case 'rejected':
        return this.i18n.instant('ADMIN.GEO_STATUS_REJECTED');
      default:
        return status;
    }
  }

  ngAfterViewInit(): void {
    this.setupIntersectionObserver();
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
    this.mapInstances.forEach(map => map.remove());
    this.mapInstances.clear();
  }

  ngOnInit(): void {
    this.loadAll();
    this.areaService
      .getGeofencePolygons()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(geofences => this.referenceGeofences.set(geofences));
  }

  openDetailDialog(geofence: UserGeofence): void {
    this.dialog.open(GeofenceDetailDialogComponent, {
      maxWidth: '90vw',
      width: '720px',
      data: { geofence, referenceGeofences: this.referenceGeofences() },
      maxHeight: '90vh',
      panelClass: 'geofence-detail-dialog-panel',
    });
  }

  openReviewDialog(geofence: UserGeofence): void {
    const ref = this.dialog.open(GeofenceApprovalDialogComponent, {
      width: '480px',
      data: { geofence } as GeofenceApprovalDialogData,
    });

    ref
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result: GeofenceApprovalDialogResult | null) => {
        if (!result) return;

        if (result.action === 'approve') {
          this.adminGeofenceService
            .approveSubmission(geofence.id, { promotedName: result.promotedName })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              error: () =>
                this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_APPROVE'), this.i18n.instant('TOAST.OK'), { duration: 3000 }),
              next: updated => {
                this.allGeofences.update(list => list.map(g => (g.id === geofence.id ? updated : g)));
                this.snackBar.open(
                  this.i18n.instant('ADMIN.SNACK_APPROVED', { name: geofence.displayName }),
                  this.i18n.instant('TOAST.OK'),
                  { duration: 3000 },
                );
              },
            });
        } else {
          this.adminGeofenceService
            .rejectSubmission(geofence.id, { reviewNotes: result.reviewNotes! })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              error: () =>
                this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_REJECT'), this.i18n.instant('TOAST.OK'), { duration: 3000 }),
              next: updated => {
                this.allGeofences.update(list => list.map(g => (g.id === geofence.id ? updated : g)));
                this.snackBar.open(
                  this.i18n.instant('ADMIN.SNACK_REJECTED', { name: geofence.displayName }),
                  this.i18n.instant('TOAST.OK'),
                  { duration: 3000 },
                );
              },
            });
        }
      });
  }

  toggleSort(field: SortField): void {
    if (this.sortField() === field) {
      this.sortDirection.update(d => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortField.set(field);
      this.sortDirection.set('asc');
    }
  }

  private applySorting(geofences: UserGeofence[]): UserGeofence[] {
    const field = this.sortField();
    const dir = this.sortDirection();
    const mul = dir === 'asc' ? 1 : -1;

    return [...geofences].sort((a, b) => {
      let cmp = 0;
      switch (field) {
        case 'name':
          cmp = (a.displayName ?? '').localeCompare(b.displayName ?? '');
          break;
        case 'status':
          cmp = (a.status ?? '').localeCompare(b.status ?? '');
          break;
        case 'owner':
          cmp = (a.ownerName ?? a.humanId ?? '').localeCompare(b.ownerName ?? b.humanId ?? '');
          break;
        case 'region':
          cmp = (a.groupName ?? '').localeCompare(b.groupName ?? '');
          break;
        case 'points':
          cmp = (a.pointCount ?? 0) - (b.pointCount ?? 0);
          break;
        case 'created':
          cmp = (a.createdAt ?? '').localeCompare(b.createdAt ?? '');
          break;
        case 'submitted':
          cmp = (a.submittedAt ?? '').localeCompare(b.submittedAt ?? '');
          break;
      }
      return cmp * mul;
    });
  }

  private destroyMap(geofenceId: number): void {
    const map = this.mapInstances.get(geofenceId);
    if (map) {
      map.remove();
      this.mapInstances.delete(geofenceId);
    }
  }

  private initMapThumbnail(container: HTMLElement, geofence: UserGeofence): void {
    if (!geofence.polygon?.length || this.mapInstances.has(geofence.id)) return;

    this.ngZone.runOutsideAngular(() => {
      const map = L.map(container, {
        attributionControl: false,
        boxZoom: false,
        doubleClickZoom: false,
        dragging: false,
        keyboard: false,
        scrollWheelZoom: false,
        touchZoom: false,
        zoomControl: false,
      });

      L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
        maxZoom: 19,
      }).addTo(map);

      const color = GEOFENCE_STATUS_COLORS[geofence.status] || '#9e9e9e';
      const polygon = L.polygon(
        geofence.polygon!.map(([lat, lng]) => [lat, lng] as L.LatLngTuple),
        {
          color,
          fillColor: color,
          fillOpacity: 0.15,
          weight: 2,
        },
      ).addTo(map);

      map.fitBounds(polygon.getBounds(), { padding: [10, 10] });
      this.mapInstances.set(geofence.id, map);
    });
  }

  private loadAll(): void {
    this.loading.set(true);
    this.adminGeofenceService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_LOAD_GEOFENCES'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: geofences => {
          this.allGeofences.set(geofences);
          this.loading.set(false);
          // Re-observe after data loads
          setTimeout(() => this.observeMapContainers(), 0);
        },
      });
  }

  private observeMapContainers(): void {
    if (!this.observer) return;

    const containers = this.elementRef.nativeElement.querySelectorAll('.map-thumbnail[data-geofence-id]');
    containers.forEach((el: HTMLElement) => this.observer!.observe(el));
  }

  private setupIntersectionObserver(): void {
    this.observer = new IntersectionObserver(
      entries => {
        entries.forEach(entry => {
          if (!entry.isIntersecting) return;

          const container = entry.target as HTMLElement;
          const geofenceId = parseInt(container.dataset['geofenceId'] || '0', 10);
          if (!geofenceId || this.mapInstances.has(geofenceId)) return;

          const geofence = this.allGeofences().find(g => g.id === geofenceId);
          if (geofence) {
            this.initMapThumbnail(container, geofence);
            this.observer!.unobserve(container);
          }
        });
      },
      { rootMargin: '100px' },
    );
  }
}
