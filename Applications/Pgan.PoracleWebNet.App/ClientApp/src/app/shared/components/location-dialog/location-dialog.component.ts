import { afterNextRender, Component, ElementRef, inject, OnDestroy, OnInit, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe } from '@ngx-translate/core';
import * as L from 'leaflet';
import { Subject } from 'rxjs';
import { debounceTime, switchMap, takeUntil, filter } from 'rxjs/operators';

import { Location, GeocodingResult } from '../../../core/models';
import { BasemapService } from '../../../core/services/basemap.service';
import { I18nService } from '../../../core/services/i18n.service';
import { LocationService } from '../../../core/services/location.service';
import { SettingsService } from '../../../core/services/settings.service';

/** Extra options for {@link LocationDialogComponent}. */
export interface LocationDialogData {
  /** Close with the chosen coordinates instead of saving them as the profile pin. */
  pickOnly?: boolean;
  /** Overrides the title, so a dialog moving a saved place can say which place it is moving. */
  title?: string;
}

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
    TranslatePipe,
  ],
  selector: 'app-location-dialog',
  standalone: true,
  styleUrl: './location-dialog.component.scss',
  templateUrl: './location-dialog.component.html',
})
export class LocationDialogComponent implements OnInit, OnDestroy {
  private readonly basemap = inject(BasemapService);
  private readonly destroy$ = new Subject<void>();
  private readonly i18n = inject(I18nService);

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
  private readonly settingsService = inject(SettingsService);

