import { AfterViewInit, Component, ElementRef, Input, OnChanges, OnDestroy, SimpleChanges, ViewChild } from '@angular/core';
import * as L from 'leaflet';

import { GeofenceData } from '../../../core/models';

const AREA_COLORS = ['#43a047', '#1e88e5', '#e53935', '#fb8c00', '#8e24aa', '#00acc1', '#f4511e', '#3949ab', '#7cb342', '#d81b60'];

@Component({
  selector: 'app-area-overview-map',
  standalone: true,
  styles: `
    :host {
      display: block;
    }
    .overview-map-container {
      width: 100%;
      height: 200px;
      border-radius: 0 0 12px 12px;
    }
    @media (max-width: 599px) {
      .overview-map-container {
        height: 160px;
      }
    }
  `,
  template: '<div #mapContainer class="overview-map-container"></div>',
})
export class AreaOverviewMapComponent implements AfterViewInit, OnChanges, OnDestroy {
  private map: L.Map | null = null;
  @ViewChild('mapContainer', { static: true }) private mapContainer!: ElementRef<HTMLDivElement>;

  private polygonLayer: L.LayerGroup | null = null;

  @Input() geofence: GeofenceData[] = [];
  @Input() selectedAreas: string[] = [];

  ngAfterViewInit(): void {
    this.map = L.map(this.mapContainer.nativeElement, {
      attributionControl: false,
      boxZoom: false,
      doubleClickZoom: false,
      dragging: false,
      keyboard: false,
      scrollWheelZoom: false,
      touchZoom: false,
      zoomControl: false,
    });

    L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
      maxZoom: 18,
    }).addTo(this.map);

    this.polygonLayer = L.layerGroup().addTo(this.map);
    this.drawPolygons();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.map) return;
    if (changes['geofence'] || changes['selectedAreas']) {
      this.drawPolygons();
    }
  }

  ngOnDestroy(): void {
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  private drawPolygons(): void {
    if (!this.map || !this.polygonLayer) return;
    this.polygonLayer.clearLayers();

    const selected = new Set(this.selectedAreas.map(a => a.toLowerCase()));
    const matching = this.geofence.filter(g => selected.has(g.name.toLowerCase()));

    if (matching.length === 0) return;

    const bounds = L.latLngBounds([]);

    matching.forEach((g, i) => {
      const latlngs = g.path.map(([lat, lon]) => L.latLng(lat, lon));
      const color = AREA_COLORS[i % AREA_COLORS.length];
      const polygon = L.polygon(latlngs, {
        color,
        fillColor: color,
        fillOpacity: 0.25,
        weight: 2,
      });
      polygon.addTo(this.polygonLayer!);
      bounds.extend(polygon.getBounds());
    });

    this.map.fitBounds(bounds, { padding: [20, 20] });
  }
}
