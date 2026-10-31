import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';

import { AreaMapComponent } from './area-map.component';
import { INITIAL_VIEW_MAX_ZOOM, LOCATION_ONLY_ZOOM } from './initial-view';
import { GeofenceData } from '../../../core/models';

/**
 * Covers the wiring between the map and planInitialView. The ladder itself is unit-tested in
 * initial-view.spec.ts; what matters here is that the right bounds reach fitBounds, and that a
 * selection change after the map has settled does not move it. See #693.
 */
describe('AreaMapComponent initial view', () => {
  let component: AreaMapComponent;
  let fixture: ComponentFixture<AreaMapComponent>;
  let fitBounds: jest.SpyInstance;
  let setView: jest.SpyInstance;

  // Richmond-ish and Sydney-ish, so "fitted the selection" and "fitted everything" are far apart.
  const richmond = (name: string): GeofenceData =>
    ({
      name,
      path: [
        [37.5, -77.5],
        [37.5, -77.4],
        [37.6, -77.4],
        [37.6, -77.5],
      ],
    }) as GeofenceData;

  const sydney = (name: string): GeofenceData =>
    ({
      name,
      path: [
        [-33.8, 151.2],
        [-33.8, 151.3],
        [-33.7, 151.3],
        [-33.7, 151.2],
      ],
    }) as GeofenceData;

  const feed = [richmond('downtown'), richmond('fan'), sydney('summerland'), sydney('patonga')];

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService()],
      imports: [AreaMapComponent],
    });

    // Leaflet needs a non-zero container to compute a zoom from bounds; jsdom reports 0x0.
    jest.spyOn(L.Map.prototype, 'getSize').mockReturnValue(L.point(1200, 400));
    fitBounds = jest.spyOn(L.Map.prototype, 'fitBounds');
    setView = jest.spyOn(L.Map.prototype, 'setView');

    fixture = TestBed.createComponent(AreaMapComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => jest.restoreAllMocks());

  /** The bounds passed to the most recent fitBounds call. */
  const lastFitted = (): L.LatLngBounds => fitBounds.mock.calls[fitBounds.mock.calls.length - 1][0] as L.LatLngBounds;

  it('opens on the selected areas rather than the whole feed', () => {
    component.geofence = feed;
    component.selectedAreas = ['downtown', 'fan'];
    fixture.detectChanges();

    const bounds = lastFitted();
    expect(bounds.getSouth()).toBeCloseTo(37.5, 1);
    expect(bounds.getNorth()).toBeCloseTo(37.6, 1);
    // Sydney must not be in shot -- that is the bug.
    expect(bounds.contains(L.latLng(-33.8, 151.2))).toBe(false);
  });

  it('caps the zoom so a single small area does not open at street level', () => {
    component.geofence = feed;
    component.selectedAreas = ['fan'];
    fixture.detectChanges();

    const options = fitBounds.mock.calls[fitBounds.mock.calls.length - 1][1] as L.FitBoundsOptions;
    expect(options.maxZoom).toBe(INITIAL_VIEW_MAX_ZOOM);
  });

  it('falls back to the whole feed when nothing is selected and there is no location', () => {
    component.geofence = feed;
    component.selectedAreas = [];
    fixture.detectChanges();

    expect(lastFitted().contains(L.latLng(-33.8, 151.2))).toBe(true);
  });

  it('opens on the pinned location when nothing is selected', () => {
    component.geofence = feed;
    component.selectedAreas = [];
    component.userLocation = { lat: 37.55, lng: -77.45 };
    fixture.detectChanges();

    expect(setView).toHaveBeenCalledWith([37.55, -77.45], LOCATION_ONLY_ZOOM);
  });

  it('ignores a 0,0 location, which is how "not set" is stored', () => {
    component.geofence = feed;
    component.selectedAreas = [];
    component.userLocation = { lat: 0, lng: 0 };
    fixture.detectChanges();

    expect(setView).not.toHaveBeenCalledWith([0, 0], LOCATION_ONLY_ZOOM);
    expect(lastFitted().contains(L.latLng(-33.8, 151.2))).toBe(true);
  });

  it('re-fits when the selection arrives after the feed', () => {
    // The Areas page loads these from independent requests; the map is routinely on screen with an
    // empty selection first. Without the upgrade the user is left looking at the whole world.
    component.geofence = feed;
    component.selectedAreas = [];
    fixture.detectChanges();
    expect(lastFitted().contains(L.latLng(-33.8, 151.2))).toBe(true);

    component.selectedAreas = ['downtown', 'fan'];
    component.ngOnChanges({ selectedAreas: { currentValue: ['downtown', 'fan'] } as never });

    expect(lastFitted().contains(L.latLng(-33.8, 151.2))).toBe(false);
  });

  it('does not move the map when an area is toggled', () => {
    component.geofence = feed;
    component.selectedAreas = ['downtown'];
    fixture.detectChanges();

    const callsAfterInitialFit = fitBounds.mock.calls.length;

    component.selectedAreas = ['downtown', 'fan'];
    component.ngOnChanges({ selectedAreas: { currentValue: ['downtown', 'fan'] } as never });

    expect(fitBounds.mock.calls.length).toBe(callsAfterInitialFit);
  });

  it('does not move the map after the user has panned it', () => {
    component.geofence = feed;
    component.selectedAreas = [];
    fixture.detectChanges();

    component.mapElement.nativeElement.dispatchEvent(new Event('pointerdown'));
    const callsBefore = fitBounds.mock.calls.length;

    component.selectedAreas = ['downtown'];
    component.ngOnChanges({ selectedAreas: { currentValue: ['downtown'] } as never });

    expect(fitBounds.mock.calls.length).toBe(callsBefore);
  });

  // ── Multi-part areas (#1117) ─────────────────────────────────────────────
  // PoracleNG serves a multi-part area as `multipath` with no `path`; the map used to skip it entirely.

  /** One part in Sydney and one in London, so a fit that missed a part is easy to see. */
  const split = (name: string): GeofenceData => ({
    id: 9,
    name,
    multipath: [
      [
        [-33.8, 151.2],
        [-33.8, 151.3],
        [-33.7, 151.3],
        [-33.7, 151.2],
      ],
      [
        [51.5, -0.2],
        [51.5, -0.1],
        [51.6, -0.1],
        [51.6, -0.2],
      ],
    ],
  });

  it('opens on every part of a selected multi-part area', () => {
    component.geofence = [richmond('downtown'), split('islands')];
    component.selectedAreas = ['islands'];
    fixture.detectChanges();

    const bounds = lastFitted();
    expect(bounds.contains(L.latLng(-33.75, 151.25))).toBe(true);
    expect(bounds.contains(L.latLng(51.55, -0.15))).toBe(true);
  });

  it('draws a multi-part area as one polygon whose parts are not holes', () => {
    component.geofence = [split('islands')];
    component.selectedAreas = [];
    fixture.detectChanges();

    const polygon = component['polygonByName'].get('islands');
    expect(polygon).toBeDefined();
    // A multi-polygon reads back as polygons of rings; [ring, ring] would be an outline and its hole.
    const parts = polygon!.getLatLngs() as L.LatLng[][][];
    expect(parts).toHaveLength(2);
    expect(parts[0]).toHaveLength(1);
    expect(parts[1]).toHaveLength(1);
    expect(parts[1][0][0].lat).toBeCloseTo(51.5);
  });

  it('still draws an ordinary area as one ring', () => {
    component.geofence = [richmond('downtown')];
    component.selectedAreas = [];
    fixture.detectChanges();

    const rings = component['polygonByName'].get('downtown')!.getLatLngs() as L.LatLng[][];
    expect(rings).toHaveLength(1);
    expect(rings[0]).toHaveLength(4);
  });
});
