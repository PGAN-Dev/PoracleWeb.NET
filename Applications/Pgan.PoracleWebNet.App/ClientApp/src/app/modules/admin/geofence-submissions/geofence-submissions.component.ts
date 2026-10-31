import { DatePipe, DecimalPipe } from '@angular/common';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  NgZone,
  OnDestroy,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import * as L from 'leaflet';
import { firstValueFrom } from 'rxjs';

import { GeofenceAlarmsChoice, GeofenceData, GeofenceDeleteImpact, GeofenceRegion, UserGeofence } from '../../../core/models';
import { AdminGeofenceService } from '../../../core/services/admin-geofence.service';
import { AreaService } from '../../../core/services/area.service';
import { BasemapService } from '../../../core/services/basemap.service';
import { I18nService } from '../../../core/services/i18n.service';
import { UserGeofenceService } from '../../../core/services/user-geofence.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  GeofenceApprovalDialogComponent,
  GeofenceApprovalDialogData,
  GeofenceApprovalDialogResult,
} from '../../../shared/components/geofence-approval-dialog/geofence-approval-dialog.component';
import {
  GeofenceDeleteDialogComponent,
  GeofenceDeleteDialogData,
} from '../../../shared/components/geofence-delete-dialog/geofence-delete-dialog.component';
import { GeofenceDetailDialogComponent } from '../../../shared/components/geofence-detail-dialog/geofence-detail-dialog.component';
import { pointInFence, polygonAreaSqKm, polygonCentroid } from '../../../shared/utils/geo.utils';
import { GEOFENCE_STATUS_COLORS } from '../../../shared/utils/geofence.utils';
import { serverReason } from '../../../shared/utils/server-reason';

/** Koji refusing an approve, reject or delete is a 502 whose reason names the likely cause (a deleted region). */
const KOJI_REFUSAL = { allow5xx: [502] } as const;

