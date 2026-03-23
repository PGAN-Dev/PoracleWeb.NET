import { Component, inject, signal, OnInit, OnDestroy, ElementRef, viewChild, afterNextRender } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import * as L from 'leaflet';
import { Subject } from 'rxjs';
import { debounceTime, switchMap, takeUntil, filter, distinctUntilChanged } from 'rxjs/operators';

import { Location, GeocodingResult } from '../../../core/models';
import { LocationService } from '../../../core/services/location.service';

@Component({
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatSnackBarModule,
    MatProgressBarModule,
    MatAutocompleteModule,
    MatListModule,
  ],
  selector: 'app-location-dialog',
  standalone: true,
  styleUrl: './location-dialog.component.scss',
  templateUrl: './location-dialog.component.html',
})
export class LocationDialogComponent implements OnInit, OnDestroy {
  private readonly destroy$ = new Subject<void>();
  private readonly locationIcon = L.divIcon({
    className: 'location-pin-marker',
    html: '<div style="width:16px;height:16px;background:#1976D2;border:3px solid #fff;border-radius:50%;box-shadow:0 2px 6px rgba(0,0,0,0.4);"></div>',
    iconAnchor: [11, 11],
    iconSize: [22, 22],
  });

  private readonly locationService = inject(LocationService);
  private map: L.Map | null = null;

  private readonly mapContainerRef = viewChild<ElementRef<HTMLElement>>('mapContainer');

