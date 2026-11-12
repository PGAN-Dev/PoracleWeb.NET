import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { LocationDialogComponent } from './location-dialog.component';
import { GeocodingResult } from '../../../core/models';
import { LocationService } from '../../../core/services/location.service';
import { SettingsService } from '../../../core/services/settings.service';

/**
 * The audit's location-dialog findings (flows D2, D3, U3, U4, U13), against the component without a
 * rendered map: Leaflet never initialises in jsdom, so a map click is driven through `pickPoint`, the
 * method the click handler calls.
 */
describe('LocationDialogComponent fields', () => {
  let component: LocationDialogComponent;
  let geocode: jest.Mock;
  let reverseGeocode: jest.Mock;
  let setLocation: jest.Mock;

  const zoo: GeocodingResult = {
    name: 'Toledo Zoo',
    city: 'Toledo',
    displayName: 'Toledo Zoo, Lodge Avenue, Toledo, Ohio, 43609',
    latitude: 41.62,
    longitude: -83.58,
    state: 'Ohio',
    streetName: 'Lodge Avenue',
    zipcode: '43609',
  };

  function create(data: unknown = null): void {
    jest.useFakeTimers();
    geocode = jest.fn(() => of([zoo]));
    setLocation = jest.fn(() => of({}));
    reverseGeocode = jest.fn(() => of({ addr: '', displayName: '2 Elsewhere Rd, Toledo' }));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: LocationService, useValue: { geocode, reverseGeocode, setLocation } },
        { provide: SettingsService, useValue: { isDisabled: () => false, siteSettings: signal({}) } },
      ],
    });
    component = TestBed.createComponent(LocationDialogComponent).componentInstance;
    component.ngOnInit();
  }

  afterEach(() => {
    component.ngOnDestroy();
    jest.useRealTimers();
  });

  describe('the address after a search pick (D2)', () => {
    beforeEach(() => create());

    it('looks up the point a map click lands on, even straight after a search result was picked', () => {
      component.selectResult(zoo);
      component.pickPoint(41.6239, -83.5918);

      expect(reverseGeocode).toHaveBeenCalledWith(41.6239, -83.5918);
      expect(component.resolvedAddress()).toBe('2 Elsewhere Rd, Toledo');
    });

    it('still shows the picked result itself without a reverse lookup', () => {
      component.selectResult(zoo);

      expect(reverseGeocode).not.toHaveBeenCalled();
      expect(component.resolvedAddress()).toBe(zoo.displayName);
    });
  });

  describe('the coordinates (D3)', () => {
    beforeEach(() => create());

    it('refuses a cleared latitude, which a number input hands over as null', () => {
      component.latitude = null as unknown as number;

      expect(component.isValid()).toBe(false);
      expect(component.latitudeError()).toBe('DIALOG.LOCATION_LATITUDE_RANGE');
    });

    it('names the range for a latitude off the planet', () => {
      component.latitude = 999;

      expect(component.isValid()).toBe(false);
      expect(component.latitudeError()).toBe('DIALOG.LOCATION_LATITUDE_RANGE');
    });

    it('refuses a cleared longitude too', () => {
      component.longitude = null as unknown as number;

      expect(component.isValid()).toBe(false);
      expect(component.longitudeError()).toBe('DIALOG.LOCATION_LONGITUDE_RANGE');
    });

    it('accepts the edges of the range', () => {
      component.latitude = -90;
      component.longitude = 180;
      expect(component.isValid()).toBe(true);
      expect(component.latitudeError()).toBeNull();
      expect(component.longitudeError()).toBeNull();
      expect(component.originPinHint()).toBe(false);
    });

    // PoracleNG reads 0,0 as "no pin": saving it said "Location updated" and the pin was gone on reload.
    it('refuses exactly 0,0, but says nothing on a fresh dialog with no pin', () => {
      expect(component.latitude).toBe(0);
      expect(component.longitude).toBe(0);

      expect(component.isValid()).toBe(false);
      expect(component.originPinHint()).toBe(false);
    });

    it('explains the refusal once the member types 0,0', () => {
      component.latitude = 0;
      component.longitude = 0;
      component.onCoordsChanged();

      expect(component.isValid()).toBe(false);
      expect(component.originPinHint()).toBe(true);
    });

    it('explains the refusal when a pin or a result lands at 0,0', () => {
      component.pickPoint(0, 0);
      expect(component.originPinHint()).toBe(true);
    });

    it('explains the refusal when a search result sits at 0,0', () => {
      component.selectResult({ ...zoo, latitude: 0, longitude: 0 });
      expect(component.originPinHint()).toBe(true);
    });

    it('shows no hint for a stored real pin, and Save is available', () => {
      create({ latitude: 37.5, longitude: -77.4 });

      expect(component.originPinHint()).toBe(false);
      expect(component.isValid()).toBe(true);
    });

    it('still accepts a point that is only near zero on one axis or both', () => {
      for (const [lat, lon] of [
        [0, 0.000001],
        [-0.000001, 0],
        [0.0001, -0.0001],
        [51.5, 0],
        [0, -77.45],
      ]) {
        component.latitude = lat;
        component.longitude = lon;
        expect(component.isValid()).toBe(true);
        expect(component.originPinHint()).toBe(false);
      }
    });

    it('does not save 0,0 even when called directly', () => {
      component.latitude = 0;
      component.longitude = 0;
      component.save();

      expect(setLocation).not.toHaveBeenCalled();
    });

    it('does not move the map or look anything up while a coordinate is empty', () => {
      component.latitude = null as unknown as number;

      expect(() => component.onCoordsChanged()).not.toThrow();
      expect(reverseGeocode).not.toHaveBeenCalled();
    });
  });

  describe('the search answer (U3, U4)', () => {
    beforeEach(() => create());

    it('says so when a search finds nothing', () => {
      geocode.mockReturnValue(of([]));
      component.onSearchChange('zzqqxxnotaplace');
      jest.advanceTimersByTime(500);

      expect(component.noMatches()).toBe(true);
    });

    it('drops the no-match note as soon as the user types again or a search finds something', () => {
      geocode.mockReturnValue(of([]));
      component.onSearchChange('zzqqxxnotaplace');
      jest.advanceTimersByTime(500);

      component.onSearchChange('toledo zoo');
      expect(component.noMatches()).toBe(false);

      geocode.mockReturnValue(of([zoo]));
      jest.advanceTimersByTime(500);
      expect(component.noMatches()).toBe(false);
    });

    it('leads with the landmark name when the geocoder returns one', () => {
      expect(component.getAddressPrimary(zoo)).toBe('Toledo Zoo, Toledo');
      expect(component.getAddressSecondary(zoo)).toBe('Lodge Avenue, Ohio, 43609');
    });

    it('keeps the street first for a plain address, whose name is only the street again', () => {
      const house: GeocodingResult = {
        name: 'Main Street',
        city: 'Toledo',
        displayName: '12 Main Street, Toledo',
        latitude: 1,
        longitude: 2,
        streetName: 'Main Street',
        streetNumber: '12',
      };

      expect(component.getAddressPrimary(house)).toBe('12 Main Street, Toledo');
      expect(component.getAddressSecondary(house)).toBe('');
    });
  });

  describe('the title (U13)', () => {
    it('uses the title the caller passed, so a move names the place', () => {
      create({ latitude: 1, longitude: 2, pickOnly: true, title: 'Move Work' });

      expect(component.title()).toBe('Move Work');
    });

    it('falls back to the picker and pin titles', () => {
      create({ latitude: 1, longitude: 2, pickOnly: true });
      expect(component.title()).toBe('DIALOG.LOCATION_PICK_TITLE');

      component.ngOnDestroy();
      create(null);
      expect(component.title()).toBe('DIALOG.LOCATION_TITLE');
    });
  });
});
