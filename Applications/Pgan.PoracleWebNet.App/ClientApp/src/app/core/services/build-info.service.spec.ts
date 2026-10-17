import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { BuildInfoService } from './build-info.service';
import { ConfigService } from './config.service';

describe('BuildInfoService', () => {
  let service: BuildInfoService;
  let httpMock: HttpTestingController;
  let apiHost: string;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(BuildInfoService);
    httpMock = TestBed.inject(HttpTestingController);
    apiHost = TestBed.inject(ConfigService).apiHost;
  });

  afterEach(() => httpMock.verify());

  it('is null until it has loaded', () => {
    expect(service.channel()).toBeNull();
  });

  it('sets channel from /api/version', () => {
    service.load();
    const req = httpMock.expectOne(`${apiHost}/api/version`);
    expect(req.request.method).toBe('GET');
    req.flush({ buildDate: 'x', channel: 'experimental', environment: 'Production', revision: 'y', revisionShort: 'y', version: '1.0.0' });
    expect(service.channel()).toBe('experimental');
  });

  it('reports an official build as official', () => {
    service.load();
    httpMock.expectOne(`${apiHost}/api/version`).flush({ channel: 'official' });
    expect(service.channel()).toBe('official');
  });

  it('leaves channel null when the request fails', () => {
    service.load();
    httpMock.expectOne(`${apiHost}/api/version`).flush('boom', { status: 500, statusText: 'Server Error' });
    expect(service.channel()).toBeNull();
  });

  it('leaves channel null when an older server omits the field', () => {
    service.load();
    httpMock.expectOne(`${apiHost}/api/version`).flush({ version: '2.20.0' });
    expect(service.channel()).toBeNull();
  });

  it('asks only once however often load is called', () => {
    service.load();
    service.load();
    httpMock.expectOne(`${apiHost}/api/version`).flush({ channel: 'official' });
  });
});
