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
import { AreaNameService } from '../../core/services/area-name.service';
import { AreaService } from '../../core/services/area.service';
import { LocationService } from '../../core/services/location.service';
import { SettingsService } from '../../core/services/settings.service';
import { UserGeofenceService } from '../../core/services/user-geofence.service';

/**
 * A new fence is saved before PoracleNG is asked to switch it on. When that second step fails the API
 * still answers with the fence and `activationFailed: true`; the toggle must stay off and the snackbar
 * must say so, rather than the fence appearing active when it is not.
 */
describe('GeofenceListComponent create flow', () => {
  const polygon: [number, number][] = [
    [37.5, -77.4],
    [37.51, -77.4],
    [37.51, -77.41],
  ];

  const setup = (created: Partial<UserGeofence>) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: LocationService, useValue: { getLocation: jest.fn(() => of({ latitude: 0, longitude: 0 })) } },
        { provide: SettingsService, useValue: { isDisabled: () => false, siteSettings: signal({}) } },
        {
          provide: AreaService,
          useValue: { getAvailable: jest.fn(() => of([])), getGeofencePolygons: jest.fn(() => of([])), getSelected: jest.fn(() => of([])) },
        },
        {
          provide: UserGeofenceService,
          useValue: {
            createGeofence: jest.fn(() => of({ id: 9, displayName: 'Park', kojiName: 'park', status: 'active', ...created })),
            getCustomGeofences: () => of([]),
            getRegions: () => of([]),
          },
        },
      ],
    });
    jest.spyOn(L.Map.prototype, 'getSize').mockReturnValue(L.point(1200, 400));
    const fixture = TestBed.createComponent(GeofenceListComponent);
    // The component gets its own MatDialog and MatSnackBar from its imports, so spy on those instances.
    jest
      .spyOn(fixture.debugElement.injector.get(MatDialog), 'open')
      .mockReturnValue({ afterClosed: () => of({ displayName: 'Park', groupName: 'g', parentId: 1 }) } as never);
    const snackOpen = jest.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open').mockReturnValue({} as never);
    fixture.detectChanges();
    return { component: fixture.componentInstance, snackOpen };
  };

  afterEach(() => jest.restoreAllMocks());

  it('leaves the toggle off and says so when the fence could not be switched on', () => {
    const { component, snackOpen } = setup({ activationFailed: true });

    component.onDrawComplete(polygon);

    expect(component.customGeofences().map(g => g.kojiName)).toEqual(['park']);
    expect(component.activeAreas()).not.toContain('park');
    expect(snackOpen).toHaveBeenCalledTimes(1);
    expect(snackOpen.mock.calls[0][0]).toBe('GEOFENCES.SNACK_SAVED_NOT_ACTIVE');
  });

  it('switches the toggle on and shows only the created message for a normal create', () => {
    const { component, snackOpen } = setup({});

    component.onDrawComplete(polygon);

    expect(component.activeAreas()).toContain('park');
    expect(snackOpen).toHaveBeenCalledTimes(1);
    expect(snackOpen.mock.calls[0][0]).toBe('GEOFENCES.SNACK_CREATED');
  });

  it('tells the chips the new fence own name', () => {
    const { component } = setup({ displayName: 'Lake Eola Loop', kojiName: 'lake eola loop 2' });

    component.onDrawComplete(polygon);

    expect(TestBed.inject(AreaNameService).label('lake eola loop 2')).toBe('Lake Eola Loop');
  });
});
