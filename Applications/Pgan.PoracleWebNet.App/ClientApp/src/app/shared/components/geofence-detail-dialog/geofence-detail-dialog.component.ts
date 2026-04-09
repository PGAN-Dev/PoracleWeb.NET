import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, ViewChild, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule } from '@ngx-translate/core';
import * as L from 'leaflet';

import { GeofenceData, UserGeofence } from '../../../core/models';
import { I18nService } from '../../../core/services/i18n.service';
import { polygonAreaSqKm } from '../../utils/geo.utils';
import { GEOFENCE_STATUS_COLORS } from '../../utils/geofence.utils';

const AREA_COLORS = [
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

export interface GeofenceDetailDialogData {
  geofence: UserGeofence;
  referenceGeofences?: GeofenceData[];
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MatButtonModule, MatChipsModule, MatDialogModule, MatIconModule, TranslateModule],
  selector: 'app-geofence-detail-dialog',
  standalone: true,
  styleUrl: './geofence-detail-dialog.component.scss',
  templateUrl: './geofence-detail-dialog.component.html',
})
export class GeofenceDetailDialogComponent implements OnDestroy {
  private readonly dialogRef = inject(MatDialogRef<GeofenceDetailDialogComponent>);
  private readonly i18n = inject(I18nService);

  private map: L.Map | null = null;
  readonly data = inject<GeofenceDetailDialogData>(MAT_DIALOG_DATA);

  @ViewChild('detailMap', { static: true }) mapElement!: ElementRef<HTMLDivElement>;

  constructor() {
    // Init map only after dialog open animation completes and container has final dimensions
    this.dialogRef.afterOpened().subscribe(() => {
      this.initMap();
    });
  }

  get areaDisplay(): string {
    if (!this.geofence.polygon || this.geofence.polygon.length < 3) return '0 m²';
    const sqKm = polygonAreaSqKm(this.geofence.polygon as [number, number][]);
    if (sqKm < 1) {
      const sqM = Math.round(sqKm * 1_000_000);
      return `${sqM.toLocaleString()} m²`;
    }
    return `${sqKm.toFixed(2)} km²`;
  }

  get geofence(): UserGeofence {
    return this.data.geofence;
  }

  get pointCount(): number {
    return this.geofence.pointCount ?? this.geofence.polygon?.length ?? 0;
  }

  get statusColor(): string {
    return GEOFENCE_STATUS_COLORS[this.geofence.status] || '#9e9e9e';
  }

  get statusLabel(): string {
    switch (this.geofence.status) {
      case 'active':
        return this.i18n.instant('GEOFENCE_DETAIL.STATUS_ACTIVE');
      case 'approved':
        return this.i18n.instant('GEOFENCE_DETAIL.STATUS_APPROVED');
      case 'pending_review':
        return this.i18n.instant('GEOFENCE_DETAIL.STATUS_PENDING');
      case 'rejected':
        return this.i18n.instant('GEOFENCE_DETAIL.STATUS_REJECTED');
      default:
        return this.geofence.status;
    }
  }

  ngOnDestroy(): void {
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  private initMap(): void {
    const el = this.mapElement.nativeElement;

    this.map = L.map(el, {
      attributionControl: true,
      zoomControl: true,
    }).setView([0, 0], 2);

    L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
      attribution: '&copy; <a href="https://carto.com/">CARTO</a> &copy; <a href="https://www.openstreetmap.org/copyright">OSM</a>',
      maxZoom: 19,
      subdomains: 'abcd',
    }).addTo(this.map);

    // Draw region/area geofences from Poracle using the same color palette as area-map
    const refs = this.data.referenceGeofences ?? [];
    for (let i = 0; i < refs.length; i++) {
      const ref = refs[i];
      if (ref.path && ref.path.length >= 3) {
        const color = AREA_COLORS[i % AREA_COLORS.length];
        const refLatLngs: L.LatLngExpression[] = ref.path.map(coord => [coord[0], coord[1]] as L.LatLngExpression);
        L.polygon(refLatLngs, {
          color,
          dashArray: '5, 5',
          fillColor: color,
          fillOpacity: 0.08,
          opacity: 0.4,
          weight: 1,
        })
          .bindTooltip(ref.name, { className: 'area-tooltip', direction: 'top', sticky: true })
          .addTo(this.map);
      }
    }

    // Draw the selected geofence on top in full status color
    if (this.geofence.polygon && this.geofence.polygon.length >= 3) {
      const color = GEOFENCE_STATUS_COLORS[this.geofence.status] || '#9e9e9e';
      const latLngs: L.LatLngExpression[] = this.geofence.polygon.map(coord => [coord[0], coord[1]] as L.LatLngExpression);

      const polygon = L.polygon(latLngs, {
        color,
        fillColor: color,
        fillOpacity: 0.2,
        weight: 2,
      });

      polygon.addTo(this.map);
      this.map.fitBounds(polygon.getBounds(), { padding: [30, 30] });
    }

    // Final invalidateSize after a tick to ensure tiles load correctly
    setTimeout(() => this.map?.invalidateSize(), 50);
  }
}
