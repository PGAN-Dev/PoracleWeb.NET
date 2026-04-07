import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';

import { AreaDefinition, GeoJsonImportResult, GeofenceData, GeofenceRegion, UserGeofence } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { UserGeofenceService } from '../../core/services/user-geofence.service';
import { AreaMapComponent } from '../../shared/components/area-map/area-map.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  GeofenceNameDialogComponent,
  GeofenceNameDialogData,
  GeofenceNameDialogResult,
} from '../../shared/components/geofence-name-dialog/geofence-name-dialog.component';
import {
  GeoJsonExportDialogComponent,
  GeoJsonExportDialogData,
  GeoJsonExportDialogResult,
} from '../../shared/components/geojson-export-dialog/geojson-export-dialog.component';
import {
  GeoJsonImportDialogComponent,
  GeoJsonImportDialogData,
} from '../../shared/components/geojson-import-dialog/geojson-import-dialog.component';
import { RegionOption } from '../../shared/components/region-selector/region-selector.component';
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
    MatSlideToggleModule,
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

  readonly activeAreas = signal<string[]>([]);
  readonly activeAreaSet = computed(() => new Set(this.activeAreas()));
  readonly availableAreas = signal<AreaDefinition[]>([]);
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
  readonly geofenceData = computed(() => {
    const available = this.availableAreas();
    const raw = this.rawGeofenceData();
    if (available.length === 0) return [];
    const accessibleNames = new Set(available.map(a => a.name));
    return raw.filter(g => accessibleNames.has(g.name));
  });

  readonly geofenceLimitText = computed(() => `${this.customGeofences().length} of ${MAX_CUSTOM_GEOFENCES}`);

  readonly geofenceRegions = signal<GeofenceRegion[]>([]);
  readonly groupMapping = computed(() => {
    const map = new Map<string, string>();
    for (const area of this.availableAreas()) {
      map.set(area.name, area.group ?? '');
    }
    return map;
  });

  readonly hasReachedLimit = computed(() => this.customGeofences().length >= MAX_CUSTOM_GEOFENCES);

  readonly maxCustomGeofences = MAX_CUSTOM_GEOFENCES;

  /** Build region data for auto-detection from region polygons */
  readonly regionDetectionData = computed(() => {
    return this.geofenceRegions()
      .filter(r => r.polygon && r.polygon.length > 0)
      .map(r => ({ id: r.id, name: r.name, displayName: r.displayName, path: r.polygon! }));
  });

  readonly savingGeofence = signal(false);
  readonly selectedMapRegion = signal<{ id: number; name: string; displayName: string } | null>(null);
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
            this.activeAreas.update(areas => areas.filter(a => a !== geofence.kojiName));
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
                  this.customGeofences.update(list => [...list.filter(g => g.id !== geofence.id), created]);
                  this.activeAreas.update(areas => [...areas.filter(a => a !== geofence.kojiName), created.kojiName]);
                  this.snackBar.open('Geofence updated', 'OK', { duration: 3000 });
                },
              });
          },
        });
    });
  }

  exportGeoJson(): void {
    const geofences = this.customGeofences().filter(g => g.polygon && g.polygon.length > 0);
    if (geofences.length === 0) {
      this.snackBar.open('No geofences to export', 'OK', { duration: 3000 });
      return;
    }

    const ref = this.dialog.open(GeoJsonExportDialogComponent, {
      width: '480px',
      data: { geofences } as GeoJsonExportDialogData,
    });

    ref.afterClosed().subscribe((result: GeoJsonExportDialogResult | null) => {
      if (!result || result.selected.length === 0) return;
      this.downloadGeoJson(result.selected);
    });
  }

  ngOnInit(): void {
    this.loadActiveAreas();
    this.loadCustomGeofences();
    this.loadRegions();
    this.loadGeofencePolygons();
    this.loadAvailableAreas();
  }

  onDrawComplete(polygon: [number, number][]): void {
    this.drawMode.set(false);

    if (polygon.length < 3) {
      this.snackBar.open('A geofence needs at least 3 points', 'OK', { duration: 3000 });
      return;
    }

    // Use centroid-based auto-detection first, fall back to map's selected region
    const detected = detectRegion(polygon, this.regionDetectionData()) ?? this.selectedMapRegion();

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
            this.activeAreas.update(areas => [...areas, created.kojiName]);
            this.snackBar.open(`Geofence "${created.displayName}" created`, 'OK', { duration: 3000 });
          },
        });
    });
  }

  onMapRegionChanged(option: RegionOption): void {
    if (!option.label) {
      this.selectedMapRegion.set(null);
      return;
    }
    // Find the matching GeofenceRegion to get the id
    const region = this.geofenceRegions().find(r => r.displayName === option.label || r.name === option.label);
    if (region) {
      this.selectedMapRegion.set({ id: region.id, name: region.name, displayName: region.displayName });
    }
  }

  openImportDialog(): void {
    const ref = this.dialog.open(GeoJsonImportDialogComponent, {
      width: '600px',
      data: {
        currentGeofenceCount: this.customGeofences().length,
        existingNames: this.customGeofences().map(g => g.displayName),
        maxGeofences: MAX_CUSTOM_GEOFENCES,
        regions: this.geofenceRegions(),
      } as GeoJsonImportDialogData,
    });

    ref.afterClosed().subscribe((result: GeoJsonImportResult | null) => {
      if (result && result.created.length > 0) {
        this.loadCustomGeofences();
        this.loadActiveAreas();
        this.snackBar.open(`Imported ${result.created.length} geofence(s)`, 'OK', { duration: 3000 });
      }
    });
  }

  async submitGeofence(geofence: UserGeofence): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Submit for Review',
        message: `This will send "${geofence.displayName}" to the admin team for review. If approved, your geofence will be promoted to a public area that all users can select and receive notifications from. Your private geofence will continue to work while the review is pending.`,
        title: 'Submit Geofence for Public Review',
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

  toggleDrawMode(): void {
    if (this.hasReachedLimit() && !this.drawMode()) {
      this.snackBar.open(`Maximum of ${MAX_CUSTOM_GEOFENCES} custom geofences reached`, 'OK', { duration: 3000 });
      return;
    }
    this.drawMode.update(v => !v);
  }

  toggleGeofenceProfile(geofence: UserGeofence): void {
    const active = this.activeAreaSet().has(geofence.kojiName);
    const action$ = active
      ? this.userGeofenceService.deactivateGeofence(geofence.id)
      : this.userGeofenceService.activateGeofence(geofence.id);

    // Optimistic update
    if (active) {
      this.activeAreas.update(areas => areas.filter(a => a !== geofence.kojiName));
    } else {
      this.activeAreas.update(areas => [...areas, geofence.kojiName]);
    }

    action$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      error: () => {
        // Revert on error
        if (active) {
          this.activeAreas.update(areas => [...areas, geofence.kojiName]);
        } else {
          this.activeAreas.update(areas => areas.filter(a => a !== geofence.kojiName));
        }
        this.snackBar.open(`Failed to ${active ? 'deactivate' : 'activate'} geofence`, 'OK', { duration: 3000 });
      },
      next: () => {
        this.snackBar.open(active ? 'Geofence deactivated for this profile' : 'Geofence activated for this profile', 'OK', {
          duration: 3000,
        });
      },
    });
  }

  private downloadGeoJson(geofences: UserGeofence[]): void {
    const features = geofences
      .filter(g => g.polygon && g.polygon.length > 0)
      .map(g => {
        // Convert [lat, lon] to GeoJSON [lon, lat] and close ring
        const ring = g.polygon!.map(([lat, lon]) => [lon, lat]);
        if (ring.length >= 2 && (ring[0][0] !== ring[ring.length - 1][0] || ring[0][1] !== ring[ring.length - 1][1])) {
          ring.push([ring[0][0], ring[0][1]]);
        }
        return {
          geometry: { coordinates: [ring], type: 'Polygon' },
          properties: { name: g.displayName, group: g.groupName },
          type: 'Feature',
        };
      });

    const geoJson = JSON.stringify({ features, type: 'FeatureCollection' }, null, 2);
    const blob = new Blob([geoJson], { type: 'application/geo+json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'geofences.geojson';
    a.click();
    URL.revokeObjectURL(url);
    this.snackBar.open(`Exported ${features.length} geofence(s)`, 'OK', { duration: 3000 });
  }

  private loadActiveAreas(): void {
    this.areaService
      .getSelected()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {},
        next: areas => this.activeAreas.set(areas),
      });
  }

  private loadAvailableAreas(): void {
    this.areaService
      .getAvailable()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {},
        next: areas => this.availableAreas.set(areas.filter(a => a.userSelectable !== false)),
      });
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
