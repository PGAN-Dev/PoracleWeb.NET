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

  it('holds canEdit from the list response', () => {
    service.load().subscribe();
    httpMock.expectOne(`${API}/api/location/places`).flush({ named: [], canEdit: true, default: null });

    expect(service.canEdit()).toBe(true);
  });

  it('treats a response with no canEdit as not editable', () => {
    // An older PoracleWeb.NET API, or one talking to a Poracle without the route. The edit is what
    // would 404, so absent has to mean no.
    service.load().subscribe();
    httpMock.expectOne(`${API}/api/location/places`).flush({ named: [], default: null });

    expect(service.canEdit()).toBe(false);
  });

  it('puts the new point under the existing label', () => {
    service.move('work', 9.5, 8.5).subscribe();

    const request = httpMock.expectOne(`${API}/api/location/places/work`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ latitude: 9.5, longitude: 8.5 });
    request.flush({ named: [{ label: 'work', latitude: 9.5, longitude: 8.5 }], canEdit: true, default: null });

    expect(service.named()[0].latitude).toBe(9.5);
  });

  it('encodes a label with a slash in it rather than growing a path segment', () => {
    service.move('home/office', 1, 2).subscribe();

    httpMock.expectOne(`${API}/api/location/places/home%2Foffice`).flush({ named: [], canEdit: true, default: null });
  });
});
