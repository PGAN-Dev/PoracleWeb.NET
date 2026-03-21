import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AdminGeofenceService } from './admin-geofence.service';
import { ConfigService } from './config.service';

describe('AdminGeofenceService', () => {
  let service: AdminGeofenceService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(AdminGeofenceService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch pending submissions', () => {
    const submissions = [
      { id: 1, displayName: 'Downtown', kojiName: 'pweb_111_downtown', status: 'pending_review' },
      { id: 2, displayName: 'Park', kojiName: 'pweb_222_park', status: 'pending_review' },
    ];

    service.getSubmissions().subscribe(result => {
      expect(result).toHaveLength(2);
      expect(result[0].displayName).toBe('Downtown');
      expect(result[1].status).toBe('pending_review');
    });

    const req = httpMock.expectOne(`${API}/api/admin/geofences/submissions`);
    expect(req.request.method).toBe('GET');
    req.flush(submissions);
  });

  it('should approve a submission', () => {
    const approved = {
      id: 1,
      displayName: 'Downtown',
      kojiName: 'pweb_111_downtown',
      promotedName: 'Downtown Official',
      status: 'approved',
    };
    const body = { promotedName: 'Downtown Official' };

    service.approveSubmission(1, body).subscribe(result => {
      expect(result.status).toBe('approved');
      expect(result.promotedName).toBe('Downtown Official');
    });

    const req = httpMock.expectOne(`${API}/api/admin/geofences/submissions/1/approve`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(approved);
  });

  it('should reject a submission', () => {
    const rejected = { id: 1, displayName: 'Downtown', kojiName: 'pweb_111_downtown', reviewNotes: 'Area too large', status: 'rejected' };
    const body = { reviewNotes: 'Area too large' };

    service.rejectSubmission(1, body).subscribe(result => {
      expect(result.status).toBe('rejected');
      expect(result.reviewNotes).toBe('Area too large');
    });

    const req = httpMock.expectOne(`${API}/api/admin/geofences/submissions/1/reject`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(rejected);
  });
});
