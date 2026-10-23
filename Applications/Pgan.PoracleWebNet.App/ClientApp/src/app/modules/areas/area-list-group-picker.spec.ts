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

/**
 * The list view's group picker filters the list. With one group, or none, there is nothing to filter by, so
 * it is not drawn; with two or more it is, and the area with no group is the bucket listed last (#1029).
 */
describe('AreaListComponent group picker', () => {
  const setup = (available: { group?: string; name: string }[]) => {
    const settings = signal<Record<string, string>>({});
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
          useValue: { named: computed(() => []), load: jest.fn(() => of({ named: [] })), pin: computed(() => null) },
        },
        { provide: SettingsService, useValue: { isDisabled: (key: string) => settings()[key] === 'true', siteSettings: settings } },
        {
          provide: AreaService,
          useValue: {
            getAvailable: () => of(available.map(a => ({ ...a, userSelectable: true }))),
            getGeofencePolygons: () => of([]),
            getMapUrl: () => of(null),
            getSelected: () => of([]),
          },
        },
      ],
      imports: [AreaListComponent, NoopAnimationsModule],
    });
    jest.spyOn(L.Map.prototype, 'getSize').mockReturnValue(L.point(1200, 400));

    const fixture = TestBed.createComponent(AreaListComponent);
    fixture.detectChanges();
    fixture.componentInstance.viewMode.set('list');
    fixture.detectChanges();
    return { component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
  };

  it('draws no picker when every area is in one group', () => {
    const { el } = setup([
      { name: 'Arts District', group: 'Downtown' },
      { name: 'Riverfront', group: 'Downtown' },
    ]);
    expect(el.querySelector('app-region-selector')).toBeNull();
    expect(el.querySelectorAll('.area-row')).toHaveLength(2);
  });

  it('draws no picker when no area has a group', () => {
    const { el } = setup([{ name: 'Arts District' }, { name: 'Riverfront' }]);
    expect(el.querySelector('app-region-selector')).toBeNull();
  });

  it('draws the picker for two or more groups, with the no-group bucket marked', () => {
    const { component, el } = setup([
      { name: 'Arts District', group: 'Downtown' },
      { name: 'Riverfront', group: 'Eastside' },
      { name: 'Airport' },
    ]);
    expect(el.querySelector('app-region-selector')).not.toBeNull();
    expect(component.groupOptions().map(o => ({ label: o.label, ungrouped: o.ungrouped }))).toEqual([
      { label: 'AREAS.GROUP_UNGROUPED', ungrouped: true },
      { label: 'Downtown', ungrouped: false },
      { label: 'Eastside', ungrouped: false },
    ]);
  });
});