  private marker: L.Marker | null = null;
  private readonly search$ = new Subject<string>();
  private skipNextReverse = false;

  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Location | null>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<LocationDialogComponent>);
  latitude = this.data?.latitude ?? 0;
  readonly locating = signal(false);

  longitude = this.data?.longitude ?? 0;
  readonly resolvedAddress = signal<string>('');
  readonly saving = signal(false);
  readonly searching = signal(false);
  searchQuery = '';

  readonly searchResults = signal<GeocodingResult[]>([]);

  constructor() {
    afterNextRender(() => {
      this.initMap();
      if (this.latitude !== 0 || this.longitude !== 0) {
        this.reverseGeocodeCurrentCoords();
      }
    });
  }

  getAddressPrimary(result: GeocodingResult): string {
    const addr = result.address;
    if (!addr) return result.display_name?.split(',')[0] || 'Unknown';

    // Build primary: street address or place name
    const parts: string[] = [];
    if (addr.house_number && addr.road) {
      parts.push(`${addr.house_number} ${addr.road}`);
    } else if (addr.road) {
      parts.push(addr.road);
    } else if ((result as any).name) {
      parts.push((result as any).name);
    }

    const city = addr.city || addr.town || addr.village || '';
    if (city && !parts.includes(city)) parts.push(city);

    return parts.join(', ') || result.display_name?.split(',')[0] || 'Unknown';
  }

  getAddressSecondary(result: GeocodingResult): string {
    const addr = result.address;
    if (!addr) {
      const parts = result.display_name?.split(',') || [];
      return parts.slice(1).join(',').trim();
    }

    const parts: string[] = [];
    const state = addr.state || '';
    const postcode = addr.postcode || '';
    const country = addr.country || '';

    if (state) parts.push(state);
    if (postcode) parts.push(postcode);
    if (country && country !== 'United States') parts.push(country);

    return parts.join(', ');
  }

  getPlaceIcon(result: GeocodingResult): string {
    const type = (result as any).type || '';
    const cls = (result as any).class || '';
    if (cls === 'place' || type === 'city' || type === 'town' || type === 'village') return 'location_city';
    if (cls === 'highway' || type === 'residential' || type === 'road') return 'add_road';
    if (cls === 'building' || type === 'house') return 'home';
    if (cls === 'amenity') return 'store';
    if (cls === 'leisure' || type === 'park') return 'park';
    return 'place';
  }

  isValid(): boolean {
    return (
      !isNaN(this.latitude) &&
      !isNaN(this.longitude) &&
      this.latitude >= -90 &&
      this.latitude <= 90 &&
      this.longitude >= -180 &&
      this.longitude <= 180
    );
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  ngOnInit(): void {
    this.search$
      .pipe(
        debounceTime(500),
        distinctUntilChanged(),
        filter(q => q.trim().length >= 3),
        switchMap(q => {
          this.searching.set(true);
          return this.locationService.geocode(q);
        }),
        takeUntil(this.destroy$),
      )
      .subscribe(results => {
        this.searching.set(false);
        this.searchResults.set(results);
      });
  }

  onCoordsChanged(): void {
    if (this.isValid()) {
      this.updateMap();
      this.reverseGeocodeCurrentCoords();
    }
  }

  onSearchChange(value: string): void {
    this.search$.next(value);
  }

  save(): void {
    if (!this.isValid()) return;

    this.saving.set(true);
    const loc: Location = { latitude: this.latitude, longitude: this.longitude };
    this.locationService.setLocation(loc).subscribe({
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Failed to update location', 'OK', { duration: 3000 });
      },
      next: () => {
        this.saving.set(false);
        this.snackBar.open('Location updated successfully', 'OK', { duration: 3000 });
        this.dialogRef.close(loc);
      },
    });
  }

  selectResult(result: GeocodingResult): void {
    this.latitude = parseFloat(result.lat);
    this.longitude = parseFloat(result.lon);
    this.searchQuery = result.display_name;
    this.resolvedAddress.set(result.display_name);
    this.searchResults.set([]);
    this.skipNextReverse = true;
    this.updateMap();
  }

  useMyLocation(): void {
    if (!navigator.geolocation) {
      this.snackBar.open('Geolocation is not supported by your browser', 'OK', {
        duration: 3000,
      });
      return;
    }

    this.locating.set(true);
    navigator.geolocation.getCurrentPosition(
      position => {
        this.latitude = position.coords.latitude;
        this.longitude = position.coords.longitude;
        this.locating.set(false);
        this.updateMap();
        this.reverseGeocodeCurrentCoords();
      },
      error => {
        this.locating.set(false);
        let msg = 'Unable to get location';
        if (error.code === error.PERMISSION_DENIED) {
          msg = 'Location permission denied';
        }
        this.snackBar.open(msg, 'OK', { duration: 3000 });
      },
      { enableHighAccuracy: true, timeout: 10000 },
    );
  }

  private initMap(): void {
    const el = this.mapContainerRef()?.nativeElement;
    if (!el) return;

    const lat = this.latitude || 0;
    const lng = this.longitude || 0;
    const zoom = lat === 0 && lng === 0 ? 2 : 14;

    this.map = L.map(el, { attributionControl: false, zoomControl: true }).setView([lat, lng], zoom);

    L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
      subdomains: 'abcd',
    }).addTo(this.map);

    if (lat !== 0 || lng !== 0) {
      this.marker = L.marker([lat, lng], { icon: this.locationIcon }).addTo(this.map);
    }

    this.map.on('click', (e: L.LeafletMouseEvent) => {
      this.latitude = parseFloat(e.latlng.lat.toFixed(6));
      this.longitude = parseFloat(e.latlng.lng.toFixed(6));
      this.updateMarker();
      this.reverseGeocodeCurrentCoords();
    });

    // Fix map sizing in dialog
    setTimeout(() => this.map?.invalidateSize(), 100);
  }

  private reverseGeocodeCurrentCoords(): void {
    if (this.skipNextReverse) {
      this.skipNextReverse = false;
      return;
    }
    if (!this.isValid() || (this.latitude === 0 && this.longitude === 0)) return;

    this.locationService.reverseGeocode(this.latitude, this.longitude).subscribe(result => {
      if (result?.display_name) {
        this.resolvedAddress.set(result.display_name);
      } else {
        this.resolvedAddress.set('');
      }
    });
  }

  private updateMap(): void {
    if (!this.map) return;
    const lat = this.latitude;
    const lng = this.longitude;
    this.map.setView([lat, lng], 14);
    this.updateMarker();
  }

  private updateMarker(): void {
    if (!this.map) return;
    const lat = this.latitude;
    const lng = this.longitude;
    if (this.marker) {
      this.marker.setLatLng([lat, lng]);
    } else {
      this.marker = L.marker([lat, lng], { icon: this.locationIcon }).addTo(this.map);
    }
  }
}