/** The statuses the table below the review queue can be narrowed to. Pending fences live in the queue. */
export type OtherFilter = 'active' | 'all' | 'approved' | 'rejected';
export type SortField = 'name' | 'status' | 'owner' | 'region' | 'points' | 'created' | 'submitted';
export type SortDirection = 'asc' | 'desc';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    DecimalPipe,
    MatButtonModule,
    MatButtonToggleModule,
    MatDialogModule,
    MatIconModule,
    MatSnackBarModule,
    MatTooltipModule,
    TranslatePipe,
  ],
  selector: 'app-geofence-submissions',
  standalone: true,
  styleUrl: './geofence-submissions.component.scss',
  templateUrl: './geofence-submissions.component.html',
})
export class GeofenceSubmissionsComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly adminGeofenceService = inject(AdminGeofenceService);
  private readonly areaService = inject(AreaService);
  private readonly basemap = inject(BasemapService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly elementRef = inject(ElementRef);
  private readonly i18n = inject(I18nService);
  private readonly mapInstances = new Map<number, L.Map>();
  private readonly ngZone = inject(NgZone);

  private observer: IntersectionObserver | null = null;
  private readonly referenceGeofences = signal<GeofenceData[]>([]);
  private readonly regions = signal<GeofenceRegion[]>([]);
  private readonly snackBar = inject(MatSnackBar);
  /** The pending tick that re-observes the queue's maps; cleared on destroy so it can't run on a dead view. */
  private thumbnailTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly userGeofenceService = inject(UserGeofenceService);

  /** Narrows the table of fences that are not waiting for review. */
  readonly activeFilter = signal<OtherFilter>('all');
  readonly allGeofences = signal<UserGeofence[]>([]);

  /** Everything but the queue, narrowed by {@link activeFilter} and sorted by the table header. */
  readonly filteredGeofences = computed(() => {
    const filter = this.activeFilter();
    const others = this.allGeofences().filter(g => g.status !== 'pending_review');
    return this.applySorting(filter === 'all' ? others : others.filter(g => g.status === filter));
  });

  readonly loading = signal(true);

  /** The review queue: oldest submission first, because it has waited longest. */
  readonly pendingGeofences = computed(() =>
    this.allGeofences()
      .filter(g => g.status === 'pending_review')
      .sort((a, b) => (a.submittedAt ?? a.createdAt ?? '').localeCompare(b.submittedAt ?? b.createdAt ?? '')),
  );

  /**
   * The public area each queued fence's centre falls inside, if any: the same test the Discord review post
   * makes (`FindContainingPublicAreaAsync`), so the page and the post name the same overlap.
   */
  readonly overlaps = computed(() => {
    const reference = this.referenceGeofences();
    const result = new Map<number, string>();
    for (const g of this.pendingGeofences()) {
      if (!g.polygon?.length) continue;
      const centre = polygonCentroid(g.polygon);
      const own = [g.kojiName, g.promotedName].filter(Boolean).map(n => n!.toLowerCase());
      const hit = reference.find(r => !own.includes(r.name.toLowerCase()) && pointInFence(centre, r));
      if (hit) result.set(g.id, hit.name);
    }
    return result;
  });

  readonly sortDirection = signal<SortDirection>('asc');

  readonly sortField = signal<SortField>('name');

  readonly statusCounts = computed(() => {
    const all = this.allGeofences();
    const pending = all.filter(g => g.status === 'pending_review').length;
    return {
      active: all.filter(g => g.status === 'active').length,
      all: all.length,
      approved: all.filter(g => g.status === 'approved').length,
      others: all.length - pending,
      pending_review: pending,
      rejected: all.filter(g => g.status === 'rejected').length,
    };
  });

  constructor() {
    // Maps are drawn only for the queue. When it changes, destroy the maps of rows that left it and
    // observe the new ones once Angular has rendered them.
    effect(() => {
      const queued = this.pendingGeofences();
      if (this.thumbnailTimer !== null) clearTimeout(this.thumbnailTimer);
      this.thumbnailTimer = setTimeout(() => {
        this.thumbnailTimer = null;
        const queuedIds = new Set(queued.map(g => g.id));
        for (const [id, map] of this.mapInstances) {
          if (!queuedIds.has(id)) {
            map.remove();
            this.mapInstances.delete(id);
          }
        }
        this.observeMapContainers();
      }, 0);
    });
  }

  /** The same choice the owner gets: asked only when deleting would widen some of the owner's alarms. */
  async adminDelete(geofence: UserGeofence): Promise<void> {
    const name = geofence.displayName;
    const owner = geofence.ownerName ?? geofence.humanId;
    const failed = (err?: unknown) =>
      this.snackBar.open(
        serverReason(err, KOJI_REFUSAL) ?? this.i18n.instant('ADMIN.SNACK_FAILED_DELETE_GEOFENCE'),
        this.i18n.instant('TOAST.OK'),
        {
          duration: 4000,
        },
      );

    let impact: GeofenceDeleteImpact;
    try {
      impact = await firstValueFrom(this.adminGeofenceService.getDeleteImpact(geofence.id));
    } catch (err) {
      failed(err);
      return;
    }

    let alarms: GeofenceAlarmsChoice;
    if (impact.total > 0) {
      const ref = this.dialog.open<GeofenceDeleteDialogComponent, GeofenceDeleteDialogData, GeofenceAlarmsChoice | null>(
        GeofenceDeleteDialogComponent,
        { data: { name, count: impact.total, owner } },
      );
      const choice = await firstValueFrom(ref.afterClosed());
      if (!choice) return;
      alarms = choice;
    } else {
      const ref = this.dialog.open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE'),
          message: this.i18n.instant('ADMIN.CONFIRM_DELETE_GEOFENCE', { name, owner }),
          title: this.i18n.instant('ADMIN.ADMIN_DELETE_GEOFENCE'),
          warn: true,
        } as ConfirmDialogData,
      });
      if (!(await firstValueFrom(ref.afterClosed()))) return;
      // Nothing was limited to the fence when asked. keep, so a rule scoped to it since then is not deleted
      // without anyone having chosen to.
      alarms = 'keep';
    }

    this.adminGeofenceService
      .adminDelete(geofence.id, alarms)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: failed,
        next: () => {
          this.destroyMap(geofence.id);
          this.allGeofences.update(list => list.filter(g => g.id !== geofence.id));
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_GEOFENCE_DELETED', { name }), this.i18n.instant('TOAST.OK'), {
            duration: 3000,
          });
        },
      });
  }

  /** Square kilometres, from the polygon; null when there is none to measure. */
  areaSqKm(geofence: UserGeofence): null | number {
    return geofence.polygon && geofence.polygon.length > 2 ? polygonAreaSqKm(geofence.polygon) : null;
  }

  /**
   * Whether the Review action applies, matching what the API will accept.
   */
  /* UserGeofenceService.ApprovableStatuses is { pending_review, rejected } -- an admin reconsidering a
   * rejection is the intended case. All three view modes gated on pending_review alone, so a
   * mistaken rejection was irreversible from the SPA while the endpoint would have taken it. See #649. */
  canReview(geofence: { status: string }): boolean {
    return geofence.status === 'pending_review' || geofence.status === 'rejected';
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
    if (this.thumbnailTimer !== null) clearTimeout(this.thumbnailTimer);
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
    this.userGeofenceService
      .getRegions()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ error: () => {}, next: regions => this.regions.set(regions) });
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

  /** `mode` opens the dialog on Approve or Reject, matching the button the admin pressed. */
  openReviewDialog(geofence: UserGeofence, mode: 'approve' | 'reject' = 'approve'): void {
    const ref = this.dialog.open(GeofenceApprovalDialogComponent, {
      width: '480px',
      data: { geofence, mode, regions: this.regions() } as GeofenceApprovalDialogData,
    });

    ref
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result: GeofenceApprovalDialogResult | null) => {
        if (!result) return;

        if (result.action === 'approve') {
          this.adminGeofenceService
            .approveSubmission(geofence.id, {
              groupName: result.groupName,
              parentId: result.parentId,
              promotedName: result.promotedName,
            })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              error: err =>
                this.snackBar.open(
                  serverReason(err, KOJI_REFUSAL) ?? this.i18n.instant('ADMIN.SNACK_FAILED_APPROVE'),
                  this.i18n.instant('TOAST.OK'),
                  {
                    duration: 4000,
                  },
                ),
              next: updated => {
                this.allGeofences.update(list => list.map(g => (g.id === geofence.id ? updated : g)));
                // Koji has the fence but the owner's areas still hold the old name. Say so, or the owner
                // is silently unsubscribed from the area they just had approved.
                if (updated.ownerAreasRenamed === false) {
                  this.snackBar.open(this.i18n.instant('ADMIN.SNACK_APPROVED_AREAS_NOT_MOVED'), this.i18n.instant('TOAST.OK'), {
                    duration: 8000,
                  });
                  return;
                }
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
              error: err =>
                this.snackBar.open(
                  serverReason(err, KOJI_REFUSAL) ?? this.i18n.instant('ADMIN.SNACK_FAILED_REJECT'),
                  this.i18n.instant('TOAST.OK'),
                  {
                    duration: 4000,
                  },
                ),
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

      this.basemap.attach(map);

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
          // The queue effect re-observes the maps once the new rows render (and on its own timer, which
          // destroy clears).
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
