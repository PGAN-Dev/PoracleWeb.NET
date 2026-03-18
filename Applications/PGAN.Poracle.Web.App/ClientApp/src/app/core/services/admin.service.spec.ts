import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AdminService } from './admin.service';
import { ConfigService } from './config.service';
import { AdminUser, Human } from '../models';

describe('AdminService', () => {
  let service: AdminService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockHuman: Human = {
    adminDisable: 0, area: '["downtown"]', communityMembership: null,
    enabled: 1, id: '123', language: 'en', latitude: 40.7,
    longitude: -74.0, name: 'TestUser',
  };

  const mockAdminUser: AdminUser = {
    adminDisable: 0, avatarUrl: null, currentProfileNo: 1,
    disabledDate: null, enabled: 1, id: '123', language: 'en',
    lastChecked: null, name: 'TestUser', type: 'discord:user',
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
      ],
    });
    service = TestBed.inject(AdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch all users', () => {
    service.getUsers().subscribe(users => {
      expect(users).toHaveLength(1);
      expect(users[0].name).toBe('TestUser');
    });

    httpMock.expectOne(`${API}/api/admin/users`).flush([mockAdminUser]);
  });

  it('should fetch a single user by ID', () => {
    service.getUser('123').subscribe(user => {
      expect(user.id).toBe('123');
    });

    httpMock.expectOne(`${API}/api/admin/users/123`).flush(mockHuman);
  });

  it('should delete a user', () => {
    service.deleteUser('123').subscribe();

    const req = httpMock.expectOne(`${API}/api/admin/users/123`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should delete user alarms and return count', () => {
    service.deleteUserAlarms('123').subscribe(result => {
      expect(result.deleted).toBe(15);
    });

    const req = httpMock.expectOne(`${API}/api/admin/users/123/alarms`);
    expect(req.request.method).toBe('DELETE');
    req.flush({ deleted: 15 });
  });

  it('should disable a user', () => {
    service.disableUser('123').subscribe(user => {
      expect(user.adminDisable).toBe(1);
    });

    const req = httpMock.expectOne(`${API}/api/admin/users/123/disable`);
    expect(req.request.method).toBe('PUT');
    req.flush({ ...mockHuman, adminDisable: 1 });
  });

  it('should enable a user', () => {
    service.enableUser('123').subscribe(user => {
      expect(user.adminDisable).toBe(0);
    });

    httpMock.expectOne(`${API}/api/admin/users/123/enable`).flush(mockHuman);
  });

  it('should pause a user', () => {
    service.pauseUser('123').subscribe(user => {
      expect(user.enabled).toBe(0);
    });

    httpMock.expectOne(`${API}/api/admin/users/123/pause`).flush({
      ...mockHuman, enabled: 0,
    });
  });

  it('should resume a user', () => {
    service.resumeUser('123').subscribe(user => {
      expect(user.enabled).toBe(1);
    });

    httpMock.expectOne(`${API}/api/admin/users/123/resume`).flush(mockHuman);
  });

  it('should impersonate a user', () => {
    service.impersonateUser('123').subscribe(result => {
      expect(result.token).toBe('impersonated-jwt');
    });

    const req = httpMock.expectOne(`${API}/api/admin/users/123/impersonate`);
    expect(req.request.method).toBe('POST');
    req.flush({ token: 'impersonated-jwt' });
  });

  it('should impersonate by ID', () => {
    service.impersonateById('456').subscribe(result => {
      expect(result.token).toBe('jwt-456');
    });

    const req = httpMock.expectOne(`${API}/api/admin/impersonate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ userId: '456' });
    req.flush({ token: 'jwt-456' });
  });

  it('should fetch avatars', () => {
    service.fetchAvatars(['111', '222']).subscribe(result => {
      expect(result['111']).toBe('https://cdn.discordapp.com/avatars/111/abc.png');
    });

    const req = httpMock.expectOne(`${API}/api/admin/users/avatars`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(['111', '222']);
    req.flush({ '111': 'https://cdn.discordapp.com/avatars/111/abc.png' });
  });

  it('should fetch poracle admins', () => {
    service.getPoracleAdmins().subscribe(admins => {
      expect(admins).toEqual(['admin1', 'admin2']);
    });

    httpMock.expectOne(`${API}/api/admin/poracle-admins`).flush(['admin1', 'admin2']);
  });

  it('should create a webhook', () => {
    service.createWebhook('my-hook', 'https://example.com').subscribe();

    const req = httpMock.expectOne(`${API}/api/admin/webhooks`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'my-hook', url: 'https://example.com' });
    req.flush(null);
  });

  it('should add a webhook delegate', () => {
    service.addWebhookDelegate('hook1', 'user1').subscribe(result => {
      expect(result).toEqual(['user1']);
    });

    const req = httpMock.expectOne(`${API}/api/admin/webhook-delegates`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ userId: 'user1', webhookId: 'hook1' });
    req.flush(['user1']);
  });

  it('should remove a webhook delegate', () => {
    service.removeWebhookDelegate('hook1', 'user1').subscribe(result => {
      expect(result).toEqual([]);
    });

    const req = httpMock.expectOne(`${API}/api/admin/webhook-delegates`);
    expect(req.request.method).toBe('DELETE');
    req.flush([]);
  });

  it('should encode special characters in user IDs', () => {
    service.getUser('user@special').subscribe();

    httpMock.expectOne(`${API}/api/admin/users/user%40special`).flush(mockHuman);
  });
});
