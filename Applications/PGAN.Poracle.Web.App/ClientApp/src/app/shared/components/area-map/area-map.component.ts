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
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import * as L from 'leaflet';

import { GeofenceData } from '../../../core/models';

const GROUP_COLORS = [
  '#e53935',
  '#1e88e5',
  '#43a047',
  '#fb8c00',
  '#8e24aa',
  '#00acc1',
  '#f4511e',
  '#3949ab',
  '#7cb342',
  '#c0ca33',
  '#6d4c41',
  '#546e7a',
  '#d81b60',
  '#039be5',
  '#00897b',
];

interface RegionEntry {
  areaCount: number;
  groups: string[];
  label: string;
  shortLabel: string;
}

interface RegionGrouping {
  label: string;
  regions: RegionEntry[];
}

@Component({
  imports: [
    FormsModule,
    MatAutocompleteModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatButtonModule,
    MatChipsModule,
    MatTooltipModule,
  ],
  selector: 'app-area-map',
  standalone: true,
  styleUrl: './area-map.component.scss',
  templateUrl: './area-map.component.html',
})
export class AreaMapComponent implements AfterViewInit, OnChanges, OnDestroy {
  private allBoundsRect: L.LatLngBounds | null = null;

  private fullscreenHandler = () => {
    if (!document.fullscreenElement) {
      this.isFullscreen.set(false);
      setTimeout(() => this.map?.invalidateSize(), 100);
    }
  };

  private groupColorMap = new Map<string, string>();
  private initialized = false;
  private map: L.Map | null = null;

  private polygonByName = new Map<string, L.Polygon>();

  private polygonLayers: L.Polygon[] = [];
  private userCircle: L.Circle | null = null;
  private userMarker: L.Marker | null = null;
  @Output() areaClicked = new EventEmitter<string>();
  @Input() geofence: GeofenceData[] = [];
  @Input() groupMapping: Map<string, string> = new Map();
  readonly isFullscreen = signal(false);
  @ViewChild('mapContainer', { static: true }) mapElement!: ElementRef<HTMLDivElement>;

  readonly regionGroups = signal<RegionGrouping[]>([]);

  readonly regions = signal<RegionEntry[]>([]);
  regionSearchText = '';
  @Input() selectedAreas: string[] = [];
  readonly selectedRegion = signal('');

  @Input() userLocation?: { lat: number; lng: number };

  readonly visibleLegend = signal<{ group: string; color: string }[]>([]);

  clearRegion(): void {
    this.selectedRegion.set('');
    this.regionSearchText = '';
    this.fitAll();
  }

  filteredRegions(): RegionEntry[] {
    const search = this.regionSearchText.toLowerCase();
    const all = this.regions();
    if (!search) return all;
    return all.filter(r => r.label.toLowerCase().includes(search) || r.shortLabel.toLowerCase().includes(search));
  }

  fitAll(): void {
    this.selectedRegion.set('');
    if (this.map && this.allBoundsRect) {
      this.map.fitBounds(this.allBoundsRect, { padding: [20, 20] });
    }
  }

  ngAfterViewInit(): void {
    this.initMap();
    this.initialized = true;
    this.drawPolygons();
    document.addEventListener('fullscreenchange', this.fullscreenHandler);
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
    document.removeEventListener('fullscreenchange', this.fullscreenHandler);
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  onRegionSelected(regionLabel: string): void {
    this.selectedRegion.set(regionLabel);
    this.regionSearchText = '';

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
      this.map.fitBounds(L.latLngBounds(bounds), { maxZoom: 14, padding: [30, 30] });
    }
  }

