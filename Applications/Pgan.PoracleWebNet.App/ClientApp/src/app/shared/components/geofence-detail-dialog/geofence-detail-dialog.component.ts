import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, ViewChild, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import * as L from 'leaflet';

import { GeofenceData, UserGeofence } from '../../../core/models';
import { GEOFENCE_STATUS_COLORS } from '../../utils/geofence.utils';

export interface GeofenceDetailDialogData {
  geofence: UserGeofence;
  referenceGeofences?: GeofenceData[];
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MatButtonModule, MatChipsModule, MatDialogModule, MatIconModule],
  selector: 'app-geofence-detail-dialog',
  standalone: true,
  styleUrl: './geofence-detail-dialog.component.scss',
  templateUrl: './geofence-detail-dialog.component.html',
})
export class GeofenceDetailDialogComponent implements OnDestroy {
  private map: L.Map | null = null;

  readonly data = inject<GeofenceDetailDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<GeofenceDetailDialogComponent>);

  @ViewChild('detailMap', { static: true }) mapElement!: ElementRef<HTMLDivElement>;

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
        return 'Active';
      case 'approved':
        return 'Approved';
      case 'pending_review':
        return 'Pending Review';
      case 'rejected':
        return 'Rejected';
      default:
        return this.geofence.status;
    }
  }

  constructor() {
    // Init map only after dialog open animation completes and container has final dimensions
    this.dialogRef.afterOpened().subscribe(() => {
      this.initMap();
    });
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

    // Draw region/area geofences from Poracle as muted reference context
    for (const ref of this.data.referenceGeofences ?? []) {
      if (ref.path && ref.path.length >= 3) {
        const refLatLngs: L.LatLngExpression[] = ref.path.map(coord => [coord[0], coord[1]] as L.LatLngExpression);
        L.polygon(refLatLngs, {
          color: '#78909c',
          dashArray: '4 6',
          fillColor: '#78909c',
          fillOpacity: 0.04,
          weight: 1.5,
        })
          .bindTooltip(ref.name, { sticky: true })
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
