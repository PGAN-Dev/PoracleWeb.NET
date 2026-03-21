import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';

import { GeofenceData, GeofenceRegion, UserGeofence } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { UserGeofenceService } from '../../core/services/user-geofence.service';
import { AreaMapComponent } from '../../shared/components/area-map/area-map.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  GeofenceNameDialogComponent,
  GeofenceNameDialogData,
  GeofenceNameDialogResult,
} from '../../shared/components/geofence-name-dialog/geofence-name-dialog.component';
import { detectRegion } from '../../shared/utils/geo.utils';

const MAX_CUSTOM_GEOFENCES = 10;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule,
    AreaMapComponent,
  ],
  selector: 'app-geofence-list',
  standalone: true,
  styleUrl: './geofence-list.component.scss',
  templateUrl: './geofence-list.component.html',
})
export class GeofenceListComponent implements OnInit {
  private readonly areaService = inject(AreaService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly rawGeofenceData = signal<GeofenceData[]>([]);
  private readonly snackBar = inject(MatSnackBar);
  private readonly userGeofenceService = inject(UserGeofenceService);

  readonly customGeofences = signal<UserGeofence[]>([]);
  readonly customGeofenceMapData = computed((): GeofenceData[] => {
    return this.customGeofences()
      .filter((g): g is UserGeofence & { polygon: [number, number][] } => !!g.polygon && g.polygon.length > 0)
      .map(g => ({
        id: g.id,
        name: g.displayName,
        path: g.polygon,
      }));
  });

  readonly customGeofencesLoading = signal(false);
  readonly drawMode = signal(false);

  readonly geofenceData = computed(() => this.rawGeofenceData());

  readonly geofenceLimitText = computed(() => `${this.customGeofences().length} of ${MAX_CUSTOM_GEOFENCES}`);
  readonly geofenceRegions = signal<GeofenceRegion[]>([]);

  readonly hasReachedLimit = computed(() => this.customGeofences().length >= MAX_CUSTOM_GEOFENCES);

  readonly maxCustomGeofences = MAX_CUSTOM_GEOFENCES;

  /** Build region data for auto-detection from raw geofence data */
  readonly regionDetectionData = computed(() => {
    const regions = this.geofenceRegions();
    const raw = this.rawGeofenceData();
    return regions
      .map(r => {
        const fence = raw.find(f => f.name.toLowerCase() === r.name.toLowerCase());
        return fence ? { id: r.id, name: r.name, displayName: r.displayName, path: fence.path } : null;
      })
      .filter((r): r is NonNullable<typeof r> => r !== null);
  });

  readonly savingGeofence = signal(false);
  readonly skeletonGeofences = Array.from({ length: 3 });

  async deleteGeofence(geofence: UserGeofence): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete',
        message: `Are you sure you want to delete "${geofence.displayName}"? This geofence will be removed from all alarms using it.`,
        title: 'Delete Custom Geofence',
        warn: true,
      } as ConfirmDialogData,
    });
    const confirmed = await firstValueFrom(ref.afterClosed());
    if (confirmed) {
      this.userGeofenceService
        .deleteGeofence(geofence.id)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          error: () => this.snackBar.open('Failed to delete geofence', 'OK', { duration: 3000 }),
          next: () => {
            this.customGeofences.update(list => list.filter(g => g.id !== geofence.id));
            this.snackBar.open('Geofence deleted', 'OK', { duration: 3000 });
          },
        });
    }
  }

  editGeofence(geofence: UserGeofence): void {
    const ref = this.dialog.open(GeofenceNameDialogComponent, {
      width: '440px',
      data: {
        detectedRegion: null,
        regions: this.geofenceRegions(),
      } as GeofenceNameDialogData,
    });
    // Pre-fill dialog with existing values
    const instance = ref.componentInstance;
    instance.displayName = geofence.displayName;
    const region = this.geofenceRegions().find(r => r.name === geofence.groupName);
    if (region) {
      instance.selectedRegionId = region.id;
    }

    ref.afterClosed().subscribe((result: GeofenceNameDialogResult | null) => {
      if (!result) return;

      // Edit = delete + recreate
      this.savingGeofence.set(true);
      this.userGeofenceService
        .deleteGeofence(geofence.id)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          error: () => {
            this.savingGeofence.set(false);
            this.snackBar.open('Failed to update geofence', 'OK', { duration: 3000 });
          },
          next: () => {
            this.userGeofenceService
              .createGeofence({
                displayName: result.displayName,
                groupName: result.groupName,
                parentId: result.parentId,
                polygon: geofence.polygon ?? [],
              })
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe({
                error: () => {
                  this.savingGeofence.set(false);
                  this.snackBar.open('Failed to update geofence', 'OK', { duration: 3000 });
                },
                next: created => {
                  this.savingGeofence.set(false);
                  this.customGeofences.update(list => [
                    ...list.filter(g => g.id !== geofence.id),
                    created,
                  ]);
                  this.snackBar.open('Geofence updated', 'OK', { duration: 3000 });
                },
              });
          },
        });
    });
  }

  async submitGeofence(geofence: UserGeofence): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Submit',
        message: 'Submit this geofence for admin review? If approved, it will become available to all users.',
        title: 'Submit for Review',
      } as ConfirmDialogData,
    });
    const confirmed = await firstValueFrom(ref.afterClosed());
    if (confirmed) {
      this.userGeofenceService
        .submitForReview(geofence.kojiName)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          error: () => this.snackBar.open('Failed to submit geofence', 'OK', { duration: 3000 }),
          next: updated => {
            this.customGeofences.update(list => list.map(g => (g.id === geofence.id ? updated : g)));
            this.snackBar.open('Geofence submitted for review', 'OK', { duration: 3000 });
          },
        });
    }
  }

  ngOnInit(): void {
    this.loadCustomGeofences();
    this.loadRegions();
    this.loadGeofencePolygons();
  }

  onDrawComplete(polygon: [number, number][]): void {
    this.drawMode.set(false);

    if (polygon.length < 3) {
      this.snackBar.open('A geofence needs at least 3 points', 'OK', { duration: 3000 });
      return;
    }

    const detected = detectRegion(polygon, this.regionDetectionData());

    const ref = this.dialog.open(GeofenceNameDialogComponent, {
      width: '440px',
      data: {
        detectedRegion: detected,
        regions: this.geofenceRegions(),
      } as GeofenceNameDialogData,
    });

    ref.afterClosed().subscribe((result: GeofenceNameDialogResult | null) => {
      if (!result) return;

      this.savingGeofence.set(true);
      this.userGeofenceService
        .createGeofence({
          displayName: result.displayName,
          groupName: result.groupName,
          parentId: result.parentId,
          polygon,
        })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          error: () => {
            this.savingGeofence.set(false);
            this.snackBar.open('Failed to save geofence', 'OK', { duration: 3000 });
          },
          next: created => {
            this.savingGeofence.set(false);
            this.customGeofences.update(list => [...list, created]);
            this.snackBar.open(`Geofence "${created.displayName}" created`, 'OK', { duration: 3000 });
          },
        });
    });
  }

  toggleDrawMode(): void {
    if (this.hasReachedLimit() && !this.drawMode()) {
      this.snackBar.open(`Maximum of ${MAX_CUSTOM_GEOFENCES} custom geofences reached`, 'OK', { duration: 3000 });
      return;
    }
    this.drawMode.update(v => !v);
  }

  private loadCustomGeofences(): void {
    this.customGeofencesLoading.set(true);
    this.userGeofenceService
      .getCustomGeofences()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.customGeofencesLoading.set(false),
        next: geofences => {
          this.customGeofences.set(geofences);
          this.customGeofencesLoading.set(false);
        },
      });
  }

  private loadGeofencePolygons(): void {
    this.areaService
      .getGeofencePolygons()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {},
        next: data => this.rawGeofenceData.set(data),
      });
  }

  private loadRegions(): void {
    this.userGeofenceService
      .getRegions()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {},
        next: regions => this.geofenceRegions.set(regions),
      });
  }
}
