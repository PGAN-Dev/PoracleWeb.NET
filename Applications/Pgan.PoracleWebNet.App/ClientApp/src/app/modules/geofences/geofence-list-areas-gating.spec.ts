import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';
import { of } from 'rxjs';

import { GeofenceListComponent } from './geofence-list.component';
import { UserGeofence } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { LocationService } from '../../core/services/location.service';
import { SettingsService } from '../../core/services/settings.service';
import { UserGeofenceService } from '../../core/services/user-geofence.service';

/**
 * My Geofences read the profile's area list and offered a per-profile on/off switch on every card,
 * whatever `disable_areas` said. The switch writes area subscriptions, which that setting refuses, so
 * it could only ever end in a 403 and a toast; and the list read had nothing left to feed.
 */
describe('GeofenceListComponent under disable_areas', () => {
  const fence = {
    id: 7,
    createdAt: '2026-09-01T00:00:00Z',
    displayName: 'Backyard',
    groupName: 'Richmond',
    kojiName: 'backyard',
    polygon: [
      [37.5, -77.4],
      [37.51, -77.4],
      [37.51, -77.41],
    ],
    status: 'active',
  } as unknown as UserGeofence;

  const setup = (disabled: string[]) => {
    const areaService = {
      getAvailable: jest.fn(() => of([])),
      getGeofencePolygons: jest.fn(() => of([])),
      getSelected: jest.fn(() => of(['backyard'])),
    };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        { provide: LocationService, useValue: { getLocation: jest.fn(() => of({ latitude: 0, longitude: 0 })) } },
        { provide: SettingsService, useValue: { isDisabled: (key: string) => disabled.includes(key), siteSettings: signal({}) } },
        { provide: AreaService, useValue: areaService },
        { provide: UserGeofenceService, useValue: { getCustomGeofences: () => of([fence]), getRegions: () => of([]) } },
      ],
    });
    jest.spyOn(L.Map.prototype, 'getSize').mockReturnValue(L.point(1200, 400));

    const fixture = TestBed.createComponent(GeofenceListComponent);
    fixture.detectChanges();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { areaService, el };
  };

  afterEach(() => jest.restoreAllMocks());

  it('does not read the area list, and offers no per-profile switch', () => {
    const { areaService, el } = setup(['disable_areas']);

    expect(areaService.getSelected).not.toHaveBeenCalled();
    expect(el.querySelector('.geofence-card-toggle')).toBeNull();
    // The geofence itself is still listed, and the map overlay still loads.
    expect(el.textContent).toContain('Backyard');
    expect(areaService.getAvailable).toHaveBeenCalled();
  });

  it('reads the area list and offers the switch while areas are enabled', () => {
    const { areaService, el } = setup([]);

    expect(areaService.getSelected).toHaveBeenCalled();
    expect(el.querySelector('.geofence-card-toggle')).not.toBeNull();
  });
});
