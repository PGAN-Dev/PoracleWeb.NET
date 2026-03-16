import {
  Component,
  Input,
  Output,
  EventEmitter,
  AfterViewInit,
  OnChanges,
  OnDestroy,
  ElementRef,
  ViewChild,
  SimpleChanges,
  computed,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import * as L from 'leaflet';
import { GeofenceData } from '../../../core/models';

const GROUP_COLORS = [
  '#e53935', '#1e88e5', '#43a047', '#fb8c00', '#8e24aa',
  '#00acc1', '#f4511e', '#3949ab', '#7cb342', '#c0ca33',
  '#6d4c41', '#546e7a', '#d81b60', '#039be5', '#00897b',
];

interface RegionEntry {
  label: string;
  shortLabel: string;
  groups: string[];
  areaCount: number;
}

interface RegionGrouping {
  label: string;
  regions: RegionEntry[];
}

@Component({
  selector: 'app-area-map',
  standalone: true,
  imports: [FormsModule, MatFormFieldModule, MatSelectModule, MatIconModule, MatButtonModule, MatChipsModule],
  template: `
    <div class="map-toolbar">
      <mat-form-field appearance="outline" class="region-select">
        <mat-label>Jump to Region</mat-label>
        <mat-select (selectionChange)="onRegionSelected($event.value)" [value]="selectedRegion()">
          <mat-option value="">All Regions</mat-option>
          @for (group of regionGroups(); track group.label) {
            <mat-optgroup [label]="group.label">
              @for (region of group.regions; track region.label) {
                <mat-option [value]="region.label">
                  {{ region.shortLabel }} ({{ region.areaCount }})
                </mat-option>
              }
            </mat-optgroup>
          }
        </mat-select>
      </mat-form-field>
      <button mat-icon-button matTooltip="Fit all areas" (click)="fitAll()">
        <mat-icon>zoom_out_map</mat-icon>
      </button>
      @if (selectedRegion()) {
        <mat-chip highlighted color="primary" (removed)="clearRegion()">
          {{ selectedRegion() }}
          <button matChipRemove><mat-icon>cancel</mat-icon></button>
        </mat-chip>
      }
    </div>
    <div #mapContainer class="area-map-container"></div>
    <div class="map-legend">
      @for (entry of visibleLegend(); track entry.group) {
        <span class="legend-item" (click)="onRegionSelected(entry.group)">
          <span class="legend-swatch" [style.background]="entry.color"></span>
          <span class="legend-label">{{ entry.group || 'Ungrouped' }}</span>
        </span>
      }
    </div>
  `,
  styles: [
    `
      .map-toolbar {
        display: flex;
        align-items: center;
        gap: 8px;
        margin-bottom: 8px;
        flex-wrap: wrap;
      }
      .region-select {
        flex: 1;
        min-width: 200px;
        max-width: 350px;
      }
      :host ::ng-deep .region-select .mat-mdc-form-field-subscript-wrapper {
        display: none;
      }
      .area-map-container {
        width: 100%;
        height: 450px;
        border-radius: 8px;
        overflow: hidden;
        border: 1px solid var(--divider, rgba(0, 0, 0, 0.12));
      }
      .map-legend {
        display: flex;
        flex-wrap: wrap;
        gap: 8px;
        margin-top: 8px;
        padding: 8px 0;
      }
      .legend-item {
        display: flex;
        align-items: center;
        gap: 4px;
        cursor: pointer;
        padding: 2px 8px;
        border-radius: 4px;
        font-size: 11px;
        transition: background 0.15s;
      }
      .legend-item:hover {
        background: var(--hover-overlay, rgba(0, 0, 0, 0.04));
      }
      .legend-swatch {
        width: 12px;
        height: 12px;
        border-radius: 3px;
        flex-shrink: 0;
      }
      .legend-label {
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        max-width: 180px;
      }
    `,
  ],
})
export class AreaMapComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('mapContainer', { static: true }) mapElement!: ElementRef<HTMLDivElement>;

  @Input() geofence: GeofenceData[] = [];
  @Input() selectedAreas: string[] = [];
  @Input() userLocation?: { lat: number; lng: number };
  @Input() groupMapping: Map<string, string> = new Map();

  @Output() areaClicked = new EventEmitter<string>();

  private map: L.Map | null = null;
  private polygonLayers: L.Polygon[] = [];
  private polygonByName = new Map<string, L.Polygon>();
  private userMarker: L.Marker | null = null;
  private initialized = false;
  private groupColorMap = new Map<string, string>();
  private allBoundsRect: L.LatLngBounds | null = null;

  readonly selectedRegion = signal('');
  readonly regions = signal<RegionEntry[]>([]);
  readonly regionGroups = signal<RegionGrouping[]>([]);
  readonly visibleLegend = signal<{ group: string; color: string }[]>([]);

  ngAfterViewInit(): void {
    this.initMap();
    this.initialized = true;
    this.drawPolygons();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.initialized) return;

    if (changes['geofence'] || changes['selectedAreas'] || changes['groupMapping']) {
      this.drawPolygons();
    }

    if (changes['userLocation']) {
      this.updateUserMarker();
    }
  }

  ngOnDestroy(): void {
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  onRegionSelected(regionLabel: string): void {
    this.selectedRegion.set(regionLabel);

    if (!regionLabel || !this.map) {
      this.fitAll();
      return;
    }

    // Find all areas belonging to groups in this region
    const region = this.regions().find(r => r.label === regionLabel);
    if (!region) return;

    const groupSet = new Set(region.groups);
    const bounds: L.LatLngExpression[] = [];

    for (const fence of this.geofence) {
      const group = this.groupMapping.get(fence.name) || '';
      if (groupSet.has(group) && fence.path?.length > 0) {
        bounds.push(...fence.path.map(c => [c[0], c[1]] as L.LatLngExpression));
      }
    }

    if (bounds.length > 0) {
      this.map.fitBounds(L.latLngBounds(bounds), { padding: [30, 30], maxZoom: 14 });
    }
  }

  clearRegion(): void {
    this.selectedRegion.set('');
    this.fitAll();
  }

  fitAll(): void {
    this.selectedRegion.set('');
    if (this.map && this.allBoundsRect) {
      this.map.fitBounds(this.allBoundsRect, { padding: [20, 20] });
    }
  }

  private initMap(): void {
    this.map = L.map(this.mapElement.nativeElement, {
      zoomControl: true,
      attributionControl: true,
    }).setView([37.5, -77.4], 10);

    L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
      attribution: '&copy; <a href="https://carto.com/">CARTO</a> &copy; <a href="https://www.openstreetmap.org/copyright">OSM</a>',
      maxZoom: 19,
      subdomains: 'abcd',
    }).addTo(this.map);
  }

  private drawPolygons(): void {
    if (!this.map) return;

    for (const layer of this.polygonLayers) {
      this.map.removeLayer(layer);
    }
    this.polygonLayers = [];
    this.polygonByName.clear();

    if (this.geofence.length === 0) return;

    const selectedSet = new Set(this.selectedAreas);

    // Build group-to-color mapping
    this.groupColorMap.clear();
    let colorIndex = 0;
    for (const fence of this.geofence) {
      const group = this.groupMapping.get(fence.name) || '';
      if (!this.groupColorMap.has(group)) {
        this.groupColorMap.set(group, GROUP_COLORS[colorIndex % GROUP_COLORS.length]);
        colorIndex++;
      }
    }

    // Build regions from groups (group by state/country prefix)
    this.buildRegions();

    // Build legend
    const legend: { group: string; color: string }[] = [];
    this.groupColorMap.forEach((color, group) => {
      legend.push({ group, color });
    });
    legend.sort((a, b) => a.group.localeCompare(b.group));
    this.visibleLegend.set(legend.slice(0, 30)); // limit legend to 30 entries

    const allBounds: L.LatLngExpression[] = [];

    for (const fence of this.geofence) {
      if (!fence.path || fence.path.length < 3) continue;

      const latLngs: L.LatLngExpression[] = fence.path.map(
        (coord) => [coord[0], coord[1]] as L.LatLngExpression,
      );
      allBounds.push(...latLngs);

      const isSelected = selectedSet.has(fence.name);
      const group = this.groupMapping.get(fence.name) || '';
      const color = this.groupColorMap.get(group) || GROUP_COLORS[0];

      const polygon = L.polygon(latLngs, {
        color: color,
        weight: isSelected ? 3 : 1.5,
        opacity: isSelected ? 0.9 : 0.5,
        fillColor: color,
        fillOpacity: isSelected ? 0.4 : 0.1,
        dashArray: isSelected ? undefined : '5, 5',
      });

      polygon.bindTooltip(fence.name, {
        sticky: true,
        direction: 'top',
        className: 'area-tooltip',
      });

      polygon.on('click', () => {
        this.areaClicked.emit(fence.name);
      });

      polygon.addTo(this.map!);
      this.polygonLayers.push(polygon);
      this.polygonByName.set(fence.name, polygon);
    }

    if (allBounds.length > 0) {
      this.allBoundsRect = L.latLngBounds(allBounds);
      if (!this.selectedRegion()) {
        this.map.fitBounds(this.allBoundsRect, { padding: [20, 20] });
      }
    }

    this.updateUserMarker();
  }

  private buildRegions(): void {
    // Group names follow pattern "US - State - City" (3 parts) or "KOR - City" (2 parts)
    // Region = full group name (all 3 parts for US, all 2 parts for KOR/AUS)
    const regionMap = new Map<string, Set<string>>();
    const areaCountMap = new Map<string, number>();

    // Only include regions that have geofence polygons available to this user
    for (const fence of this.geofence) {
      const group = this.groupMapping.get(fence.name) || '';
      const regionKey = group || 'Other';

      if (!regionMap.has(regionKey)) {
        regionMap.set(regionKey, new Set());
        areaCountMap.set(regionKey, 0);
      }
      regionMap.get(regionKey)!.add(group);
      areaCountMap.set(regionKey, (areaCountMap.get(regionKey) || 0) + 1);
    }

    const regions: RegionEntry[] = [];
    regionMap.forEach((groups, label) => {
      const parts = label.split(' - ');
      const shortLabel = parts.length >= 3 ? parts.slice(2).join(' - ') : (parts.length >= 2 ? parts[1] : label);
      regions.push({
        label,
        shortLabel,
        groups: [...groups],
        areaCount: areaCountMap.get(label) || 0,
      });
    });

    regions.sort((a, b) => a.label.localeCompare(b.label));
    this.regions.set(regions);

    // Build hierarchical groups: "US - VA" -> [Richmond, Hampton Roads, ...]
    const hierMap = new Map<string, RegionEntry[]>();
    for (const region of regions) {
      const parts = region.label.split(' - ');
      const parentKey = parts.length >= 2 ? parts.slice(0, 2).join(' - ').trim() : (parts[0] || 'Other');
      if (!hierMap.has(parentKey)) hierMap.set(parentKey, []);
      hierMap.get(parentKey)!.push(region);
    }

    const groups: RegionGrouping[] = [];
    hierMap.forEach((entries, label) => {
      groups.push({ label, regions: entries });
    });
    groups.sort((a, b) => a.label.localeCompare(b.label));
    this.regionGroups.set(groups);
  }

  private updateUserMarker(): void {
    if (!this.map) return;

    if (this.userMarker) {
      this.map.removeLayer(this.userMarker);
      this.userMarker = null;
    }

    if (this.userLocation) {
      this.userMarker = L.marker([this.userLocation.lat, this.userLocation.lng], {
        icon: L.divIcon({
          className: 'user-location-marker',
          html: '<div style="width:14px;height:14px;background:#1976D2;border:3px solid #fff;border-radius:50%;box-shadow:0 2px 6px rgba(0,0,0,0.4);"></div>',
          iconSize: [20, 20],
          iconAnchor: [10, 10],
        }),
      })
        .bindTooltip('Your Location', { direction: 'top' })
        .addTo(this.map);
    }
  }
}
