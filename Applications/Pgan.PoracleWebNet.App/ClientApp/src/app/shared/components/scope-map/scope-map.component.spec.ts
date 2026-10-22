import { computed, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';
import { of } from 'rxjs';

import { ScopeMapComponent } from './scope-map.component';
import { AlarmScope, GeofenceData, SavedPlace } from '../../../core/models';
import { AreaService } from '../../../core/services/area.service';
import { BasemapService } from '../../../core/services/basemap.service';
import { PlacesService } from '../../../core/services/places.service';

describe('ScopeMapComponent', () => {
  const ABERDEEN: GeofenceData = {
    id: 1,
    name: 'Aberdeen',
    path: [
      [57.1, -2.2],
      [57.2, -2.2],
      [57.2, -2.0],
    ],
  };

  let fixture: ComponentFixture<ScopeMapComponent>;
  let areaService: { getGeofencePolygonsShared: jest.Mock };
  let basemap: { attach: jest.Mock };

  function create(scope: AlarmScope, pin: null | SavedPlace, profileAreas: string[] = []): void {
    const places = signal<{ default: null | SavedPlace; named: SavedPlace[] }>({ named: [], default: pin });
    areaService = { getGeofencePolygonsShared: jest.fn().mockReturnValue(of([ABERDEEN])) };
    basemap = { attach: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: AreaService, useValue: areaService },
        { provide: BasemapService, useValue: basemap },
        {
          provide: PlacesService,
          useValue: { named: computed(() => places().named), pin: computed(() => places().default) },
        },
      ],
      imports: [ScopeMapComponent],
    });
    fixture = TestBed.createComponent(ScopeMapComponent);
    fixture.componentRef.setInput('scope', scope);
    fixture.componentRef.setInput('profileAreas', profileAreas);
    fixture.detectChanges();
  }

  const map = () => (fixture.nativeElement as HTMLElement).querySelector('.scope-map');

  it('draws a radius from the pin on a map built by the basemap service', () => {
    create({ distanceKm: 2, mode: 'profile' }, { label: '', latitude: 57.15, longitude: -2.1 });

    expect(map()).not.toBeNull();
    expect(map()?.getAttribute('role')).toBe('img');
    expect(basemap.attach).toHaveBeenCalledTimes(1);
  });

  it('never reads the area feed for a radius', () => {
    create({ distanceKm: 2, mode: 'profile' }, { label: '', latitude: 57.15, longitude: -2.1 });

    expect(areaService.getGeofencePolygonsShared).not.toHaveBeenCalled();
  });

  it('draws nothing at all when there is no pin to measure from (as under disable_location)', () => {
    create({ distanceKm: 2, mode: 'profile' }, null);

    expect(map()).toBeNull();
    expect(basemap.attach).not.toHaveBeenCalled();
  });

  it("shades the profile's areas for the inherited scope, matching names case-insensitively", () => {
    create({ mode: 'profile' }, null, ['aberdeen']);

    expect(areaService.getGeofencePolygonsShared).toHaveBeenCalledTimes(1);
    expect(map()).not.toBeNull();
  });

  it('draws nothing for areas the feed has no polygon for', () => {
    create({ areas: ['nowhere'], mode: 'areas' }, null);

    expect(map()).toBeNull();
  });

  /**
   * Inside a dialog tab the container is 0x0 for a moment, and Leaflet won't re-measure a map without a
   * view. The map waits for a size, then fits, and gives up after two seconds rather than polling forever.
   */
  describe('fitting once the tab has a size', () => {
    const sized = (el: Element, width: number, height: number) => {
      Object.defineProperty(el, 'clientWidth', { configurable: true, value: width });
      Object.defineProperty(el, 'clientHeight', { configurable: true, value: height });
    };

    afterEach(() => {
      jest.useRealTimers();
      jest.restoreAllMocks();
    });

    it('fits the circle as soon as the container has a size', () => {
      jest.useFakeTimers();
      const fitBounds = jest.spyOn(L.Map.prototype, 'fitBounds');
      create({ distanceKm: 2, mode: 'profile' }, { label: '', latitude: 57.15, longitude: -2.1 });
      expect(fitBounds).not.toHaveBeenCalled();

      sized(map()!, 500, 180);
      jest.advanceTimersByTime(100);

      expect(fitBounds).toHaveBeenCalledTimes(1);
    });

    it('stops waiting after twenty tries when the container never gets one', () => {
      jest.useFakeTimers();
      create({ distanceKm: 2, mode: 'profile' }, { label: '', latitude: 57.15, longitude: -2.1 });

      jest.advanceTimersByTime(5000);

      expect(jest.getTimerCount()).toBe(0);
    });

    it('runs one chain of retries however often it redraws', () => {
      jest.useFakeTimers();
      create({ distanceKm: 2, mode: 'profile' }, { label: '', latitude: 57.15, longitude: -2.1 });

      fixture.componentRef.setInput('scope', { distanceKm: 3, mode: 'profile' });
      fixture.detectChanges();
      fixture.componentRef.setInput('scope', { distanceKm: 4, mode: 'profile' });
      fixture.detectChanges();

      expect(jest.getTimerCount()).toBe(1);
    });
  });
});
