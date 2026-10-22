import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';

import { AREA_STYLE_SELECTED, AREA_STYLE_UNSELECTED, AreaMapComponent } from './area-map.component';
import { GeofenceData } from '../../../core/models';

/**
 * Areas & Places is map first: the map is the control, so a selected area has to read as selected at a
 * glance, and the pin and the named places sit on the same map as markers rather than as cards of
 * coordinates further down the page.
 */
describe('AreaMapComponent selection fill and place markers', () => {
  let fixture: ComponentFixture<AreaMapComponent>;
  let component: AreaMapComponent;
  let el: HTMLElement;

  const square = (name: string, lat: number): GeofenceData =>
    ({
      name,
      path: [
        [lat, -77.5],
        [lat, -77.4],
        [lat + 0.1, -77.4],
        [lat + 0.1, -77.5],
      ],
    }) as GeofenceData;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService()],
      imports: [AreaMapComponent],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { AREA_MAP: { PLACE_MARKER: 'Place: {{place}}', YOUR_LOCATION: 'Your pin' } });
    translate.use('en');

    jest.spyOn(L.Map.prototype, 'getSize').mockReturnValue(L.point(1200, 400));
    fixture = TestBed.createComponent(AreaMapComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => jest.restoreAllMocks());

  const paths = () => Array.from(el.querySelectorAll<SVGPathElement>('path.leaflet-interactive'));

  it('fills a selected area strongly in one colour, whatever its group', () => {
    component.geofence = [square('Downtown', 37.5), square('Uptown', 37.7), square('Summerland', 37.9)];
    component.groupMapping = new Map([
      ['Downtown', 'City'],
      ['Uptown', 'North'],
      ['Summerland', 'South'],
    ]);
    component.selectedAreas = ['downtown', 'uptown'];
    fixture.detectChanges();

    const selected = paths().filter(p => p.classList.contains('area-selected'));
    const unselected = paths().filter(p => p.classList.contains('area-unselected'));
    expect(selected).toHaveLength(2);
    expect(unselected).toHaveLength(1);
    // Two groups, one selected colour: the colour says "selected", not which group.
    expect(new Set(selected.map(p => p.getAttribute('fill')))).toEqual(new Set([AREA_STYLE_SELECTED.fillColor]));
    expect(Number(selected[0].getAttribute('fill-opacity'))).toBeGreaterThanOrEqual(0.4);
    expect(Number(unselected[0].getAttribute('fill-opacity'))).toBeLessThanOrEqual(0.1);
    expect(AREA_STYLE_UNSELECTED.fillColor).not.toBe(AREA_STYLE_SELECTED.fillColor);
  });

  it('restyles an area when it is ticked after the map has drawn', () => {
    component.geofence = [square('Downtown', 37.5)];
    component.selectedAreas = [];
    fixture.detectChanges();
    expect(paths()[0].classList.contains('area-unselected')).toBe(true);

    component.selectedAreas = ['downtown'];
    component.ngOnChanges({ selectedAreas: { currentValue: ['downtown'] } as never });

    expect(paths()[0].classList.contains('area-selected')).toBe(true);
    expect(paths()[0].getAttribute('fill')).toBe(AREA_STYLE_SELECTED.fillColor);
  });

  it('draws each named place as a labelled marker beside the pin', () => {
    fixture.componentRef.setInput('places', [
      { label: 'Work', latitude: 37.55, longitude: -77.45 },
      { label: 'Home', latitude: 37.6, longitude: -77.5 },
    ]);
    component.geofence = [square('Downtown', 37.5)];
    component.userLocation = { lat: 37.5, lng: -77.4 };
    fixture.detectChanges();

    const markers = Array.from(el.querySelectorAll<HTMLElement>('.place-marker'));
    expect(markers.map(m => m.getAttribute('title'))).toEqual(['Place: Work', 'Place: Home']);
    expect(el.querySelector('.user-location-marker')?.getAttribute('title')).toBe('Your pin');
  });

  it('redraws the markers when the places change', () => {
    fixture.componentRef.setInput('places', [{ label: 'Work', latitude: 37.55, longitude: -77.45 }]);
    fixture.detectChanges();
    expect(el.querySelectorAll('.place-marker')).toHaveLength(1);

    fixture.componentRef.setInput('places', []);
    fixture.detectChanges();
    expect(el.querySelectorAll('.place-marker')).toHaveLength(0);
  });

  it('says which place was clicked, and null for the pin', () => {
    const clicked: (null | string)[] = [];
    component.placeClicked.subscribe(label => clicked.push(label));
    fixture.componentRef.setInput('places', [{ label: 'Work', latitude: 37.55, longitude: -77.45 }]);
    component.geofence = [square('Downtown', 37.5)];
    component.userLocation = { lat: 37.5, lng: -77.4 };
    fixture.detectChanges();

    el.querySelector<HTMLElement>('.place-marker')!.click();
    el.querySelector<HTMLElement>('.user-location-marker')!.click();

    expect(clicked).toEqual(['Work', null]);
  });
});
