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
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import * as L from 'leaflet';
import { firstValueFrom } from 'rxjs';

import { GeofenceData, UserGeofence } from '../../../core/models';
import { AdminGeofenceService } from '../../../core/services/admin-geofence.service';
import { AreaService } from '../../../core/services/area.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  GeofenceApprovalDialogComponent,
  GeofenceApprovalDialogData,
  GeofenceApprovalDialogResult,
} from '../../../shared/components/geofence-approval-dialog/geofence-approval-dialog.component';
import { GeofenceDetailDialogComponent } from '../../../shared/components/geofence-detail-dialog/geofence-detail-dialog.component';
import { GEOFENCE_STATUS_COLORS } from '../../../shared/utils/geofence.utils';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule,
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
    if (filter === 'all') return all;
    return all.filter(g => g.status === filter);
  });

  readonly loading = signal(true);

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

  constructor() {
    // When the filtered list changes (tab switch), destroy orphaned maps and re-observe
    effect(() => {
      const visible = this.filteredGeofences();
      // Allow Angular to render the new DOM first
      setTimeout(() => {
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
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete',
        message: `Permanently delete "${geofence.displayName}" (owned by ${geofence.humanId})? This will remove it from the user's areas and clean up all associated data.`,
        title: 'Admin Delete Geofence',
        warn: true,
      } as ConfirmDialogData,
    });
    const confirmed = await firstValueFrom(ref.afterClosed());
    if (confirmed) {
      this.adminGeofenceService
        .adminDelete(geofence.id)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          error: () => this.snackBar.open('Failed to delete geofence', 'OK', { duration: 3000 }),
          next: () => {
            this.destroyMap(geofence.id);
            this.allGeofences.update(list => list.filter(g => g.id !== geofence.id));
            this.snackBar.open(`"${geofence.displayName}" deleted`, 'OK', { duration: 3000 });
          },
        });
    }
  }

  getPointCount(geofence: UserGeofence): number {
    return geofence.pointCount ?? geofence.polygon?.length ?? 0;
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
              error: () => this.snackBar.open('Failed to approve submission', 'OK', { duration: 3000 }),
              next: updated => {
                this.allGeofences.update(list => list.map(g => (g.id === geofence.id ? updated : g)));
                this.snackBar.open(`"${geofence.displayName}" approved`, 'OK', { duration: 3000 });
              },
            });
        } else {
          this.adminGeofenceService
            .rejectSubmission(geofence.id, { reviewNotes: result.reviewNotes! })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              error: () => this.snackBar.open('Failed to reject submission', 'OK', { duration: 3000 }),
              next: updated => {
                this.allGeofences.update(list => list.map(g => (g.id === geofence.id ? updated : g)));
                this.snackBar.open(`"${geofence.displayName}" rejected`, 'OK', { duration: 3000 });
              },
            });
        }
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
          this.snackBar.open('Failed to load geofences', 'OK', { duration: 3000 });
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
