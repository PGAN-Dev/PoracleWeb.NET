import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

import { AreaDefinition, GeofenceData, Location } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { LocationService } from '../../core/services/location.service';
import { AreaMapComponent } from '../../shared/components/area-map/area-map.component';
import { LocationDialogComponent } from '../../shared/components/location-dialog/location-dialog.component';

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

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatChipsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
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
  readonly geofenceData = computed(() => {
    const available = this.availableAreas();
    const raw = this.rawGeofenceData();
    if (available.length === 0) return raw;
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

  readonly loading = signal(true);
  readonly location = signal<Location | null>(null);

  readonly locationAddress = signal<string>('');

  readonly locationMapUrl = signal<string>('');

  manualAreaName = '';

  readonly saving = signal(false);

  searchText = '';

  readonly userLocationForMap = computed(() => {
    const loc = this.location();
    if (loc && loc.latitude && loc.longitude) {
      return { lat: loc.latitude, lng: loc.longitude };
    }
    return undefined;
  });

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
    const savedSet = new Set(this.savedSelection);
    for (const a of this.areas()) {
      a.selected = savedSet.has(a.name);
    }
    this.selectedAreas.set([...this.savedSelection]);
  }

  clearSearch(): void {
    this.searchText = '';
  }

  deselectAllVisible(): void {
    for (const a of this.visibleAreas()) a.selected = false;
    this.syncSelectedFromAreas();
  }

  ngOnInit(): void {
    this.loadData();
  }

  onMapAreaClicked(name: string): void {
    const area = this.areas().find(a => a.name === name);
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
    const area = this.areas().find(a => a.name === name);
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

  private buildAreaList(): void {
    const selectedSet = new Set(this.selectedAreas());
    const available = this.availableAreas();
    this.areas.set(
      available
        .map(a => ({
          name: a.name,
          group: a.group ?? '',
          selected: selectedSet.has(a.name),
        }))
        .sort((a, b) => a.name.localeCompare(b.name)),
    );
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

  private syncSelectedFromAreas(): void {
    this.selectedAreas.set(
      this.areas()
        .filter(a => a.selected)
        .map(a => a.name),
    );
  }
}
