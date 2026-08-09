import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { DeliveryPreviewComponent } from './delivery-preview.component';
import { AreaService } from '../../../core/services/area.service';
import { LocationService } from '../../../core/services/location.service';

describe('DeliveryPreviewComponent', () => {
  let areaService: { getSelected: jest.Mock };
  let locationService: { getDistanceMapUrl: jest.Mock; getLocation: jest.Mock };

  function create(): DeliveryPreviewComponent {
    const fixture = TestBed.createComponent(DeliveryPreviewComponent);
    return fixture.componentInstance;
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    areaService = { getSelected: jest.fn().mockReturnValue(of([])) };
    locationService = {
      getDistanceMapUrl: jest.fn().mockReturnValue(of({ url: 'map.png' })),
      getLocation: jest.fn().mockReturnValue(of({ latitude: 1, longitude: 2 })),
    };

    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: AreaService, useValue: areaService },
        { provide: LocationService, useValue: locationService },
      ],
      imports: [DeliveryPreviewComponent],
    });
  });

  it('stops loading when the location request fails', () => {
    // disable_location answers 403 here. A next-only subscriber left the spinner running in every add
    // and edit alarm dialog the moment distance mode was picked. See #617.
    locationService.getLocation.mockReturnValue(throwError(() => ({ status: 403 })));

    const component = create();
    component.mode = 'distance';
    component.distanceKm = 5;
    component.ngOnChanges({});

    expect(component.loading()).toBe(false);
    expect(component.mapUrl()).toBe('');
  });

  it('shows the map when the location resolves', () => {
    const component = create();
    component.mode = 'distance';
    component.distanceKm = 5;
    component.ngOnChanges({});

    expect(component.loading()).toBe(false);
    expect(component.mapUrl()).toBe('map.png');
    expect(locationService.getDistanceMapUrl).toHaveBeenCalledWith(1, 2, 5000);
  });

  it('does not fetch a map when the user has no location set', () => {
    locationService.getLocation.mockReturnValue(of({ latitude: 0, longitude: 0 }));

    const component = create();
    component.mode = 'distance';
    component.distanceKm = 5;
    component.ngOnChanges({});

    expect(component.loading()).toBe(false);
    expect(locationService.getDistanceMapUrl).not.toHaveBeenCalled();
  });
});
