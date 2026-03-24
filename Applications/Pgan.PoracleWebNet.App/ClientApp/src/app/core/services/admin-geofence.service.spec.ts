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

  it('should fetch all geofences', () => {
    const geofences = [
      { id: 1, displayName: 'Downtown', kojiName: 'pweb_111_downtown', status: 'active' },
      { id: 2, displayName: 'Park', kojiName: 'pweb_222_park', status: 'pending_review' },
      { id: 3, displayName: 'Suburbs', kojiName: 'pweb_333_suburbs', status: 'approved' },
    ];

    service.getAll().subscribe(result => {
      expect(result).toHaveLength(3);
      expect(result[0].displayName).toBe('Downtown');
      expect(result[2].status).toBe('approved');
    });

    const req = httpMock.expectOne(`${API}/api/admin/geofences/all`);
    expect(req.request.method).toBe('GET');
    req.flush(geofences);
  });

  it('should delete a geofence by id as admin', () => {
    const geofenceId = 42;
    service.adminDelete(geofenceId).subscribe();

    const req = httpMock.expectOne(`${API}/api/admin/geofences/${geofenceId}`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should include reviewedByName and reviewedByAvatarUrl in responses', () => {
    const geofences = [
      {
        id: 1,
        displayName: 'Downtown',
        groupName: 'west end',
        humanId: '111',
        kojiName: 'pweb_111_downtown',
        ownerAvatarUrl: 'https://cdn.discordapp.com/avatars/111/abc.png',
        ownerName: 'TestUser',
        reviewedBy: '999',
        reviewedByAvatarUrl: 'https://cdn.discordapp.com/avatars/999/def.png',
        reviewedByName: 'AdminUser',
        status: 'approved',
      },
      {
        id: 2,
        displayName: 'Park',
        groupName: 'east side',
        humanId: '222',
        kojiName: 'pweb_222_park',
        ownerName: 'AnotherUser',
        status: 'pending_review',
      },
    ];

    service.getAll().subscribe(result => {
      expect(result).toHaveLength(2);
      expect(result[0].reviewedByName).toBe('AdminUser');
      expect(result[0].reviewedByAvatarUrl).toBe('https://cdn.discordapp.com/avatars/999/def.png');
      expect(result[0].ownerName).toBe('TestUser');
      expect(result[0].ownerAvatarUrl).toBe('https://cdn.discordapp.com/avatars/111/abc.png');
      expect(result[1].reviewedByName).toBeUndefined();
      expect(result[1].reviewedByAvatarUrl).toBeUndefined();
    });

    const req = httpMock.expectOne(`${API}/api/admin/geofences/all`);
    expect(req.request.method).toBe('GET');
    req.flush(geofences);
  });

  it('should include resolved names in submission responses', () => {
    const submissions = [
      {
        id: 1,
        displayName: 'Downtown',
        humanId: '111',
        kojiName: 'pweb_111_downtown',
        ownerAvatarUrl: 'https://cdn.discordapp.com/avatars/111/abc.png',
        ownerName: 'SubmitterUser',
        status: 'pending_review',
      },
    ];

    service.getSubmissions().subscribe(result => {
      expect(result).toHaveLength(1);
      expect(result[0].ownerName).toBe('SubmitterUser');
      expect(result[0].ownerAvatarUrl).toBe('https://cdn.discordapp.com/avatars/111/abc.png');
    });

    const req = httpMock.expectOne(`${API}/api/admin/geofences/submissions`);
    expect(req.request.method).toBe('GET');
    req.flush(submissions);
  });

  it('should approve a submission without promotedName', () => {
    const approved = {
      id: 3,
      displayName: 'Park',
      kojiName: 'pweb_222_park',
      status: 'approved',
    };
    const body = { promotedName: undefined };

    service.approveSubmission(3, body as { promotedName?: string }).subscribe(result => {
      expect(result.status).toBe('approved');
    });

    const req = httpMock.expectOne(`${API}/api/admin/geofences/submissions/3/approve`);
    expect(req.request.method).toBe('POST');
    req.flush(approved);
  });
});
