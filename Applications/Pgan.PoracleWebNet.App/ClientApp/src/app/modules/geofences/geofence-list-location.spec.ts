import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';
import { Observable, of, throwError } from 'rxjs';

import { GeofenceListComponent } from './geofence-list.component';
import { Location } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { LocationService } from '../../core/services/location.service';
import { SettingsService } from '../../core/services/settings.service';
import { UserGeofenceService } from '../../core/services/user-geofence.service';
import { AreaMapComponent } from '../../shared/components/area-map/area-map.component';

/**
 * The Areas map opens on the user's pin; My Geofences opened on the bounds of the whole feed, which on
 * a multi-region instance is the planet. The map already knows how to open on a pin -- it was never
 * given one here.
 */
describe('GeofenceListComponent map location', () => {
  const setup = (location: () => Observable<Location>) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        { provide: LocationService, useValue: { getLocation: jest.fn(location) } },
        { provide: SettingsService, useValue: { isDisabled: () => false, siteSettings: signal({}) } },
        {
          provide: AreaService,
          useValue: { getAvailable: () => of([]), getGeofencePolygons: () => of([]), getSelected: () => of([]) },
        },
        { provide: UserGeofenceService, useValue: { getCustomGeofences: () => of([]), getRegions: () => of([]) } },
      ],
    });
    jest.spyOn(L.Map.prototype, 'getSize').mockReturnValue(L.point(1200, 400));

    const fixture = TestBed.createComponent(GeofenceListComponent);
    fixture.detectChanges();
    fixture.detectChanges();
    return fixture.debugElement.query(By.directive(AreaMapComponent)).componentInstance as AreaMapComponent;
  };

  afterEach(() => jest.restoreAllMocks());

  it('hands the map the user pin', () => {
    const map = setup(() => of({ latitude: 37.54, longitude: -77.43 }));

    expect(map.userLocation).toEqual({ lat: 37.54, lng: -77.43 });
  });

  it('hands the map nothing for a cleared 0,0 pin', () => {
    expect(setup(() => of({ latitude: 0, longitude: 0 })).userLocation).toBeUndefined();
  });

  it('still renders the map when the pin cannot be read', () => {
    expect(setup(() => throwError(() => new Error('403'))).userLocation).toBeUndefined();
  });
});