  toggleFullscreen(): void {
    const el = this.mapElement.nativeElement.closest('app-area-map') as HTMLElement | null;
    if (!el) return;

    if (!document.fullscreenElement) {
      el.requestFullscreen().then(() => {
        this.isFullscreen.set(true);
        setTimeout(() => this.map?.invalidateSize(), 100);
      });
    } else {
      document.exitFullscreen().then(() => {
        this.isFullscreen.set(false);
        setTimeout(() => this.map?.invalidateSize(), 100);
      });
    }
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
      const shortLabel = parts.length >= 3 ? parts.slice(2).join(' - ') : parts.length >= 2 ? parts[1] : label;
      regions.push({
        areaCount: areaCountMap.get(label) || 0,
        groups: [...groups],
        label,
        shortLabel,
      });
    });

    regions.sort((a, b) => a.label.localeCompare(b.label));
    this.regions.set(regions);

    // Build hierarchical groups: "US - VA" -> [Richmond, Hampton Roads, ...]
    const hierMap = new Map<string, RegionEntry[]>();
    for (const region of regions) {
      const parts = region.label.split(' - ');
      const parentKey = parts.length >= 2 ? parts.slice(0, 2).join(' - ').trim() : parts[0] || 'Other';
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

  private drawPolygons(): void {
    if (!this.map) return;

    for (const layer of this.polygonLayers) {
      this.map.removeLayer(layer);
    }
    this.polygonLayers = [];
    this.polygonByName.clear();

    if (this.geofence.length === 0) return;

    // Case-insensitive match: DB stores lowercase, geofence names may be mixed case
    const selectedSet = new Set(this.selectedAreas.map(a => a.toLowerCase()));

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
      legend.push({ color, group });
    });
    legend.sort((a, b) => a.group.localeCompare(b.group));
    this.visibleLegend.set(legend);

    const allBounds: L.LatLngExpression[] = [];

    for (const fence of this.geofence) {
      if (!fence.path || fence.path.length < 3) continue;

      const latLngs: L.LatLngExpression[] = fence.path.map(coord => [coord[0], coord[1]] as L.LatLngExpression);
      allBounds.push(...latLngs);

      const isSelected = selectedSet.has(fence.name.toLowerCase());
      const group = this.groupMapping.get(fence.name) || '';
      const color = this.groupColorMap.get(group) || GROUP_COLORS[0];

      const polygon = L.polygon(latLngs, {
        color: isSelected ? '#4caf50' : color,
        dashArray: isSelected ? undefined : '5, 5',
        fillColor: isSelected ? '#4caf50' : color,
        fillOpacity: isSelected ? 0.35 : 0.08,
        opacity: isSelected ? 1 : 0.4,
        weight: isSelected ? 3 : 1,
      });

      polygon.bindTooltip(fence.name, {
        className: 'area-tooltip',
        direction: 'top',
        sticky: true,
      });

      const originalWeight = isSelected ? 3 : 1;
      const originalFillOpacity = isSelected ? 0.35 : 0.08;

      polygon.on('mouseover', () => {
        polygon.setStyle({ weight: 3, fillOpacity: 0.4 });
      });
      polygon.on('mouseout', () => {
        polygon.setStyle({ weight: originalWeight, fillOpacity: originalFillOpacity });
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

  private initMap(): void {
    this.map = L.map(this.mapElement.nativeElement, {
      attributionControl: true,
      zoomControl: true,
    }).setView([37.5, -77.4], 10);

    L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
      attribution: '&copy; <a href="https://carto.com/">CARTO</a> &copy; <a href="https://www.openstreetmap.org/copyright">OSM</a>',
      maxZoom: 19,
      subdomains: 'abcd',
    }).addTo(this.map);
  }

  private updateUserMarker(): void {
    if (!this.map) return;

    if (this.userMarker) {
      this.map.removeLayer(this.userMarker);
      this.userMarker = null;
    }

    if (this.userCircle) {
      this.map.removeLayer(this.userCircle);
      this.userCircle = null;
    }

    if (this.userLocation) {
      this.userMarker = L.marker([this.userLocation.lat, this.userLocation.lng], {
        icon: L.divIcon({
          className: 'user-location-marker',
          html: '<div style="width:14px;height:14px;background:#1976D2;border:3px solid #fff;border-radius:50%;box-shadow:0 2px 6px rgba(0,0,0,0.4);"></div>',
          iconAnchor: [10, 10],
          iconSize: [20, 20],
        }),
      })
        .bindTooltip('Your Location', { direction: 'top' })
        .addTo(this.map);

      this.userCircle = L.circle(
        [this.userLocation.lat, this.userLocation.lng],
        {
          radius: 5000,
          color: '#1976d2',
          fillColor: '#1976d2',
          fillOpacity: 0.06,
          weight: 1.5,
          dashArray: '5, 5',
          interactive: false,
        }
      ).addTo(this.map);
    }
  }
}
