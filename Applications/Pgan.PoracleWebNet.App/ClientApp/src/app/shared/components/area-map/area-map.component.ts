import {
  AfterViewInit,
  Component,
  ElementRef,
  EventEmitter,
  inject,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  ViewChild,
  computed,
  effect,
  input,
  output,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import * as L from 'leaflet';
import 'leaflet-draw';

import { INITIAL_VIEW_MAX_ZOOM, LOCATION_ONLY_ZOOM, planInitialView } from './initial-view';
import { GeofenceData } from '../../../core/models';
import { I18nService } from '../../../core/services/i18n.service';
import { RegionOption, RegionSelectorComponent } from '../region-selector/region-selector.component';

/**
 * Padding for an automatic fit. Asymmetric on purpose: the "N area(s) selected" badge sits at
 * bottom centre and the Leaflet attribution at bottom right, so a shape fitted flush to the bottom
 * edge ends up underneath them.
 */
const FIT_OPTIONS: L.FitBoundsOptions = {
  maxZoom: INITIAL_VIEW_MAX_ZOOM,
  paddingBottomRight: [24, 56],
  paddingTopLeft: [24, 24],
};

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

@Component({
  imports: [MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe, RegionSelectorComponent],
  selector: 'app-area-map',
  standalone: true,
  styleUrl: './area-map.component.scss',
  templateUrl: './area-map.component.html',
})
export class AreaMapComponent implements AfterViewInit, OnChanges, OnDestroy {
  private allBoundsRect: L.LatLngBounds | null = null;
  private customBoundsRect: L.LatLngBounds | null = null;
  private customGeofenceLayer: L.LayerGroup = L.layerGroup();
  private drawControl: L.Control.Draw | null = null;

  /** Rank of the anchor the map is currently sitting on. See planInitialView. */
  private fittedViewPriority = 0;

  private fullscreenHandler = () => {
    if (!document.fullscreenElement) {
      this.isFullscreen.set(false);
      setTimeout(() => this.map?.invalidateSize(), 100);
    }
  };

  private groupColorMap = new Map<string, string>();

  private readonly i18n = inject(I18nService);

  private initialized = false;

  private lockViewHandler = (): void => {
    this.viewLockedByUser = true;
  };

  private map: L.Map | null = null;

  private onDrawCreated = (event: L.LeafletEvent): void => {
    const drawEvent = event as L.DrawEvents.Created;
    const layer = drawEvent.layer as L.Polygon;
    const latLngs = (layer.getLatLngs()[0] as L.LatLng[]).map(ll => [ll.lat, ll.lng] as [number, number]);

    this.polygonDrawn.emit(latLngs);
    // Do not add to map -- parent component handles saving and re-rendering
  };

  private polygonByName = new Map<string, L.Polygon>();

  private polygonLayers: L.Polygon[] = [];
  private selectionBoundsRect: L.LatLngBounds | null = null;
  private userCircle: L.Circle | null = null;
  private userMarker: L.Marker | null = null;

  /** Set by any deliberate view choice -- drag, zoom, region jump, fit all. Stops auto-fitting. */
  private viewLockedByUser = false;
  @Output() areaClicked = new EventEmitter<string>();
  customGeofences = input<GeofenceData[]>([]);
  drawMode = input(false);
  @Input() geofence: GeofenceData[] = [];
  @Input() groupMapping: Map<string, string> = new Map();
  readonly isFullscreen = signal(false);
  @ViewChild('mapContainer', { static: true }) mapElement!: ElementRef<HTMLDivElement>;
  polygonDrawn = output<[number, number][]>();
  regionChanged = output<RegionOption>();

  readonly regions = signal<RegionEntry[]>([]);

  readonly regionOptions = computed((): RegionOption[] => {
    return this.regions().map(r => ({
      count: r.areaCount,
      label: r.label,
      shortLabel: r.shortLabel,
    }));
  });

  @Input() selectedAreas: string[] = [];
  readonly selectedRegion = signal('');

  @Input() userLocation?: { lat: number; lng: number };

  readonly visibleLegend = signal<{ group: string; color: string }[]>([]);

  constructor() {
    // React to drawMode changes
    effect(() => {
      const enabled = this.drawMode();
      if (!this.map) return;

      if (enabled) {
        this.addDrawControl();
      } else {
        this.removeDrawControl();
      }
    });

    // React to customGeofences changes
    effect(() => {
      const geofences = this.customGeofences();
      this.renderCustomGeofences(geofences);
    });
  }

  clearRegion(): void {
    this.selectedRegion.set('');
    this.fitAll();
  }

  fitAll(): void {
    this.selectedRegion.set('');
    // An explicit "show me everything" is a view choice; nothing should silently override it.
    this.viewLockedByUser = true;
    if (this.map && this.allBoundsRect) {
      this.map.fitBounds(this.allBoundsRect, FIT_OPTIONS);
    }
  }

  ngAfterViewInit(): void {
    this.initMap();
    this.initialized = true;
    this.drawPolygons();
    // The customGeofences effect runs before the map exists and bails out, so the first value has
    // to be drawn here or My Geofences opens with no shapes and no bounds to anchor on.
    this.renderCustomGeofences(this.customGeofences());
    document.addEventListener('fullscreenchange', this.fullscreenHandler);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.initialized) return;

    if (changes['geofence'] || changes['groupMapping']) {
      // Geofence data or group mapping changed -- full redraw needed, allow re-fit
      if (changes['geofence']) {
        this.fittedViewPriority = 0;
      }
      this.drawPolygons();
    } else if (changes['selectedAreas']) {
      // Only selection changed -- restyle without resetting the view. The fit below cannot move the
      // map once it has already anchored on a selection, so toggling an area never jumps the view;
      // it only matters when the selection arrives after the map has fitted something worse.
      this.updatePolygonStyles();
      this.selectionBoundsRect = this.computeSelectionBounds();
      this.applyInitialView();
    }

    if (changes['userLocation']) {
      this.updateUserMarker();
      this.applyInitialView();
    }
  }

  ngOnDestroy(): void {
    document.removeEventListener('fullscreenchange', this.fullscreenHandler);
    for (const event of ['pointerdown', 'wheel', 'keydown']) {
      this.mapElement.nativeElement.removeEventListener(event, this.lockViewHandler);
    }
    this.removeDrawControl();
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  onRegionSelected(option: RegionOption): void {
    const regionLabel = option.label;
    this.selectedRegion.set(regionLabel);
    // Jumping to a region states where the user wants to be; later data must not pull them away.
    this.viewLockedByUser = true;
    this.regionChanged.emit(option);

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

  private addDrawControl(): void {
    if (!this.map || this.drawControl) return;

    this.drawControl = new L.Control.Draw({
      draw: {
        circle: false,
        circlemarker: false,
        marker: false,
        polygon: {
          shapeOptions: {
            color: '#2196f3',
            fillOpacity: 0.15,
            weight: 2,
          },
        },
        polyline: false,
        rectangle: false,
      },
      edit: false as any,
    });

    this.map.addControl(this.drawControl);

    this.map.on('draw:created', this.onDrawCreated);
  }

  /**
   * Positions the map on the best anchor available so far, upgrading if better data has since
   * arrived. Safe to call as often as you like -- planInitialView decides whether anything happens.
   */
  private applyInitialView(): void {
    if (!this.map) return;

    const location = this.userLocation;
    // 0,0 is how an unset location is stored, and the Gulf of Guinea is not a useful opening view.
    const hasUserLocation = !!location && (location.lat !== 0 || location.lng !== 0);

    const plan = planInitialView({
      fittedPriority: this.fittedViewPriority,
      hasAllBounds: !!this.allBoundsRect,
      hasCustomBounds: !!this.customBoundsRect,
      hasSelectionBounds: !!this.selectionBoundsRect,
      hasUserLocation,
      viewLockedByUser: this.viewLockedByUser || !!this.selectedRegion(),
    });

    if (!plan) return;

    switch (plan.source) {
      case 'all':
        this.map.fitBounds(this.allBoundsRect!, FIT_OPTIONS);
        break;
      case 'custom':
        this.map.fitBounds(this.customBoundsRect!, FIT_OPTIONS);
        break;
      case 'location':
        this.map.setView([location!.lat, location!.lng], LOCATION_ONLY_ZOOM);
        break;
      case 'selection':
        this.map.fitBounds(this.selectionBoundsRect!, FIT_OPTIONS);
        break;
    }

    this.fittedViewPriority = plan.priority;
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
  }

  /** Bounds of the fences the user is subscribed to, or null when none of them are in the feed. */
  private computeSelectionBounds(): L.LatLngBounds | null {
    if (this.selectedAreas.length === 0 || this.geofence.length === 0) return null;

    const selectedSet = new Set(this.selectedAreas.map(a => a.toLowerCase()));
    const points: L.LatLngExpression[] = [];

    for (const fence of this.geofence) {
      if (!fence.path || fence.path.length < 3) continue;
      if (!selectedSet.has(fence.name.toLowerCase())) continue;
      points.push(...fence.path.map(coord => [coord[0], coord[1]] as L.LatLngExpression));
    }

    // A selection can name geofences the feed does not carry -- user-drawn fences are served with
    // userSelectable=false and are not in the admin area list -- so an empty result is normal.
    return points.length > 0 ? L.latLngBounds(points) : null;
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

    // Sort geofences by polygon area (largest first) so smaller polygons render
    // on top and are clickable even when nested inside larger ones.
    const sortedFences = [...this.geofence].sort((a, b) => {
      const areaOf = (path: number[][] | undefined): number => {
        if (!path || path.length < 3) return 0;
        let area = 0;
        for (let i = 0, j = path.length - 1; i < path.length; j = i++) {
          area += (path[j][1] + path[i][1]) * (path[j][0] - path[i][0]);
        }
        return Math.abs(area / 2);
      };
      return areaOf(b.path) - areaOf(a.path);
    });

    for (const fence of sortedFences) {
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
        polygon.setStyle({ fillOpacity: 0.4, weight: 3 });
      });
      polygon.on('mouseout', () => {
        polygon.setStyle({ fillOpacity: originalFillOpacity, weight: originalWeight });
      });

      polygon.on('click', () => {
        this.areaClicked.emit(fence.name);
      });

      polygon.addTo(this.map!);
      this.polygonLayers.push(polygon);
      this.polygonByName.set(fence.name, polygon);
    }

    // Ensure smaller polygons are visually and interactively on top by
    // bringing them to the front of the SVG/Canvas layer in reverse order
    // (last bringToFront call wins, so iterate largest-to-smallest which
    // is already the sort order — smallest ends up on top).
    for (const layer of this.polygonLayers) {
      layer.bringToFront();
    }

    if (allBounds.length > 0) {
      this.allBoundsRect = L.latLngBounds(allBounds);
    }

    this.selectionBoundsRect = this.computeSelectionBounds();
    this.updateUserMarker();
    this.applyInitialView();
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

    // Once the user has touched the map, stop repositioning it. Raw DOM input events are used
    // rather than Leaflet's movestart/zoomstart because those fire for our own fitBounds calls too,
    // which would lock the view against the very first fit.
    const container = this.mapElement.nativeElement;
    for (const event of ['pointerdown', 'wheel', 'keydown']) {
      container.addEventListener(event, this.lockViewHandler, { passive: true });
    }

    this.customGeofenceLayer.addTo(this.map);
  }

  private removeDrawControl(): void {
    if (!this.map) return;

    if (this.drawControl) {
      this.map.removeControl(this.drawControl);
      this.drawControl = null;
    }

    this.map.off('draw:created', this.onDrawCreated);
  }

  private renderCustomGeofences(geofences: GeofenceData[]): void {
    if (!this.map) return;
    this.customGeofenceLayer.clearLayers();

    const customBounds: L.LatLngExpression[] = [];

    for (const fence of geofences) {
      if (!fence.path || fence.path.length < 3) continue;

      const latLngs: L.LatLngExpression[] = fence.path.map(coord => [coord[0], coord[1]] as L.LatLngExpression);
      customBounds.push(...latLngs);

      const polygon = L.polygon(latLngs, {
        color: '#2196f3',
        fillColor: '#2196f3',
        fillOpacity: 0.2,
        weight: 2,
      });

      polygon.bindTooltip(fence.name, {
        className: 'area-tooltip',
        direction: 'top',
        sticky: true,
      });

      this.customGeofenceLayer.addLayer(polygon);
    }

    this.customBoundsRect = customBounds.length > 0 ? L.latLngBounds(customBounds) : null;
    this.applyInitialView();
  }

  private updatePolygonStyles(): void {
    if (!this.map) return;

    const selectedSet = new Set(this.selectedAreas.map(a => a.toLowerCase()));

    for (const fence of this.geofence) {
      const polygon = this.polygonByName.get(fence.name);
      if (!polygon) continue;

      const isSelected = selectedSet.has(fence.name.toLowerCase());
      const group = this.groupMapping.get(fence.name) || '';
      const color = this.groupColorMap.get(group) || GROUP_COLORS[0];

      polygon.setStyle({
        color: isSelected ? '#4caf50' : color,
        dashArray: isSelected ? undefined : '5, 5',
        fillColor: isSelected ? '#4caf50' : color,
        fillOpacity: isSelected ? 0.35 : 0.08,
        opacity: isSelected ? 1 : 0.4,
        weight: isSelected ? 3 : 1,
      });

      // Rebind hover handlers with updated base values
      polygon.off('mouseover');
      polygon.off('mouseout');
      const originalWeight = isSelected ? 3 : 1;
      const originalFillOpacity = isSelected ? 0.35 : 0.08;
      polygon.on('mouseover', () => {
        polygon.setStyle({ fillOpacity: 0.4, weight: 3 });
      });
      polygon.on('mouseout', () => {
        polygon.setStyle({ fillOpacity: originalFillOpacity, weight: originalWeight });
      });
    }
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
        .bindTooltip(this.i18n.instant('AREA_MAP.YOUR_LOCATION'), { direction: 'top' })
        .addTo(this.map);

      this.userCircle = L.circle([this.userLocation.lat, this.userLocation.lng], {
        color: '#1976d2',
        dashArray: '5, 5',
        fillColor: '#1976d2',
        fillOpacity: 0.06,
        interactive: false,
        radius: 5000,
        weight: 1.5,
      }).addTo(this.map);
    }
  }
}
