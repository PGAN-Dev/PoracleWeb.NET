import {
  Component,
  inject,
  signal,
  OnInit,
  OnDestroy,
  ElementRef,
  viewChild,
  afterNextRender,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatListModule } from '@angular/material/list';
import { LocationService } from '../../../core/services/location.service';
import { Location, GeocodingResult } from '../../../core/models';
import { Subject } from 'rxjs';
import { debounceTime, switchMap, takeUntil, filter, distinctUntilChanged } from 'rxjs/operators';
import * as L from 'leaflet';

@Component({
  selector: 'app-location-dialog',
  standalone: true,
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
  template: `
    <h2 mat-dialog-title>Set Location</h2>
    @if (saving()) {
      <mat-progress-bar mode="indeterminate"></mat-progress-bar>
    }
    <mat-dialog-content>
      <!-- Address Search -->
      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Search address</mat-label>
        <mat-icon matPrefix>search</mat-icon>
        <input
          matInput
          [matAutocomplete]="auto"
          [(ngModel)]="searchQuery"
          (ngModelChange)="onSearchChange($event)"
          placeholder="Type an address to search..."
        />
        @if (searching()) {
          <mat-icon matSuffix class="spin">sync</mat-icon>
        }
        <mat-autocomplete #auto="matAutocomplete" (optionSelected)="selectResult($event.option.value)" class="address-autocomplete">
          @for (result of searchResults(); track $index) {
            <mat-option [value]="result" class="address-option">
              <div class="address-option-content">
                <mat-icon class="address-option-icon">{{ getPlaceIcon(result) }}</mat-icon>
                <div class="address-option-text">
                  <span class="address-primary">{{ getAddressPrimary(result) }}</span>
                  <span class="address-secondary">{{ getAddressSecondary(result) }}</span>
                </div>
              </div>
            </mat-option>
          }
          @if (searchResults().length === 0 && searching()) {
            <mat-option disabled>
              <span class="autocomplete-option">Searching...</span>
            </mat-option>
          }
        </mat-autocomplete>
      </mat-form-field>

      <!-- Lat/Lng Fields -->
      <div class="location-form">
        <mat-form-field appearance="outline">
          <mat-label>Latitude</mat-label>
          <input matInput type="number" [(ngModel)]="latitude" step="0.000001" (change)="onCoordsChanged()" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Longitude</mat-label>
          <input matInput type="number" [(ngModel)]="longitude" step="0.000001" (change)="onCoordsChanged()" />
        </mat-form-field>
      </div>

      <!-- Resolved Address Display -->
      @if (resolvedAddress()) {
        <div class="resolved-address">
          <mat-icon>place</mat-icon>
          <span>{{ resolvedAddress() }}</span>
        </div>
      }

      <!-- Mini Map -->
      <div #mapContainer class="mini-map"></div>

      <!-- Use My Location -->
      <button
        mat-stroked-button
        color="primary"
        (click)="useMyLocation()"
        [disabled]="locating()"
        class="geolocate-btn"
      >
        <mat-icon>my_location</mat-icon>
        {{ locating() ? 'Getting location...' : 'Use My Location' }}
      </button>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button
        mat-raised-button
        color="primary"
        (click)="save()"
        [disabled]="saving() || !isValid()"
      >
        <mat-icon>save</mat-icon> Save
      </button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      .full-width {
        width: 100%;
      }
      .location-form {
        display: flex;
        gap: 16px;
        margin-bottom: 8px;
      }
      .location-form mat-form-field {
        flex: 1;
      }
      .resolved-address {
        display: flex;
        align-items: flex-start;
        gap: 8px;
        padding: 8px 12px;
        margin-bottom: 12px;
        background: rgba(33, 150, 243, 0.08);
        border-radius: 8px;
        font-size: 13px;
        color: var(--text-secondary, rgba(0, 0, 0, 0.7));
        line-height: 1.4;
      }
      .resolved-address mat-icon {
        font-size: 18px;
        width: 18px;
        height: 18px;
        color: #1565c0;
        flex-shrink: 0;
        margin-top: 1px;
      }
      .mini-map {
        width: 100%;
        height: 200px;
        border-radius: 8px;
        margin-bottom: 12px;
        border: 1px solid var(--divider, rgba(0, 0, 0, 0.12));
        z-index: 0;
      }
      .geolocate-btn {
        width: 100%;
        margin-bottom: 8px;
      }
      .address-option-content {
        display: flex;
        align-items: flex-start;
        gap: 10px;
        padding: 4px 0;
      }
      .address-option-icon {
        font-size: 20px;
        width: 20px;
        height: 20px;
        color: #1565c0;
        flex-shrink: 0;
        margin-top: 2px;
      }
      .address-option-text {
        display: flex;
        flex-direction: column;
        min-width: 0;
      }
      .address-primary {
        font-size: 14px;
        font-weight: 500;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }
      .address-secondary {
        font-size: 12px;
        color: var(--text-secondary, rgba(0,0,0,0.54));
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }
      .autocomplete-option {
        font-size: 13px;
        white-space: normal;
        line-height: 1.3;
      }
      :host ::ng-deep .address-option {
        height: auto !important;
        min-height: 48px;
        line-height: normal !important;
      }
      .spin {
        animation: spin 1s linear infinite;
      }
      @keyframes spin {
        100% {
          transform: rotate(360deg);
        }
      }
    `,
  ],
})
export class LocationDialogComponent implements OnInit, OnDestroy {
  private readonly locationService = inject(LocationService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Location | null>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<LocationDialogComponent>);

