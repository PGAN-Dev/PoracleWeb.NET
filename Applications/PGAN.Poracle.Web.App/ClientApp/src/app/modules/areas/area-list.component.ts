import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { AreaDefinition, GeofenceData, Location } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { LocationService } from '../../core/services/location.service';
import { AreaMapComponent } from '../../shared/components/area-map/area-map.component';
import { LocationDialogComponent } from '../../shared/components/location-dialog/location-dialog.component';

interface AreaItem {
  description?: string;
  group: string;
  mapLoading?: boolean;
  mapUrl?: string | null;
  name: string;
  selected: boolean;
}

interface AreaGroup {
  allSelected: boolean;
  areas: AreaItem[];
  name: string;
  selectedCount: number;
  someSelected: boolean;
  totalCount: number;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatDialogModule,
    MatTooltipModule,
    MatExpansionModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatInputModule,
    MatBadgeModule,
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
  // Snapshot for cancel
  private originalSelection: string[] = [];

  private readonly rawGeofenceData = signal<GeofenceData[]>([]);
  private readonly snackBar = inject(MatSnackBar);
  readonly availableAreas = signal<AreaDefinition[]>([]);
  // Editable state
  readonly editableAreas = signal<AreaItem[]>([]);
  readonly editing = signal(false);
  readonly filteredGroups = signal<AreaGroup[]>([]);
  // Only show geofence polygons for areas the user has access to
  readonly geofenceData = computed(() => {
    const available = this.availableAreas();
    const raw = this.rawGeofenceData();
    if (available.length === 0) return raw; // no filtering if available not loaded yet
    const accessibleNames = new Set(available.map(a => a.name));
    return raw.filter(g => accessibleNames.has(g.name));
  });

  readonly groupMapping = computed(() => {
    const map = new Map<string, string>();
    for (const area of this.availableAreas()) {
      map.set(area.name, area.group ?? '');
    }
    return map;
  });

  readonly hasMultipleGroups = computed(() => {
    const groups = new Set(this.editableAreas().map(a => a.group));
    return groups.size > 1;
  });

  readonly loading = signal(true);

  readonly location = signal<Location | null>(null);
  readonly locationAddress = signal<string>('');

  readonly locationMapUrl = signal<string>('');
  manualAreaName = '';

  readonly saving = signal(false);

  searchText = '';

  readonly selectedAreas = signal<string[]>([]);

  readonly totalSelectedCount = computed(() => this.editableAreas().filter(a => a.selected).length);

  readonly userLocationForMap = computed(() => {
    const loc = this.location();
    if (loc && loc.latitude && loc.longitude) {
      return { lat: loc.latitude, lng: loc.longitude };
    }
    return undefined;
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
    this.rebuildGroups();
  }

  cancelEditing(): void {
    this.selectedAreas.set(this.originalSelection);
    this.editing.set(false);
  }

  clearSearch(): void {
    this.searchText = '';
    this.applyFilter();
  }

  deselectAllAreas(): void {
    for (const a of this.editableAreas()) a.selected = false;
    this.rebuildGroups();
    this.syncChipsFromEditable();
  }

  deselectGroup(groupName: string): void {
    for (const a of this.editableAreas()) {
      if (a.group === groupName) a.selected = false;
    }
    this.rebuildGroups();
    this.syncChipsFromEditable();
  }

  ngOnInit(): void {
    this.loadData();
  }

  onMapAreaClicked(name: string): void {
    if (this.editing()) {
      const areas = this.editableAreas();
      const item = areas.find(a => a.name === name);
      if (item) {
        this.toggleArea(item, !item.selected);
      }
    } else {
      // Not editing - start editing and toggle this area
      this.startEditing();
      const areas = this.editableAreas();
      const item = areas.find(a => a.name === name);
      if (item) {
        this.toggleArea(item, !item.selected);
      }
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

  removeArea(name: string): void {
    if (!this.editing()) return;
    const areas = this.editableAreas();
    const item = areas.find(a => a.name === name);
    if (item) {
      item.selected = false;
      this.applyFilter();
    }
    // Also update chips immediately
    this.selectedAreas.set(this.selectedAreas().filter(a => a !== name));
  }

  saveAreas(): void {
    const selected = this.editableAreas()
      .filter(a => a.selected)
      .map(a => a.name);

    this.saving.set(true);
    this.areaService
      .update(selected)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.saving.set(false);
          this.snackBar.open('Failed to update areas', 'OK', { duration: 3000 });
        },
        next: () => {
          this.selectedAreas.set(selected);
          this.saving.set(false);
          this.editing.set(false);
          this.snackBar.open('Areas updated successfully', 'OK', { duration: 3000 });
        },
      });
  }

