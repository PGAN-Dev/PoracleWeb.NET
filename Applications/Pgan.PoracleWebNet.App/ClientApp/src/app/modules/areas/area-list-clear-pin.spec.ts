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
 * Clearing the pin (flows U2) set 0,0 at once, with no confirmation and no undo, and every alarm with a
 * radius around the pin stopped matching. It now asks first, and says how many alarms that is.
 */
describe('AreaListComponent clearing the pin', () => {
  const setup = (usage: null | number, confirmed: boolean) => {
    const settings = signal<Record<string, string>>({});
    const location = {
      getLocation: jest.fn(() => of({ latitude: 37.5, longitude: -77.4 })),
      getPinUsage: jest.fn(() => of(usage)),
      getStaticMapUrl: jest.fn(() => of(null)),
      reverseGeocode: jest.fn(() => of({ displayName: '1 Main St' })),
      setLocation: jest.fn(() => of(void 0)),
    };
    const dialog = { open: jest.fn(() => ({ afterClosed: () => of(confirmed) })) };
    const snackBar = { open: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: LocationService, useValue: location },
        {
          provide: PlacesService,
          useValue: { named: computed(() => []), load: jest.fn(() => of({ named: [] })), pin: computed(() => null) },
        },
        { provide: SettingsService, useValue: { isDisabled: () => false, siteSettings: settings } },
        {
          provide: AreaService,
          useValue: {
            getAvailable: () => of([]),
            getGeofencePolygons: () => of([]),
            getMapUrl: () => of(null),
            getSelected: () => of([]),
          },
        },
      ],
      imports: [AreaListComponent, NoopAnimationsModule],
    });
    TestBed.overrideComponent(AreaListComponent, {
      add: {
        providers: [
          { provide: MatDialog, useValue: dialog },
          { provide: MatSnackBar, useValue: snackBar },
        ],
      },
    });

    const fixture = TestBed.createComponent(AreaListComponent);
    fixture.detectChanges();
    return { component: fixture.componentInstance, dialog, location };
  };

  it('asks before clearing, naming how many alarms measure from the pin', () => {
    const { component, dialog, location } = setup(3, false);

    component.clearLocation();

    expect(location.getPinUsage).toHaveBeenCalled();
    const data = (dialog.open.mock.calls[0] as unknown[])[1] as { data: { message: string; warn: boolean } };
    expect(data.data.message).toBe('AREAS.CLEAR_PIN_CONFIRM_OTHER');
    expect(data.data.warn).toBe(true);
    expect(location.setLocation).not.toHaveBeenCalled();
  });

  it('clears once confirmed', () => {
    const { component, location } = setup(1, true);

    component.clearLocation();

    expect(location.setLocation).toHaveBeenCalledWith({ latitude: 0, longitude: 0 });
    expect(component.location()).toBeNull();
  });

  it('still asks, without a number, when the count cannot be read', () => {
    const { component, dialog } = setup(null, false);

    component.clearLocation();

    const data = (dialog.open.mock.calls[0] as unknown[])[1] as { data: { message: string } };
    expect(data.data.message).toBe('AREAS.CLEAR_PIN_CONFIRM_UNKNOWN');
  });

  it('says nothing measures from the pin when nothing does', () => {
    const { component, dialog } = setup(0, false);

    component.clearLocation();

    const data = (dialog.open.mock.calls[0] as unknown[])[1] as { data: { message: string } };
    expect(data.data.message).toBe('AREAS.CLEAR_PIN_CONFIRM_NONE');
  });
});
