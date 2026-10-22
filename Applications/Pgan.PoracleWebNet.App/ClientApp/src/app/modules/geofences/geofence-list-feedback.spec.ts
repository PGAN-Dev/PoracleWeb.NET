import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';
import { of, Subject, throwError } from 'rxjs';

import { GeofenceListComponent } from './geofence-list.component';
import { GeoJsonImportResult, UserGeofence } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { LocationService } from '../../core/services/location.service';
import { SettingsService } from '../../core/services/settings.service';
import { UserGeofenceService } from '../../core/services/user-geofence.service';

/**
 * What the geofence page tells the user when something goes wrong, and when the import dialog closes.
 */
describe('GeofenceListComponent feedback', () => {
  const fence: UserGeofence = {
    id: 9,
    createdAt: '2026-10-21T00:00:00Z',
    displayName: 'Lake Eola Loop',
    groupName: 'g',
    humanId: 'u1',
    kojiName: 'lake eola loop',
    parentId: 1,
    status: 'active',
    updatedAt: '2026-10-21T00:00:00Z',
  };

  const refused = (status: number, error: unknown) => throwError(() => new HttpErrorResponse({ error, status }));

  const setup = (overrides: Record<string, jest.Mock> = {}, dialogRef: Record<string, unknown> = {}) => {
    const service = {
      renameGeofence: jest.fn(() => of(fence)),
      activateGeofence: jest.fn(() => of(undefined)),
      deactivateGeofence: jest.fn(() => of(undefined)),
      getCustomGeofences: jest.fn(() => of([fence])),
      getRegions: () => of([]),
      ...overrides,
    };
    const areas = { getAvailable: jest.fn(() => of([])), getGeofencePolygons: jest.fn(() => of([])), getSelected: jest.fn(() => of([])) };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: LocationService, useValue: { getLocation: jest.fn(() => of({ latitude: 0, longitude: 0 })) } },
        { provide: SettingsService, useValue: { isDisabled: () => false, siteSettings: signal({}) } },
        { provide: AreaService, useValue: areas },
        { provide: UserGeofenceService, useValue: service },
      ],
    });
    jest.spyOn(L.Map.prototype, 'getSize').mockReturnValue(L.point(1200, 400));
    const fixture = TestBed.createComponent(GeofenceListComponent);
    const open = jest.spyOn(fixture.debugElement.injector.get(MatDialog), 'open').mockReturnValue({
      afterClosed: () => of({ displayName: 'Lake Eola Loop 2', groupName: 'g', parentId: 1 }),
      componentInstance: { displayName: '', selectedRegionId: null },
      ...dialogRef,
    } as never);
    const snackOpen = jest.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open').mockReturnValue({} as never);
    fixture.detectChanges();
    return { areas, component: fixture.componentInstance, open, service, snackOpen };
  };

  afterEach(() => jest.restoreAllMocks());

  describe('failures carry the server reason', () => {
    it('shows why a fence could not be switched on', () => {
      const reason = "Geofence 'lake eola loop 2' could not be switched on yet. Try again.";
      const { component, snackOpen } = setup({ activateGeofence: jest.fn(() => refused(409, { error: reason })) });

      component.toggleGeofenceProfile(fence);

      expect(snackOpen.mock.calls[0][0]).toBe(reason);
      expect(component.activeAreas()).not.toContain(fence.kojiName);
    });

    it('falls back to its own words when the server gives no reason', () => {
      const { component, snackOpen } = setup({ activateGeofence: jest.fn(() => refused(500, null)) });

      component.toggleGeofenceProfile(fence);

      expect(snackOpen.mock.calls[0][0]).toBe('GEOFENCES.SNACK_FAILED_ACTIVATE');
    });

    it('shows why a rename was refused', () => {
      const reason = 'Lake Eola Loop could not be renamed right now, so nothing was changed. Try again in a moment.';
      const { component, snackOpen } = setup({ renameGeofence: jest.fn(() => refused(409, { error: reason })) });

      component.editGeofence(fence);

      expect(snackOpen.mock.calls[0][0]).toBe(reason);
    });

    it('keeps its own translated words when PoracleNG could not be reached', () => {
      const { component, snackOpen } = setup({
        renameGeofence: jest.fn(() => refused(503, { error: 'PoracleNG could not be reached. Try again shortly.' })),
      });

      component.editGeofence(fence);

      expect(snackOpen.mock.calls[0][0]).toBe('GEOFENCES.SNACK_FAILED_UPDATE');
    });

    it('opens the rename as a rename', () => {
      const { component, open } = setup();

      component.editGeofence(fence);

      expect((open.mock.calls[0][1] as { data: { rename?: boolean } }).data.rename).toBe(true);
    });
  });

  /**
   * Only Done handed back the result, so leaving the results step with Esc or a backdrop click kept the
   * old list: "1 of 10" for a user who now had two fences, and the new one missing from the map.
   */
  describe('import refresh', () => {
    const result: GeoJsonImportResult = { created: [{ ...fence, id: 10 }], errors: [] } as unknown as GeoJsonImportResult;

    it('reloads after an import closed with Esc', () => {
      const { areas, component, service } = setup(
        {},
        { afterClosed: () => of(undefined), componentInstance: { importResult: () => result } },
      );
      service.getCustomGeofences.mockClear();
      areas.getSelected.mockClear();

      component.openImportDialog();

      expect(service.getCustomGeofences).toHaveBeenCalledTimes(1);
      expect(areas.getSelected).toHaveBeenCalledTimes(1);
    });

    /** Esc while the request is still out: the fences arrive after the dialog has gone. */
    it('reloads once an import closed mid-request settles', () => {
      const settled = new Subject<GeoJsonImportResult | null>();
      const { component, service } = setup(
        {},
        { afterClosed: () => of(undefined), componentInstance: { importing: () => true, importResult: () => null, settled } },
      );
      service.getCustomGeofences.mockClear();

      component.openImportDialog();
      expect(service.getCustomGeofences).not.toHaveBeenCalled();

      settled.next(result);
      expect(service.getCustomGeofences).toHaveBeenCalledTimes(1);
    });

    it('does not reload when an import closed mid-request fails', () => {
      const settled = new Subject<GeoJsonImportResult | null>();
      const { component, service } = setup(
        {},
        { afterClosed: () => of(undefined), componentInstance: { importing: () => true, importResult: () => null, settled } },
      );
      service.getCustomGeofences.mockClear();

      component.openImportDialog();
      settled.next(null);

      expect(service.getCustomGeofences).not.toHaveBeenCalled();
    });

    it('does not reload when the dialog closed before anything was imported', () => {
      const { component, service } = setup({}, { afterClosed: () => of(undefined), componentInstance: { importResult: () => null } });
      service.getCustomGeofences.mockClear();

      component.openImportDialog();

      expect(service.getCustomGeofences).not.toHaveBeenCalled();
    });

    it('still reloads when Done hands the result back', () => {
      const { component, service } = setup({}, { afterClosed: () => of(result), componentInstance: { importResult: () => result } });
      service.getCustomGeofences.mockClear();

      component.openImportDialog();

      expect(service.getCustomGeofences).toHaveBeenCalledTimes(1);
    });
  });
});