  private readonly mapContainerRef = viewChild<ElementRef<HTMLElement>>('mapContainer');

  latitude = this.data?.latitude ?? 0;
  longitude = this.data?.longitude ?? 0;
  searchQuery = '';

  readonly saving = signal(false);
  readonly locating = signal(false);
  readonly searching = signal(false);
  readonly searchResults = signal<GeocodingResult[]>([]);
  readonly resolvedAddress = signal<string>('');

  private map: L.Map | null = null;
  private marker: L.Marker | null = null;
  private readonly destroy$ = new Subject<void>();
  private readonly search$ = new Subject<string>();
  private skipNextReverse = false;

  constructor() {
    afterNextRender(() => {
      this.initMap();
      if (this.latitude !== 0 || this.longitude !== 0) {
        this.reverseGeocodeCurrentCoords();
      }
    });
  }

  ngOnInit(): void {
    this.search$
      .pipe(
        debounceTime(500),
        distinctUntilChanged(),
        filter((q) => q.trim().length >= 3),
        switchMap((q) => {
          this.searching.set(true);
          return this.locationService.geocode(q);
        }),
        takeUntil(this.destroy$),
      )
      .subscribe((results) => {
        this.searching.set(false);
        this.searchResults.set(results);
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  onSearchChange(value: string): void {
    this.search$.next(value);
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

  onCoordsChanged(): void {
    if (this.isValid()) {
      this.updateMap();
      this.reverseGeocodeCurrentCoords();
    }
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
      (position) => {
        this.latitude = position.coords.latitude;
        this.longitude = position.coords.longitude;
        this.locating.set(false);
        this.updateMap();
        this.reverseGeocodeCurrentCoords();
      },
      (error) => {
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

  save(): void {
    if (!this.isValid()) return;

    this.saving.set(true);
    const loc: Location = { latitude: this.latitude, longitude: this.longitude };
    this.locationService.setLocation(loc).subscribe({
      next: () => {
        this.saving.set(false);
        this.snackBar.open('Location updated successfully', 'OK', { duration: 3000 });
        this.dialogRef.close(loc);
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Failed to update location', 'OK', { duration: 3000 });
      },
    });
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

  private initMap(): void {
    const el = this.mapContainerRef()?.nativeElement;
    if (!el) return;

    const lat = this.latitude || 0;
    const lng = this.longitude || 0;
    const zoom = lat === 0 && lng === 0 ? 2 : 14;

    this.map = L.map(el, { zoomControl: true, attributionControl: false }).setView(
      [lat, lng],
      zoom,
    );

    L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
      subdomains: 'abcd',
    }).addTo(this.map);

    if (lat !== 0 || lng !== 0) {
      this.marker = L.marker([lat, lng]).addTo(this.map);
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
      this.marker = L.marker([lat, lng]).addTo(this.map);
    }
  }

  private reverseGeocodeCurrentCoords(): void {
    if (this.skipNextReverse) {
      this.skipNextReverse = false;
      return;
    }
    if (!this.isValid() || (this.latitude === 0 && this.longitude === 0)) return;

    this.locationService.reverseGeocode(this.latitude, this.longitude).subscribe((result) => {
      if (result?.display_name) {
        this.resolvedAddress.set(result.display_name);
      } else {
        this.resolvedAddress.set('');
      }
    });
  }
}
