import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';
import { Observable, of, throwError } from 'rxjs';

import { GeofenceListComponent } from './geofence-list.component';
import { GeofenceDeleteImpact, UserGeofence } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { LocationService } from '../../core/services/location.service';
import { SettingsService } from '../../core/services/settings.service';
import { UserGeofenceService } from '../../core/services/user-geofence.service';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { GeofenceDeleteDialogComponent } from '../../shared/components/geofence-delete-dialog/geofence-delete-dialog.component';

/**
 * Deleting a fence also clears it from every alarm's override areas, and an alarm whose only override was
 * this fence would then match across the profile's areas. The page asks first, and only when there is
 * something to ask about.
 */
describe('GeofenceListComponent delete flow', () => {
  const fence: UserGeofence = {
    id: 9,
    createdAt: '2026-10-21T00:00:00Z',
    displayName: 'My Park',
    groupName: 'g',
    humanId: 'u1',
    kojiName: 'my park',
    parentId: 1,
    status: 'active',
    updatedAt: '2026-10-21T00:00:00Z',
  };

  const setup = (impact: Observable<GeofenceDeleteImpact>, dialogAnswer: unknown) => {
    const service = {
      deleteGeofence: jest.fn(() => of(undefined)),
      getCustomGeofences: () => of([fence]),
      getDeleteImpact: jest.fn(() => impact),
      getRegions: () => of([]),
    };
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
        { provide: UserGeofenceService, useValue: service },
      ],
    });
    jest.spyOn(L.Map.prototype, 'getSize').mockReturnValue(L.point(1200, 400));
    const fixture = TestBed.createComponent(GeofenceListComponent);
    const open = jest
      .spyOn(fixture.debugElement.injector.get(MatDialog), 'open')
      .mockReturnValue({ afterClosed: () => of(dialogAnswer) } as never);
    const snackOpen = jest.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open').mockReturnValue({} as never);
    fixture.detectChanges();
    return { component: fixture.componentInstance, open, service, snackOpen };
  };

  afterEach(() => jest.restoreAllMocks());

  it('uses the plain confirm when nothing would widen, then deletes', async () => {
    const { component, open, service } = setup(of({ widened: {}, total: 0 }), true);

    await component.deleteGeofence(fence);

    expect(service.getDeleteImpact).toHaveBeenCalledWith(9);
    expect(open).toHaveBeenCalledTimes(1);
    expect(open.mock.calls[0][0]).toBe(ConfirmDialogComponent);
    expect(service.deleteGeofence).toHaveBeenCalledWith(9, 'delete');
    expect(component.customGeofences()).toEqual([]);
  });

  it('asks about the widened alarms and deletes them by default', async () => {
    const { component, open, service } = setup(of({ widened: { raid: 1, pokemon: 2 }, total: 3 }), 'delete');

    await component.deleteGeofence(fence);

    expect(open).toHaveBeenCalledTimes(1);
    expect(open.mock.calls[0][0]).toBe(GeofenceDeleteDialogComponent);
    expect((open.mock.calls[0][1] as { data: unknown }).data).toEqual({ name: 'My Park', count: 3 });
    expect(service.deleteGeofence).toHaveBeenCalledWith(9, 'delete');
  });

  it('sends keep when the user keeps the alarms', async () => {
    const { component, service } = setup(of({ widened: { pokemon: 3 }, total: 3 }), 'keep');

    await component.deleteGeofence(fence);

    expect(service.deleteGeofence).toHaveBeenCalledWith(9, 'keep');
  });

  it('deletes nothing when the choice is cancelled', async () => {
    const { component, service } = setup(of({ widened: { pokemon: 3 }, total: 3 }), null);

    await component.deleteGeofence(fence);

    expect(service.deleteGeofence).not.toHaveBeenCalled();
    expect(component.customGeofences()).toEqual([fence]);
  });

  it('deletes nothing and says so when the impact cannot be read', async () => {
    const { component, open, service, snackOpen } = setup(
      throwError(() => new Error('503')),
      true,
    );

    await component.deleteGeofence(fence);

    expect(open).not.toHaveBeenCalled();
    expect(service.deleteGeofence).not.toHaveBeenCalled();
    expect(snackOpen.mock.calls[0][0]).toBe('GEOFENCES.SNACK_FAILED_DELETE');
  });
});