  selectAllAreas(): void {
    for (const a of this.editableAreas()) a.selected = true;
    this.rebuildGroups();
    this.syncChipsFromEditable();
  }

  selectGroup(groupName: string): void {
    for (const a of this.editableAreas()) {
      if (a.group === groupName) a.selected = true;
    }
    this.rebuildGroups();
    this.syncChipsFromEditable();
  }

  startEditing(): void {
    this.originalSelection = [...this.selectedAreas()];
    const selectedSet = new Set(this.selectedAreas());
    const available = this.availableAreas();

    this.editableAreas.set(
      available
        .map(a => ({
          name: a.name,
          description: a.description,
          group: a.group ?? '',
          selected: selectedSet.has(a.name),
        }))
        .sort((a, b) => a.name.localeCompare(b.name)),
    );

    this.searchText = '';
    this.applyFilter();
    this.editing.set(true);
    this.loadMapPreviews();
  }

  toggleArea(area: AreaItem, checked: boolean): void {
    area.selected = checked;
    this.rebuildGroups();
    // Update chip preview
    this.selectedAreas.set(
      this.editableAreas()
        .filter(a => a.selected)
        .map(a => a.name),
    );
  }

  private loadData(): void {
    this.loading.set(true);
    let loaded = 0;
    const check = () => {
      loaded++;
      if (loaded >= 3) this.loading.set(false);
    };

    this.areaService
      .getSelected()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => check(),
        next: areas => {
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
                if (result?.display_name) {
                  this.locationAddress.set(result.display_name);
                }
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

    // Load geofence data async (non-blocking)
    this.areaService
      .getGeofencePolygons()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {},
        next: data => this.rawGeofenceData.set(data),
      });
  }

  private loadMapPreviews(): void {
    // Load map images for visible areas (batch in chunks to avoid flooding)
    const areas = this.editableAreas();
    const batchSize = 10;
    let i = 0;

    const loadBatch = () => {
      const batch = areas.slice(i, i + batchSize);
      if (batch.length === 0) return;

      for (const area of batch) {
        if (area.mapUrl !== undefined) continue; // already loaded or loading
        area.mapLoading = true;
        this.areaService
          .getMapUrl(area.name)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe(url => {
            area.mapUrl = url;
            area.mapLoading = false;
          });
      }

      i += batchSize;
      if (i < areas.length) {
        setTimeout(loadBatch, 200);
      }
    };

    loadBatch();
  }

  private rebuildGroups(): void {
    const search = this.searchText.toLowerCase();
    const all = this.editableAreas();
    const filtered = all.filter(a => !search || a.name.toLowerCase().includes(search));

    const groupMap = new Map<string, AreaItem[]>();
    for (const area of filtered) {
      const key = area.group || '';
      if (!groupMap.has(key)) groupMap.set(key, []);
      groupMap.get(key)!.push(area);
    }

    const groups: AreaGroup[] = [];
    const sortedKeys = [...groupMap.keys()].sort((a, b) => {
      if (a === '') return -1;
      if (b === '') return 1;
      return a.localeCompare(b);
    });

    for (const key of sortedKeys) {
      const areas = groupMap.get(key)!;
      // Count from ALL areas in this group (not just filtered)
      const allInGroup = all.filter(a => (a.group || '') === key);
      const selectedCount = allInGroup.filter(a => a.selected).length;
      const totalCount = allInGroup.length;

      groups.push({
        name: key || 'Ungrouped',
        allSelected: selectedCount === totalCount,
        areas,
        selectedCount,
        someSelected: selectedCount > 0 && selectedCount < totalCount,
        totalCount,
      });
    }

    this.filteredGroups.set(groups);
  }

  private syncChipsFromEditable(): void {
    this.selectedAreas.set(
      this.editableAreas()
        .filter(a => a.selected)
        .map(a => a.name),
    );
  }
}
