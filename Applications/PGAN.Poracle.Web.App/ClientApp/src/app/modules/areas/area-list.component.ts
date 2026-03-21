import { DatePipe, SlicePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';

import { AreaDefinition, GeofenceData, GeofenceRegion, Location, UserGeofence } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { LocationService } from '../../core/services/location.service';
import { UserGeofenceService } from '../../core/services/user-geofence.service';
import { AreaMapComponent } from '../../shared/components/area-map/area-map.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  GeofenceNameDialogComponent,
  GeofenceNameDialogData,
  GeofenceNameDialogResult,
} from '../../shared/components/geofence-name-dialog/geofence-name-dialog.component';
import { LocationDialogComponent } from '../../shared/components/location-dialog/location-dialog.component';
import { detectRegion } from '../../shared/utils/geo.utils';

interface AreaItem {
  group: string;
  name: string;
  selected: boolean;
}

interface GroupInfo {
  name: string;
  selectedCount: number;
  totalCount: number;
}

const MAX_CUSTOM_GEOFENCES = 10;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    SlicePipe,
    FormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatCheckboxModule,
    MatChipsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule,
    AreaMapComponent,
  ],
  selector: 'app-area-list',
  standalone: true,
  styleUrl: './area-list.component.scss',
  templateUrl: './area-list.component.html',
})
export class AreaListComponent implements OnInit {
  private readonly areaService = inject(AreaService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly locationService = inject(LocationService);
  private readonly rawGeofenceData = signal<GeofenceData[]>([]);
  // Saved state (what's in the DB)
  private savedSelection: string[] = [];

  private readonly snackBar = inject(MatSnackBar);

  private readonly userGeofenceService = inject(UserGeofenceService);

  readonly activeGroup = signal<string | null>(null);
  readonly areas = signal<AreaItem[]>([]);
  readonly allGroups = computed((): GroupInfo[] => {
    const all = this.areas();
    const groupMap = new Map<string, { selected: number; total: number }>();
    for (const area of all) {
      const key = area.group || 'Ungrouped';
      if (!groupMap.has(key)) groupMap.set(key, { selected: 0, total: 0 });
      const g = groupMap.get(key)!;
      g.total++;
      if (area.selected) g.selected++;
    }
    return [...groupMap.entries()]
      .map(([name, counts]) => ({ name, selectedCount: counts.selected, totalCount: counts.total }))
      .sort((a, b) => a.name.localeCompare(b.name));
  });

  readonly availableAreas = signal<AreaDefinition[]>([]);

  // Custom geofences
  readonly customGeofences = signal<UserGeofence[]>([]);
  readonly customGeofenceMapData = computed((): GeofenceData[] => {
    return this.customGeofences()
      .filter(g => g.polygonJson)
      .map(g => ({
        id: g.id,
        name: g.displayName,
        path: JSON.parse(g.polygonJson) as [number, number][],
      }));
  });

  readonly customGeofencesLoading = signal(false);

  readonly drawMode = signal(false);
  readonly geofenceData = computed(() => {
    const available = this.availableAreas();
    const raw = this.rawGeofenceData();
    if (available.length === 0) return raw;
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

  groupSearchText = '';

  readonly selectedAreas = signal<string[]>([]);

  readonly hasChanges = computed(() => {
    const current = [...this.selectedAreas()].sort();
    const saved = [...this.savedSelection].sort();
    if (current.length !== saved.length) return true;
    return current.some((v, i) => v !== saved[i]);
  });

  readonly hasMultipleGroups = computed(() => {
    const groups = new Set(this.areas().map(a => a.group));
    return groups.size > 1;
  });

  readonly hasReachedLimit = computed(() => this.customGeofences().length >= MAX_CUSTOM_GEOFENCES);

  readonly loading = signal(true);
  readonly location = signal<Location | null>(null);

  readonly locationAddress = signal<string>('');

  readonly locationMapUrl = signal<string>('');
  manualAreaName = '';

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

  readonly saving = signal(false);
  readonly savingGeofence = signal(false);

  searchText = '';

  readonly skeletonGeofences = Array.from({ length: 3 });

  readonly userLocationForMap = computed(() => {
    const loc = this.location();
    if (loc && loc.latitude && loc.longitude) {
      return { lat: loc.latitude, lng: loc.longitude };
    }
    return undefined;
  });

  readonly viewMode = signal<'map' | 'list'>('map');

  readonly visibleAreas = computed(() => {
    const search = this.searchText.toLowerCase();
    const group = this.activeGroup();
    return this.areas().filter(a => {
      if (search && !a.name.toLowerCase().includes(search)) return false;
      if (group && (a.group || 'Ungrouped') !== group) return false;
      return true;
    });
  });

  addManualArea(): void {
    const name = this.manualAreaName.trim();
    if (!name) return;
    if (!this.selectedAreas().includes(name)) {
      this.selectedAreas.set([...this.selectedAreas(), name]);
    }
    this.manualAreaName = '';
  }

  applyFilter(): void {
    // Triggers visibleAreas recomputation via searchText binding
  }

  cancelChanges(): void {
    const savedSet = new Set(this.savedSelection.map(s => s.toLowerCase()));
    for (const a of this.areas()) {
      a.selected = savedSet.has(a.name.toLowerCase());
    }
    this.selectedAreas.set([...this.savedSelection]);
  }

  clearAllAreas(): void {
    for (const a of this.areas()) a.selected = false;
    this.syncSelectedFromAreas();
  }

  clearLocation(): void {
    this.locationService
      .setLocation({ latitude: 0, longitude: 0 })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.snackBar.open('Failed to clear location', 'OK', { duration: 3000 }),
        next: () => {
          this.location.set(null);
          this.locationAddress.set('');
          this.locationMapUrl.set('');
          this.snackBar.open('Location cleared', 'OK', { duration: 3000 });
        },
      });
  }

  clearSearch(): void {
    this.searchText = '';
  }

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

  deselectAllVisible(): void {
    for (const a of this.visibleAreas()) a.selected = false;
    this.syncSelectedFromAreas();
  }

  editGeofence(geofence: UserGeofence): void {
    const ref = this.dialog.open(GeofenceNameDialogComponent, {
      width: '440px',
      data: {
        detectedRegion: null,
        regions: this.geofenceRegions(),
      } as GeofenceNameDialogData,
    });
    // Pre-fill dialog with existing values -- component sets these after inject
    const instance = ref.componentInstance;
    instance.displayName = geofence.displayName;
    const region = this.geofenceRegions().find(r => r.name === geofence.groupName);
    if (region) {
      instance.selectedRegionId = region.id;
    }

    ref.afterClosed().subscribe((result: GeofenceNameDialogResult | null) => {
      if (!result) return;

      const polygon: [number, number][] = JSON.parse(geofence.polygonJson);

      this.userGeofenceService
        .updateGeofence(geofence.id, {
          displayName: result.displayName,
          groupName: result.groupName,
          parentId: result.parentId,
          polygon,
        })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          error: () => this.snackBar.open('Failed to update geofence', 'OK', { duration: 3000 }),
          next: updated => {
            this.customGeofences.update(list => list.map(g => (g.id === updated.id ? updated : g)));
            this.snackBar.open('Geofence updated', 'OK', { duration: 3000 });
          },
        });
    });
  }

  filteredGroupOptions(): GroupInfo[] {
    const search = this.groupSearchText.toLowerCase();
    const all = this.allGroups();
    if (!search) return all;
    return all.filter(g => g.name.toLowerCase().includes(search));
  }

  ngOnInit(): void {
    this.loadData();
    this.loadCustomGeofences();
    this.loadRegions();
  }

  onDrawComplete(polygon: [number, number][]): void {
    // Exit draw mode
    this.drawMode.set(false);

    if (polygon.length < 3) {
      this.snackBar.open('A geofence needs at least 3 points', 'OK', { duration: 3000 });
      return;
    }

    // Auto-detect which region this polygon is in
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

  onGroupFilterSelected(value: string): void {
    this.activeGroup.set(value || null);
    this.groupSearchText = '';
  }

  onMapAreaClicked(name: string): void {
    if (this.drawMode()) return; // Ignore area clicks during draw mode
    const lowerName = name.toLowerCase();
    const area = this.areas().find(a => a.name.toLowerCase() === lowerName);
    if (area) {
      this.toggleAreaDirect(area);
    }
  }

  openLocationDialog(): void {
    const ref = this.dialog.open(LocationDialogComponent, {
      width: '400px',
      data: this.location(),
    });
    ref.afterClosed().subscribe((result: Location | undefined) => {
      if (result) {
        this.location.set(result);
        this.locationAddress.set('');
        this.locationMapUrl.set('');
        if (result.latitude !== 0 || result.longitude !== 0) {
          this.locationService
            .reverseGeocode(result.latitude, result.longitude)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe(geo => {
              if (geo?.display_name) this.locationAddress.set(geo.display_name);
            });
          this.locationService
            .getStaticMapUrl(result.latitude, result.longitude)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe(map => {
              if (map?.url) this.locationMapUrl.set(map.url);
            });
        }
      }
    });
  }

  removeAreaDirect(name: string): void {
    const lowerName = name.toLowerCase();
    const area = this.areas().find(a => a.name.toLowerCase() === lowerName);
    if (area) {
      area.selected = false;
      this.syncSelectedFromAreas();
    }
  }

  saveAreas(): void {
    this.saving.set(true);
    const selected = this.areas()
      .filter(a => a.selected)
      .map(a => a.name);

    this.areaService
      .update(selected)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.saving.set(false);
          this.snackBar.open('Failed to update areas', 'OK', { duration: 3000 });
        },
        next: () => {
          this.savedSelection = [...selected];
          this.selectedAreas.set(selected);
          this.saving.set(false);
          this.snackBar.open('Areas updated', 'OK', { duration: 3000 });
        },
      });
  }

  selectAllVisible(): void {
    for (const a of this.visibleAreas()) a.selected = true;
    this.syncSelectedFromAreas();
  }

  toggleAreaDirect(area: AreaItem): void {
    area.selected = !area.selected;
    this.syncSelectedFromAreas();
  }

  toggleDrawMode(): void {
    if (this.hasReachedLimit() && !this.drawMode()) {
      this.snackBar.open(`Maximum of ${MAX_CUSTOM_GEOFENCES} custom geofences reached`, 'OK', { duration: 3000 });
      return;
    }
    this.drawMode.update(v => !v);
    if (this.drawMode()) {
      this.viewMode.set('map');
    }
  }

  private buildAreaList(): void {
    // DB stores lowercase area names, API may return mixed case
    const selectedSet = new Set(this.selectedAreas().map(a => a.toLowerCase()));
    const available = this.availableAreas();
    this.areas.set(
      available
        .map(a => ({
          name: a.name,
          group: a.group ?? '',
          selected: selectedSet.has(a.name.toLowerCase()),
        }))
        .sort((a, b) => a.name.localeCompare(b.name)),
    );
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

  private loadData(): void {
    this.loading.set(true);
    let loaded = 0;
    const check = () => {
      loaded++;
      if (loaded >= 3) {
        this.buildAreaList();
        this.loading.set(false);
      }
    };

    this.areaService
      .getSelected()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => check(),
        next: areas => {
          this.savedSelection = [...areas];
          this.selectedAreas.set(areas);
          check();
        },
      });

    this.areaService
      .getAvailable()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => check(),
        next: areas => {
          this.availableAreas.set(areas.filter(a => a.userSelectable !== false));
          check();
        },
      });

    this.locationService
      .getLocation()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => check(),
        next: loc => {
          this.location.set(loc);
          check();
          if (loc && (loc.latitude !== 0 || loc.longitude !== 0)) {
            this.locationService
              .reverseGeocode(loc.latitude, loc.longitude)
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe(result => {
                if (result?.display_name) this.locationAddress.set(result.display_name);
              });
            this.locationService
              .getStaticMapUrl(loc.latitude, loc.longitude)
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe(result => {
                if (result?.url) this.locationMapUrl.set(result.url);
              });
          }
        },
      });

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

  private syncSelectedFromAreas(): void {
    this.selectedAreas.set(
      this.areas()
        .filter(a => a.selected)
        .map(a => a.name),
    );
  }
}