  private readonly snackBar = inject(MatSnackBar);
  /**
   * `pickOnly` borrows this dialog as a coordinate picker without touching the profile pin. Saving
   * unconditionally is what the dialog was for, so a caller that only wants a point (naming a place,
   * say) would otherwise silently move the user's pin on the way past.
   */
  readonly data = inject<(LocationDialogData & Location) | null>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<LocationDialogComponent>);
  /**
   * When the operator has switched off geocoding, hide the address search rather than let it 403.
   * A feature-disabled 403 makes the error interceptor navigate to /dashboard, which would throw the
   * user out of this dialog mid-edit. Coordinates and the map still work. See #420.
   */
  readonly geocodingDisabled = this.settingsService.isDisabled('disable_nominatim');
  latitude = this.data?.latitude ?? 0;
  /** Shows the coordinate's own error as soon as it is wrong, not only after the field loses focus. */
  readonly latitudeMatcher: ErrorStateMatcher = { isErrorState: () => this.latitudeError() !== null };

  readonly locating = signal(false);
  longitude = this.data?.longitude ?? 0;
  readonly longitudeMatcher: ErrorStateMatcher = { isErrorState: () => this.longitudeError() !== null };
  /** True once a finished search came back empty, until the user types again. */
  readonly noMatches = signal(false);
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

  /**
   * What the search input shows for its model. Picking an option writes the option's value -- the
   * GeocodingResult object -- into the model, which rendered "[object Object]" without this.
   */
  displayResult(value: GeocodingResult | string | null): string {
    if (typeof value === 'string') return value;
    return value?.displayName ?? '';
  }

  /**
   * The landmark's own name when the geocoder returns one ("Toledo Zoo"), otherwise the street address,
   * then the city. Leading with the street listed the zoo as "Lodge Avenue, Toledo", so nothing on the
   * option said it was the zoo.
   */
  getAddressPrimary(result: GeocodingResult): string {
    const parts: string[] = [];
    const landmark = landmarkName(result);
    if (landmark) {
      parts.push(landmark);
    } else {
      const street = streetLine(result);
      if (street) parts.push(street);
      else if (result.name) parts.push(result.name);
    }

    const city = result.city || '';
    if (city && !parts.includes(city)) parts.push(city);

    return parts.join(', ') || result.displayName?.split(',')[0] || 'Unknown';
  }

  /** The street (when a landmark took the first line), then state, postcode and country. */
  getAddressSecondary(result: GeocodingResult): string {
    const parts: string[] = [];
    if (landmarkName(result)) {
      const street = streetLine(result);
      if (street) parts.push(street);
    }
    if (result.state) parts.push(result.state);
    if (result.zipcode) parts.push(result.zipcode);
    if (result.country && result.country !== 'United States') parts.push(result.country);

    return parts.join(', ');
  }

  /**
   * PoracleNG's forward result carries no OSM class/type the way Nominatim's did, so this infers a
   * reasonable icon from what the result itself has rather than reading fields that no longer exist.
   */
  getPlaceIcon(result: GeocodingResult): string {
    if (result.streetNumber) return 'home';
    if (result.streetName) return 'add_road';
    if (result.city) return 'location_city';
    return 'place';
  }

  /**
   * A cleared number input hands the model `null`, which `isNaN` waved through (`isNaN(null)` is false
   * and `null >= -90` is true): Save stayed live, Leaflet threw on the map update and the server answered
   * 400 behind a generic toast. `Number.isFinite` refuses null, empty and NaN alike.
   */
  isValid(): boolean {
    return this.latitudeError() === null && this.longitudeError() === null;
  }

  /** The translation key for what is wrong with the latitude, or null when it is usable. */
  latitudeError(): null | string {
    return inRange(this.latitude, 90) ? null : 'DIALOG.LOCATION_LATITUDE_RANGE';
  }

  /** The translation key for what is wrong with the longitude, or null when it is usable. */
  longitudeError(): null | string {
    return inRange(this.longitude, 180) ? null : 'DIALOG.LOCATION_LONGITUDE_RANGE';
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
        // No distinctUntilChanged: after a pick, typing the same text again is a new search, and the
        // debounce already collapses keystrokes.
        debounceTime(500),
        // Belt and braces with onSearchChange: a non-string here threw on trim() and killed the stream.
        filter(q => typeof q === 'string' && q.trim().length >= 3),
        switchMap(q => {
          this.searching.set(true);
          return this.locationService.geocode(q);
        }),
        takeUntil(this.destroy$),
      )
      .subscribe(results => {
        this.searching.set(false);
        this.searchResults.set(results);
        this.noMatches.set(results.length === 0);
      });
  }

  onCoordsChanged(): void {
    if (this.isValid()) {
      this.updateMap();
      this.reverseGeocodeCurrentCoords();
    }
  }

  /**
   * Picking an option emits the chosen GeocodingResult through `ngModelChange`, not text the user typed.
   * It reached `trim()` and errored the search stream, so no search after the first pick returned
   * anything. Only typed text is a query.
   */
  onSearchChange(value: GeocodingResult | string | null): void {
    if (typeof value === 'string') {
      this.noMatches.set(false);
      this.search$.next(value);
    }
  }

  /**
   * A point chosen on the map. Always looks the new point up: a flag set when a search result was picked
   * used to swallow this lookup, so the dialog kept the search result's address under a pin that had
   * moved away from it.
   */
  pickPoint(lat: number, lng: number): void {
    this.latitude = parseFloat(lat.toFixed(6));
    this.longitude = parseFloat(lng.toFixed(6));
    this.updateMarker();
    this.reverseGeocodeCurrentCoords();
  }

  save(): void {
    if (!this.isValid()) return;

    const loc: Location = { latitude: this.latitude, longitude: this.longitude };

    if (this.data?.pickOnly) {
      this.dialogRef.close(loc);
      return;
    }

    this.saving.set(true);
    this.locationService.setLocation(loc).subscribe({
      error: () => {
        this.saving.set(false);
        this.snackBar.open(this.i18n.instant('DIALOG.LOCATION_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
      },
      next: () => {
        this.saving.set(false);
        this.snackBar.open(this.i18n.instant('DIALOG.LOCATION_SAVE_SUCCESS'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
        this.dialogRef.close(loc);
      },
    });
  }

  selectResult(result: GeocodingResult): void {
    this.latitude = result.latitude;
    this.longitude = result.longitude;
    this.searchQuery = result.displayName;
    this.resolvedAddress.set(result.displayName);
    this.searchResults.set([]);
    this.noMatches.set(false);
    this.updateMap();
  }

  /** The caller's own title, else the picker's or the pin's. A translation key unless the caller passed one. */
  title(): string {
    return this.data?.title || (this.data?.pickOnly ? 'DIALOG.LOCATION_PICK_TITLE' : 'DIALOG.LOCATION_TITLE');
  }

  useMyLocation(): void {
    if (!navigator.geolocation) {
      this.snackBar.open(this.i18n.instant('DIALOG.LOCATION_GEO_UNSUPPORTED'), this.i18n.instant('COMMON.OK'), {
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
        let msg = this.i18n.instant('DIALOG.LOCATION_GEO_UNABLE');
        if (error.code === error.PERMISSION_DENIED) {
          msg = this.i18n.instant('DIALOG.LOCATION_GEO_DENIED');
        }
        this.snackBar.open(msg, this.i18n.instant('COMMON.OK'), { duration: 3000 });
      },
      { enableHighAccuracy: true, timeout: 10000 },
    );
  }

  /** Leaflet makes a marker a focusable role="button"; `title` gives it a name (axe: aria-command-name). */
  private createMarker(lat: number, lng: number): L.Marker {
    const label = this.i18n.instant('AREA_MAP.YOUR_LOCATION');
    return L.marker([lat, lng], { alt: label, icon: this.locationIcon, title: label }).addTo(this.map!);
  }

  private initMap(): void {
    const el = this.mapContainerRef()?.nativeElement;
    if (!el) return;

    const lat = this.latitude || 0;
    const lng = this.longitude || 0;
    const zoom = lat === 0 && lng === 0 ? 2 : 14;

    // attributionControl was off here, so centralising the layer's attribution in #843 still left
    // this map crediting nobody. It is a full-size interactive map, not a thumbnail.
    this.map = L.map(el, { attributionControl: true, zoomControl: true }).setView([lat, lng], zoom);

    this.basemap.attach(this.map, { picker: true });

    if (lat !== 0 || lng !== 0) {
      this.marker = this.createMarker(lat, lng);
    }

    this.map.on('click', (e: L.LeafletMouseEvent) => this.pickPoint(e.latlng.lat, e.latlng.lng));

    // Fix map sizing in dialog
    setTimeout(() => this.map?.invalidateSize(), 100);
  }

  private reverseGeocodeCurrentCoords(): void {
    if (!this.isValid() || (this.latitude === 0 && this.longitude === 0)) return;

    this.locationService.reverseGeocode(this.latitude, this.longitude).subscribe(result => {
      if (result?.displayName) {
        this.resolvedAddress.set(result.displayName);
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
      this.marker = this.createMarker(lat, lng);
    }
  }
}

/** A coordinate is usable when it is a real number within plus or minus `limit`. */
function inRange(value: unknown, limit: number): boolean {
  return typeof value === 'number' && Number.isFinite(value) && value >= -limit && value <= limit;
}

function streetLine(result: GeocodingResult): string {
  if (result.streetNumber && result.streetName) return `${result.streetNumber} ${result.streetName}`;
  return result.streetName ?? '';
}

/** A name that is the place's own rather than the street or city again ("Toledo Zoo", not "Lodge Avenue"). */
function landmarkName(result: GeocodingResult): string {
  const name = result.name?.trim();
  if (!name) return '';
  const lower = name.toLowerCase();
  const repeats = [result.streetName, streetLine(result), result.city].some(v => v?.trim().toLowerCase() === lower);
  return repeats ? '' : name;
}
