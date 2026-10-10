import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AreaListComponent } from './area-list.component';
import { AreaService } from '../../core/services/area.service';
import { LocationService } from '../../core/services/location.service';
import { PlacesService } from '../../core/services/places.service';
import { SettingsService } from '../../core/services/settings.service';

/**
 * With `disable_location` on, the Areas page kept its pin card and the Places section: "No pin set" for a
 * user who has one, a Set button leading to a dialog whose save 403s, and two GETs that 403 with a toast.
 * The areas half of the page is a separate feature and must keep working.
 */
describe('AreaListComponent location gating', () => {
  const setup = (disabled: string[]) => {
    const settings = signal<Record<string, string>>(Object.fromEntries(disabled.map(k => [k, 'true'])));
    const location = {
      getLocation: jest.fn(() => of({ latitude: 37.5, longitude: -77.4 })),
      getStaticMapUrl: jest.fn(() => of(null)),
      reverseGeocode: jest.fn(() => of({ displayName: '1 Main St' })),
    };
    const places = {
      named: computed(() => []),
      canEdit: computed(() => false),
      load: jest.fn(() => of({ named: [] })),
      pin: computed(() => null),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        { provide: LocationService, useValue: location },
        { provide: PlacesService, useValue: places },
        { provide: SettingsService, useValue: { isDisabled: (key: string) => settings()[key] === 'true', siteSettings: settings } },
        {
          provide: AreaService,
          useValue: {
            getAvailable: () => of([{ name: 'Downtown', group: 'City', userSelectable: true }]),
            getGeofencePolygons: () => of([]),
            getMapUrl: () => of(null),
            getSelected: () => of(['downtown']),
          },
        },
      ],
      imports: [AreaListComponent, NoopAnimationsModule],
    });

    const fixture = TestBed.createComponent(AreaListComponent);
    fixture.detectChanges();
    return { el: fixture.nativeElement as HTMLElement, fixture, location, places };
  };

  it('shows the pin card and places, and reads the pin, while location is enabled', () => {
    const { el, location, places } = setup([]);

    expect(location.getLocation).toHaveBeenCalled();
    expect(location.reverseGeocode).toHaveBeenCalledWith(37.5, -77.4);
    expect(places.load).toHaveBeenCalled();
    expect(el.querySelector('.method-location')).not.toBeNull();
    expect(el.querySelector('app-places-section')).not.toBeNull();
  });

  it('hides the pin card and places, and requests neither, when location is disabled', () => {
    const { el, location, places } = setup(['disable_location']);

    expect(location.getLocation).not.toHaveBeenCalled();
    expect(places.load).not.toHaveBeenCalled();
    expect(el.querySelector('.method-location')).toBeNull();
    expect(el.querySelector('app-places-section')).toBeNull();
  });

  it('keeps the area selection working when location is disabled', () => {
    const { el, fixture } = setup(['disable_location']);

    expect(fixture.componentInstance.loading()).toBe(false);
    expect(fixture.componentInstance.selectedAreas()).toEqual(['downtown']);
    expect(el.querySelector('.method-areas')).not.toBeNull();
  });

  it('shows the pin without an address lookup when only geocoding is disabled', () => {
    const { el, location } = setup(['disable_nominatim']);

    expect(location.getLocation).toHaveBeenCalled();
    expect(location.reverseGeocode).not.toHaveBeenCalled();
    expect(el.querySelector('.method-location')?.textContent).toContain('37.5000');
  });
});
