import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatBadgeModule } from '@angular/material/badge';
import { AreaService } from '../../core/services/area.service';
import { LocationService } from '../../core/services/location.service';
import { AreaDefinition, GeofenceData, Location } from '../../core/models';
import { LocationDialogComponent } from '../../shared/components/location-dialog/location-dialog.component';
import { AreaMapComponent } from '../../shared/components/area-map/area-map.component';

interface AreaItem {
  name: string;
  group: string;
  description?: string;
  selected: boolean;
  mapUrl?: string | null;
  mapLoading?: boolean;
}

interface AreaGroup {
  name: string;
  areas: AreaItem[];
  selectedCount: number;
  totalCount: number;
  allSelected: boolean;
  someSelected: boolean;
}

@Component({
  selector: 'app-area-list',
  standalone: true,
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
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>Areas & Location</h1>
        <p class="page-description">Configure which geographic areas you receive notifications for, and set your home location for distance-based alerts. Click <mat-icon class="inline-icon">edit</mat-icon> Edit Areas to select from available geofences. Set your location to enable distance-based filtering.</p>
      </div>
    </div>

    @if (loading()) {
      <div class="loading-container">
        <mat-spinner diameter="48"></mat-spinner>
      </div>
    } @else {
      <div class="areas-page">
        <!-- Area Overview Map -->
        @if (geofenceData().length > 0) {
          <mat-card class="area-map-card">
            <mat-card-header>
              <mat-icon mat-card-avatar>public</mat-icon>
              <mat-card-title>Area Overview</mat-card-title>
              <mat-card-subtitle>{{ geofenceData().length }} geofenced areas</mat-card-subtitle>
            </mat-card-header>
            <mat-card-content>
              <app-area-map
                [geofence]="geofenceData()"
                [selectedAreas]="selectedAreas()"
                [userLocation]="userLocationForMap()"
                [groupMapping]="groupMapping()"
                (areaClicked)="onMapAreaClicked($event)"
              ></app-area-map>
            </mat-card-content>
          </mat-card>
        }

        <!-- Selected Areas Chips -->
        <mat-card class="selected-areas-card">
          <mat-card-header>
            <mat-icon mat-card-avatar>map</mat-icon>
            <mat-card-title>Selected Areas</mat-card-title>
            <mat-card-subtitle>{{ selectedAreas().length }} area(s) active</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            @if (selectedAreas().length > 0) {
              <mat-chip-set class="selected-chips">
                @for (area of selectedAreas(); track area) {
                  <mat-chip highlighted color="primary" (removed)="removeArea(area)">
                    <mat-icon matChipAvatar>place</mat-icon>
                    {{ area }}
                    @if (editing()) {
                      <button matChipRemove>
                        <mat-icon>cancel</mat-icon>
                      </button>
                    }
                  </mat-chip>
                }
              </mat-chip-set>
            } @else {
              <div class="empty-state">
                <mat-icon class="empty-icon">map</mat-icon>
                <p>No areas selected</p>
              </div>
            }
          </mat-card-content>
          <mat-card-actions align="end">
            @if (!editing()) {
              <button mat-raised-button color="primary" (click)="startEditing()">
                <mat-icon>edit</mat-icon> Edit Areas
              </button>
            }
          </mat-card-actions>
        </mat-card>

        <!-- Area Selection Panel -->
        @if (editing()) {
          <mat-card class="area-selection-card">
            <mat-card-header>
              <mat-icon mat-card-avatar>checklist</mat-icon>
              <mat-card-title>Select Areas</mat-card-title>
              <mat-card-subtitle>Choose the areas you want to track</mat-card-subtitle>
            </mat-card-header>
            <mat-card-content>
              @if (availableAreas().length === 0) {
                <!-- Poracle API offline -->
                <div class="api-offline-notice">
                  <mat-icon class="warning-icon">cloud_off</mat-icon>
                  <p>Area geofences are not available. The Poracle API may be offline.</p>
                  <mat-form-field appearance="outline" class="manual-input">
                    <mat-label>Add area name manually</mat-label>
                    <input
                      matInput
                      [(ngModel)]="manualAreaName"
                      (keyup.enter)="addManualArea()"
                      placeholder="Type an area name and press Enter"
                    />
                    <button
                      mat-icon-button
                      matSuffix
                      (click)="addManualArea()"
                      [disabled]="!manualAreaName.trim()"
                    >
                      <mat-icon>add_circle</mat-icon>
                    </button>
                  </mat-form-field>
                </div>
              } @else {
                <!-- Search -->
                <mat-form-field appearance="outline" class="search-field">
                  <mat-label>Search areas</mat-label>
                  <mat-icon matPrefix>search</mat-icon>
                  <input
                    matInput
                    [(ngModel)]="searchText"
                    (ngModelChange)="applyFilter()"
                    placeholder="Filter by name..."
                  />
                  @if (searchText) {
                    <button mat-icon-button matSuffix (click)="clearSearch()">
                      <mat-icon>close</mat-icon>
                    </button>
                  }
                </mat-form-field>

                @if (hasMultipleGroups()) {
                  <!-- Accordion grouped view -->
                  <mat-accordion multi>
                    @for (group of filteredGroups(); track group.name) {
                      <mat-expansion-panel>
                        <mat-expansion-panel-header>
                          <mat-panel-title>
                            <mat-icon class="group-icon">folder</mat-icon>
                            <span class="group-name">{{ group.name }}</span>
                            <span class="group-badge" [class.has-selected]="group.selectedCount > 0">
                              {{ group.selectedCount }}/{{ group.totalCount }}
                            </span>
                          </mat-panel-title>
                        </mat-expansion-panel-header>

                        <div class="group-actions">
                          <button mat-button color="primary" (click)="selectGroup(group.name)">
                            <mat-icon>select_all</mat-icon> Select All
                          </button>
                          <button mat-button (click)="deselectGroup(group.name)">
                            <mat-icon>deselect</mat-icon> Deselect All
                          </button>
                        </div>

                        <div class="area-grid">
                          @for (area of group.areas; track area.name) {
                            <div class="area-tile" [class.area-selected]="area.selected" (click)="toggleArea(area, !area.selected)">
                              @if (area.mapUrl) {
                                <img [src]="area.mapUrl" class="area-map-img" [alt]="area.name" loading="lazy" />
                              } @else if (area.mapLoading) {
                                <div class="area-map-placeholder"><mat-spinner diameter="24"></mat-spinner></div>
                              } @else {
                                <div class="area-map-placeholder"><mat-icon>map</mat-icon></div>
                              }
                              <div class="area-tile-footer">
                                <mat-checkbox [checked]="area.selected" (click)="$event.stopPropagation()" (change)="toggleArea(area, $event.checked)"></mat-checkbox>
                                <span class="area-tile-name">{{ area.name }}</span>
                              </div>
                            </div>
                          }
                        </div>
                      </mat-expansion-panel>
                    }
                  </mat-accordion>
                } @else {
                  <!-- Flat list (single group or ungrouped) -->
                  @if (filteredGroups().length > 0) {
                    <div class="flat-list-actions">
                      <button mat-button color="primary" (click)="selectAllAreas()">
                        <mat-icon>select_all</mat-icon> Select All
                      </button>
                      <button mat-button (click)="deselectAllAreas()">
                        <mat-icon>deselect</mat-icon> Deselect All
                      </button>
                      <span class="selection-count">
                        {{ totalSelectedCount() }} of {{ editableAreas().length }} selected
                      </span>
                    </div>
                    <div class="area-grid">
                      @for (area of filteredGroups()[0].areas; track area.name) {
                        <div class="area-tile" [class.area-selected]="area.selected" (click)="toggleArea(area, !area.selected)">
                          @if (area.mapUrl) {
                            <img [src]="area.mapUrl" class="area-map-img" [alt]="area.name" loading="lazy" />
                          } @else if (area.mapLoading) {
                            <div class="area-map-placeholder"><mat-spinner diameter="24"></mat-spinner></div>
                          } @else {
                            <div class="area-map-placeholder"><mat-icon>map</mat-icon></div>
                          }
                          <div class="area-tile-footer">
                            <mat-checkbox [checked]="area.selected" (click)="$event.stopPropagation()" (change)="toggleArea(area, $event.checked)"></mat-checkbox>
                            <span class="area-tile-name">{{ area.name }}</span>
                          </div>
                        </div>
                      }
                    </div>
                  }
                }

                @if (filteredGroups().length === 0 && searchText) {
                  <p class="no-results">No areas match "{{ searchText }}"</p>
                }
              }
            </mat-card-content>
            <mat-card-actions align="end">
              <button mat-button (click)="cancelEditing()">
                <mat-icon>close</mat-icon> Cancel
              </button>
              <button mat-raised-button color="primary" (click)="saveAreas()" [disabled]="saving()">
                @if (saving()) {
                  <mat-spinner diameter="20" class="inline-spinner"></mat-spinner>
                } @else {
                  <mat-icon>save</mat-icon>
                }
                Save
              </button>
            </mat-card-actions>
          </mat-card>
        }

        <!-- Location Card -->
        <mat-card class="location-card">
          <mat-card-header>
            <mat-icon mat-card-avatar>place</mat-icon>
            <mat-card-title>Current Location</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            @if (location(); as loc) {
              @if (locationMapUrl()) {
                <img [src]="locationMapUrl()" class="location-static-map" alt="Location map" loading="lazy" />
              }
              @if (locationAddress()) {
                <div class="location-address">
                  <mat-icon>place</mat-icon>
                  <span>{{ locationAddress() }}</span>
                </div>
              }
              <div class="location-coords">
                <span class="coord-chip">{{ loc.latitude.toFixed(6) }}, {{ loc.longitude.toFixed(6) }}</span>
              </div>
            } @else {
              <p class="no-location">No location set</p>
            }
          </mat-card-content>
          <mat-card-actions align="end">
            <button mat-raised-button color="accent" (click)="openLocationDialog()">
              <mat-icon>my_location</mat-icon> Set Location
            </button>
          </mat-card-actions>
        </mat-card>
      </div>
    }
  `,
  styles: [
    `
      .page-header {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        padding: 16px 24px;
        gap: 16px;
      }
      .page-header-text {
        flex: 1;
        min-width: 0;
      }
      .page-header h1 {
        margin: 0;
        font-size: 24px;
        font-weight: 400;
      }
      .page-description {
        margin: 4px 0 0;
        color: var(--text-secondary, rgba(0,0,0,0.54));
        font-size: 13px;
        line-height: 1.5;
        border-left: 3px solid #1976d2;
        padding-left: 12px;
      }
      .page-description .inline-icon {
        font-size: 14px;
        width: 14px;
        height: 14px;
        vertical-align: middle;
      }
      .loading-container {
        display: flex;
        justify-content: center;
        padding: 64px;
      }
      .areas-page {
        display: flex;
        flex-direction: column;
        gap: 24px;
        padding: 0 24px 24px;
        max-width: 900px;
      }

      /* Area Map Card */
      .area-map-card {
        border-top: 4px solid #9c27b0;
      }

      /* Selected Areas Card */
      .selected-areas-card {
        border-top: 4px solid #4caf50;
      }
      .selected-chips {
        margin-top: 8px;
        display: flex;
        flex-wrap: wrap;
        gap: 4px;
      }
      .empty-state {
        text-align: center;
        padding: 24px 0;
      }
      .empty-icon {
        font-size: 48px;
        width: 48px;
        height: 48px;
        color: var(--text-hint, rgba(0, 0, 0, 0.24));
      }
      .empty-state p {
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
        margin: 8px 0 0;
      }

      /* Area Selection Card */
      .area-selection-card {
        border-top: 4px solid #ff9800;
      }
      .search-field {
        width: 100%;
        margin-bottom: 8px;
      }

      /* API Offline */
      .api-offline-notice {
        text-align: center;
        padding: 16px 0;
      }
      .warning-icon {
        font-size: 48px;
        width: 48px;
        height: 48px;
        color: #ff9800;
      }
      .api-offline-notice p {
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
        margin: 8px 0 16px;
      }
      .manual-input {
        width: 100%;
        max-width: 400px;
      }

      /* Accordion & Groups */
      mat-accordion {
        display: block;
      }
      .group-icon {
        font-size: 20px;
        width: 20px;
        height: 20px;
        margin-right: 8px;
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
      }
      .group-name {
        font-weight: 500;
      }
      .group-badge {
        margin-left: 12px;
        font-size: 12px;
        font-weight: 500;
        padding: 2px 8px;
        border-radius: 12px;
        background: var(--divider, rgba(0, 0, 0, 0.08));
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
      }
      .group-badge.has-selected {
        background: #e8f5e9;
        color: #2e7d32;
      }
      .group-actions {
        display: flex;
        gap: 8px;
        margin-bottom: 8px;
        padding-bottom: 8px;
        border-bottom: 1px solid var(--divider, rgba(0, 0, 0, 0.08));
      }

      /* Flat list */
      .flat-list-actions {
        display: flex;
        align-items: center;
        gap: 8px;
        margin-bottom: 12px;
      }
      .selection-count {
        margin-left: auto;
        font-size: 13px;
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
      }

      /* Area Grid */
      .area-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
        gap: 12px;
        padding: 8px 0;
      }
      .area-tile {
        border: 2px solid var(--divider, rgba(0,0,0,0.12));
        border-radius: 8px;
        overflow: hidden;
        cursor: pointer;
        transition: border-color 0.2s, box-shadow 0.2s;
      }
      .area-tile:hover {
        border-color: #90caf9;
        box-shadow: 0 2px 8px rgba(0,0,0,0.1);
      }
      .area-tile.area-selected {
        border-color: #4caf50;
        box-shadow: 0 0 0 1px #4caf50;
      }
      .area-map-img {
        width: 100%;
        height: 100px;
        object-fit: cover;
        display: block;
        background: #e0e0e0;
      }
      .area-map-placeholder {
        width: 100%;
        height: 100px;
        display: flex;
        align-items: center;
        justify-content: center;
        background: var(--skeleton-bg, #f0f0f0);
        color: var(--text-hint, rgba(0,0,0,0.24));
      }
      .area-map-placeholder mat-icon {
        font-size: 36px;
        width: 36px;
        height: 36px;
      }
      .area-tile-footer {
        display: flex;
        align-items: center;
        padding: 4px 8px;
        gap: 4px;
      }
      .area-tile-name {
        font-size: 12px;
        font-weight: 500;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .no-results {
        text-align: center;
        color: var(--text-hint, rgba(0, 0, 0, 0.38));
        padding: 24px;
      }

      /* Location Card */
      .location-card {
        border-top: 4px solid #2196f3;
      }
      .location-static-map {
        width: 100%;
        max-height: 200px;
        object-fit: cover;
        border-radius: 8px;
        margin-bottom: 12px;
        border: 1px solid var(--divider, rgba(0,0,0,0.08));
      }
      .location-details {
        display: flex;
        flex-direction: column;
        gap: 8px;
      }
      .location-coords {
        margin-top: 4px;
      }
      .coord-chip {
        font-family: monospace;
        font-size: 12px;
        color: var(--text-secondary, rgba(0,0,0,0.54));
        background: var(--skeleton-bg, rgba(0,0,0,0.04));
        padding: 4px 10px;
        border-radius: 12px;
      }
      .location-address {
        display: flex;
        align-items: flex-start;
        gap: 8px;
        margin-top: 12px;
        padding: 8px 12px;
        background: rgba(33, 150, 243, 0.08);
        border-radius: 8px;
        font-size: 13px;
        color: var(--text-secondary, rgba(0, 0, 0, 0.7));
        line-height: 1.4;
      }
      .location-address mat-icon {
        font-size: 18px;
        width: 18px;
        height: 18px;
        color: #1565c0;
        flex-shrink: 0;
        margin-top: 1px;
      }
      .no-location {
        color: var(--text-hint, rgba(0, 0, 0, 0.38));
        font-style: italic;
      }

      /* Save button spinner */
      .inline-spinner {
        display: inline-block;
        margin-right: 4px;
      }

      /* Responsive */
      @media (max-width: 600px) {
        .page-header {
          padding: 12px 16px;
        }
        .areas-page {
          padding: 0 16px 16px;
        }
        .location-info {
          flex-direction: column;
          gap: 12px;
        }
      }
    `,
  ],
})
export class AreaListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly areaService = inject(AreaService);
  private readonly locationService = inject(LocationService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly editing = signal(false);
  readonly selectedAreas = signal<string[]>([]);
  readonly availableAreas = signal<AreaDefinition[]>([]);
  readonly location = signal<Location | null>(null);
  readonly locationAddress = signal<string>('');
  readonly locationMapUrl = signal<string>('');
  private readonly rawGeofenceData = signal<GeofenceData[]>([]);

  // Only show geofence polygons for areas the user has access to
  readonly geofenceData = computed(() => {
    const available = this.availableAreas();
    const raw = this.rawGeofenceData();
    if (available.length === 0) return raw; // no filtering if available not loaded yet
    const accessibleNames = new Set(available.map(a => a.name));
    return raw.filter(g => accessibleNames.has(g.name));
  });

  // Editable state
  readonly editableAreas = signal<AreaItem[]>([]);
  readonly filteredGroups = signal<AreaGroup[]>([]);

  searchText = '';
  manualAreaName = '';

  // Snapshot for cancel
  private originalSelection: string[] = [];

  readonly userLocationForMap = computed(() => {
    const loc = this.location();
    if (loc && loc.latitude && loc.longitude) {
      return { lat: loc.latitude, lng: loc.longitude };
    }
    return undefined;
  });

  readonly groupMapping = computed(() => {
    const map = new Map<string, string>();
    for (const area of this.availableAreas()) {
      map.set(area.name, area.group ?? '');
    }
    return map;
  });

  readonly hasMultipleGroups = computed(() => {
    const groups = new Set(this.editableAreas().map((a) => a.group));
    return groups.size > 1;
  });

  readonly totalSelectedCount = computed(() =>
    this.editableAreas().filter((a) => a.selected).length,
  );

  ngOnInit(): void {
    this.loadData();
  }

  private loadData(): void {
    this.loading.set(true);
    let loaded = 0;
    const check = () => {
      loaded++;
      if (loaded >= 3) this.loading.set(false);
    };

    this.areaService.getSelected().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (areas) => {
        this.selectedAreas.set(areas);
        check();
      },
      error: () => check(),
    });

    this.areaService.getAvailable().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (areas) => {
        this.availableAreas.set(areas.filter((a) => a.userSelectable !== false));
        check();
      },
      error: () => check(),
    });

    this.locationService.getLocation().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (loc) => {
        this.location.set(loc);
        check();
        if (loc && (loc.latitude !== 0 || loc.longitude !== 0)) {
          this.locationService.reverseGeocode(loc.latitude, loc.longitude).pipe(
            takeUntilDestroyed(this.destroyRef)
          ).subscribe((result) => {
            if (result?.display_name) {
              this.locationAddress.set(result.display_name);
            }
          });
          this.locationService.getStaticMapUrl(loc.latitude, loc.longitude).pipe(
            takeUntilDestroyed(this.destroyRef)
          ).subscribe((result) => {
            if (result?.url) this.locationMapUrl.set(result.url);
          });
        }
      },
      error: () => check(),
    });

    // Load geofence data async (non-blocking)
    this.areaService.getGeofencePolygons().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (data) => this.rawGeofenceData.set(data),
      error: () => {},
    });
  }

  startEditing(): void {
    this.originalSelection = [...this.selectedAreas()];
    const selectedSet = new Set(this.selectedAreas());
    const available = this.availableAreas();

    this.editableAreas.set(
      available
        .map((a) => ({
          name: a.name,
          group: a.group ?? '',
          description: a.description,
          selected: selectedSet.has(a.name),
        }))
        .sort((a, b) => a.name.localeCompare(b.name)),
    );

    this.searchText = '';
    this.applyFilter();
    this.editing.set(true);
    this.loadMapPreviews();
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
        this.areaService.getMapUrl(area.name).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(url => {
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

  cancelEditing(): void {
    this.selectedAreas.set(this.originalSelection);
    this.editing.set(false);
  }

  saveAreas(): void {
    const selected = this.editableAreas()
      .filter((a) => a.selected)
      .map((a) => a.name);

    this.saving.set(true);
    this.areaService.update(selected).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.selectedAreas.set(selected);
        this.saving.set(false);
        this.editing.set(false);
        this.snackBar.open('Areas updated successfully', 'OK', { duration: 3000 });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Failed to update areas', 'OK', { duration: 3000 });
      },
    });
  }

  removeArea(name: string): void {
    if (!this.editing()) return;
    const areas = this.editableAreas();
    const item = areas.find((a) => a.name === name);
    if (item) {
      item.selected = false;
      this.applyFilter();
    }
    // Also update chips immediately
    this.selectedAreas.set(this.selectedAreas().filter((a) => a !== name));
  }

  toggleArea(area: AreaItem, checked: boolean): void {
    area.selected = checked;
    this.rebuildGroups();
    // Update chip preview
    this.selectedAreas.set(
      this.editableAreas()
        .filter((a) => a.selected)
        .map((a) => a.name),
    );
  }

  selectGroup(groupName: string): void {
    for (const a of this.editableAreas()) {
      if (a.group === groupName) a.selected = true;
    }
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

  selectAllAreas(): void {
    for (const a of this.editableAreas()) a.selected = true;
    this.rebuildGroups();
    this.syncChipsFromEditable();
  }

  deselectAllAreas(): void {
    for (const a of this.editableAreas()) a.selected = false;
    this.rebuildGroups();
    this.syncChipsFromEditable();
  }

  applyFilter(): void {
    this.rebuildGroups();
  }

  clearSearch(): void {
    this.searchText = '';
    this.applyFilter();
  }

  addManualArea(): void {
    const name = this.manualAreaName.trim();
    if (!name) return;
    if (!this.selectedAreas().includes(name)) {
      this.selectedAreas.set([...this.selectedAreas(), name]);
    }
    this.manualAreaName = '';
  }

  onMapAreaClicked(name: string): void {
    if (this.editing()) {
      const areas = this.editableAreas();
      const item = areas.find((a) => a.name === name);
      if (item) {
        this.toggleArea(item, !item.selected);
      }
    } else {
      // Not editing - start editing and toggle this area
      this.startEditing();
      const areas = this.editableAreas();
      const item = areas.find((a) => a.name === name);
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
          this.locationService.reverseGeocode(result.latitude, result.longitude).pipe(
            takeUntilDestroyed(this.destroyRef)
          ).subscribe((geo) => {
            if (geo?.display_name) this.locationAddress.set(geo.display_name);
          });
          this.locationService.getStaticMapUrl(result.latitude, result.longitude).pipe(
            takeUntilDestroyed(this.destroyRef)
          ).subscribe((map) => {
            if (map?.url) this.locationMapUrl.set(map.url);
          });
        }
      }
    });
  }

  private syncChipsFromEditable(): void {
    this.selectedAreas.set(
      this.editableAreas()
        .filter((a) => a.selected)
        .map((a) => a.name),
    );
  }

  private rebuildGroups(): void {
    const search = this.searchText.toLowerCase();
    const all = this.editableAreas();
    const filtered = all.filter(
      (a) => !search || a.name.toLowerCase().includes(search),
    );

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
      const allInGroup = all.filter((a) => (a.group || '') === key);
      const selectedCount = allInGroup.filter((a) => a.selected).length;
      const totalCount = allInGroup.length;

      groups.push({
        name: key || 'Ungrouped',
        areas,
        selectedCount,
        totalCount,
        allSelected: selectedCount === totalCount,
        someSelected: selectedCount > 0 && selectedCount < totalCount,
      });
    }

    this.filteredGroups.set(groups);
  }
}
