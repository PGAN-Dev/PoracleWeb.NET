import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';
import { of } from 'rxjs';

import { AreaListComponent } from './area-list.component';
import { AreaService } from '../../core/services/area.service';
import { LocationService } from '../../core/services/location.service';
import { MuteService } from '../../core/services/mute.service';
import { PlacesService } from '../../core/services/places.service';
import { SettingsService } from '../../core/services/settings.service';
import { AreaMapComponent } from '../../shared/components/area-map/area-map.component';

/**
 * Areas & Places is map first. The map used to start below two explainer cards, an info strip and the
 * places grid, under the fold at 1440x900. Now the map opens the page, the selected chips sit under it, and
 * the pin and places are markers on that map, managed in a panel below.
 */
describe('AreaListComponent map-first layout', () => {
  const work = { label: 'Work', latitude: 37.55, longitude: -77.45 };

  const setup = (disabled: string[] = []) => {
    const settings = signal<Record<string, string>>(Object.fromEntries(disabled.map(k => [k, 'true'])));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        { provide: MuteService, useValue: { countdownLabel: () => '', refresh: jest.fn(), remainingFor: () => null } },
        {
          provide: LocationService,
          useValue: {
            getLocation: () => of({ latitude: 37.5, longitude: -77.4 }),
            getStaticMapUrl: () => of(null),
            reverseGeocode: () => of({ displayName: '1 Main St' }),
          },
        },
        {
          provide: PlacesService,
          useValue: { named: computed(() => [work]), load: jest.fn(() => of({ named: [work] })), pin: computed(() => null) },
        },
        { provide: SettingsService, useValue: { isDisabled: (key: string) => settings()[key] === 'true', siteSettings: settings } },
        {
          provide: AreaService,
          useValue: {
            getAvailable: () => of([{ name: 'Downtown', group: 'City', userSelectable: true }]),
            getGeofencePolygons: () =>
              of([
                {
                  name: 'Downtown',
                  path: [
                    [37.5, -77.5],
                    [37.5, -77.4],
                    [37.6, -77.4],
                  ],
                },
              ]),
            getMapUrl: () => of(null),
            getSelected: () => of(['downtown']),
          },
        },
      ],
      imports: [AreaListComponent, NoopAnimationsModule],
    });
    jest.spyOn(L.Map.prototype, 'getSize').mockReturnValue(L.point(1200, 400));

    const fixture = TestBed.createComponent(AreaListComponent);
    fixture.detectChanges();
    return { component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement, fixture };
  };

  afterEach(() => jest.restoreAllMocks());

  /** True when `a` comes before `b` in document order. */
  const before = (a: Element | null, b: Element | null) =>
    !!a && !!b && !!(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);

  it('puts the map first, the selected areas under it, then the pin and places', () => {
    const { el } = setup();

    const map = el.querySelector('app-area-map');
    const chips = el.querySelector('.selected-chips');
    const panel = el.querySelector('.pin-places');
    expect(before(map, chips)).toBe(true);
    expect(before(chips, panel)).toBe(true);
    expect(panel?.querySelector('app-places-section')).not.toBeNull();
  });

  it('draws the named places on the map', () => {
    const { fixture } = setup();

    const map = fixture.debugElement.query(d => d.componentInstance instanceof AreaMapComponent).componentInstance as AreaMapComponent;
    expect(map.places()).toEqual([work]);
    expect(map.userLocation).toEqual({ lat: 37.5, lng: -77.4 });
  });

  it('keeps the panel closed until a marker is clicked, then opens it', () => {
    const { component, fixture } = setup();
    expect(component.pinPanelOpen()).toBe(false);

    const map = fixture.debugElement.query(d => d.componentInstance instanceof AreaMapComponent).componentInstance as AreaMapComponent;
    map.placeClicked.emit('Work');

    expect(component.pinPanelOpen()).toBe(true);
  });

  it('draws no places and no panel with location switched off', () => {
    const { component, el } = setup(['disable_location']);

    expect(component.mapPlaces()).toEqual([]);
    expect(el.querySelector('.pin-places')).toBeNull();
    expect(el.querySelector('app-area-map')).not.toBeNull();
  });
});
