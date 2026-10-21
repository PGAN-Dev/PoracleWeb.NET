import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { PlacesService } from './places.service';

const API = 'http://test';

describe('PlacesService', () => {
  let httpMock: HttpTestingController;
  let service: PlacesService;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [PlacesService, { provide: ConfigService, useValue: { apiHost: API } }, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(PlacesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('serves the list as sent, with no capability flag', () => {
    service.load().subscribe();
    httpMock.expectOne(`${API}/api/location/places`).flush({ named: [{ label: 'work', latitude: 1, longitude: 2 }], default: null });

    expect(service.named()).toHaveLength(1);
    expect((service as unknown as Record<string, unknown>)['canEdit']).toBeUndefined();
  });

  it('puts the new point under the existing label', () => {
    service.move('work', 9.5, 8.5).subscribe();

    const request = httpMock.expectOne(`${API}/api/location/places/work`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ latitude: 9.5, longitude: 8.5 });
    request.flush({ named: [{ label: 'work', latitude: 9.5, longitude: 8.5 }], default: null });

    expect(service.named()[0].latitude).toBe(9.5);
  });

  it('encodes a label with a slash in it rather than growing a path segment', () => {
    service.move('home/office', 1, 2).subscribe();

    httpMock.expectOne(`${API}/api/location/places/home%2Foffice`).flush({ named: [], default: null });
  });
});
