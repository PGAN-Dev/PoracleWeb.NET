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
    id: '123',
    name: 'TestUser',
    adminDisable: 0,
    area: '["downtown"]',
    communityMembership: null,
    enabled: 1,
    language: 'en',
    latitude: 40.7,
    longitude: -74.0,
  };

  const mockAdminUser: AdminUser = {
    id: '123',
    name: 'TestUser',
    adminDisable: 0,
    avatarUrl: null,
    currentProfileNo: 1,
    disabledDate: null,
    enabled: 1,
    language: 'en',
    lastChecked: null,
    type: 'discord:user',
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
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

    const req = httpMock.expectOne(r => r.url === `${API}/api/admin/users/by-id` && r.params.get('id') === '123');
    req.flush(mockHuman);
  });

  it('should delete a user', () => {
    service.deleteUser('123').subscribe();

    const req = httpMock.expectOne(r => r.url === `${API}/api/admin/users` && r.params.get('id') === '123');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should delete user alarms and return count', () => {
    service.deleteUserAlarms('123').subscribe(result => {
      expect(result.deleted).toBe(15);
    });

    const req = httpMock.expectOne(r => r.url === `${API}/api/admin/users/alarms` && r.params.get('id') === '123');
    expect(req.request.method).toBe('DELETE');
    req.flush({ deleted: 15 });
  });

  it('should disable a user', () => {
    service.disableUser('123').subscribe(user => {
      expect(user.adminDisable).toBe(1);
    });

    const req = httpMock.expectOne(r => r.url === `${API}/api/admin/users/disable` && r.params.get('id') === '123');
    expect(req.request.method).toBe('PUT');
    req.flush({ ...mockHuman, adminDisable: 1 });
  });

  it('should enable a user', () => {
    service.enableUser('123').subscribe(user => {
      expect(user.adminDisable).toBe(0);
    });

    const req = httpMock.expectOne(r => r.url === `${API}/api/admin/users/enable` && r.params.get('id') === '123');
    req.flush(mockHuman);
  });

  it('should pause a user', () => {
    service.pauseUser('123').subscribe(user => {
      expect(user.enabled).toBe(0);
    });

    const req = httpMock.expectOne(r => r.url === `${API}/api/admin/users/pause` && r.params.get('id') === '123');
    req.flush({
      ...mockHuman,
      enabled: 0,
    });
  });

  it('should resume a user', () => {
    service.resumeUser('123').subscribe(user => {
      expect(user.enabled).toBe(1);
    });

    const req = httpMock.expectOne(r => r.url === `${API}/api/admin/users/resume` && r.params.get('id') === '123');
    req.flush(mockHuman);
  });

  it('should impersonate a user', () => {
    service.impersonateUser('123').subscribe(result => {
      expect(result.token).toBe('impersonated-jwt');
    });

    const req = httpMock.expectOne(r => r.url === `${API}/api/admin/users/impersonate` && r.params.get('id') === '123');
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

  it('should pass webhook URL IDs as query params', () => {
    const webhookUrl = 'http://discord.com/api/webhooks/123/token';
    service.getUser(webhookUrl).subscribe();

    const req = httpMock.expectOne(r => r.url === `${API}/api/admin/users/by-id` && r.params.get('id') === webhookUrl);
    req.flush(mockHuman);
  });

  it('should fetch poracle servers', () => {
    const mockServers = [
      { name: 'Server1', checkedAt: '2026-03-21T00:00:00Z', host: '10.0.0.1', online: true },
      { name: 'Server2', checkedAt: '2026-03-21T00:00:00Z', host: '10.0.0.2', online: false },
    ];

    service.getPoracleServers().subscribe(servers => {
      expect(servers).toHaveLength(2);
      expect(servers[0].online).toBe(true);
      expect(servers[1].online).toBe(false);
    });

    httpMock.expectOne(`${API}/api/admin/poracle/servers`).flush(mockServers);
  });

  it('should restart a single server', () => {
    const mockStatus = { name: 'Server1', checkedAt: '2026-03-21T00:00:00Z', host: '10.0.0.1', message: 'restarted', online: true };

    service.restartServer('10.0.0.1').subscribe(status => {
      expect(status.online).toBe(true);
      expect(status.message).toBe('restarted');
    });

    const req = httpMock.expectOne(`${API}/api/admin/poracle/servers/10.0.0.1/restart`);
    expect(req.request.method).toBe('POST');
    req.flush(mockStatus);
  });

  it('should restart all servers', () => {
    const mockStatuses = [
      { name: 'Server1', checkedAt: '2026-03-21T00:00:00Z', host: '10.0.0.1', online: true },
      { name: 'Server2', checkedAt: '2026-03-21T00:00:00Z', host: '10.0.0.2', online: true },
    ];

    service.restartAllServers().subscribe(statuses => {
      expect(statuses).toHaveLength(2);
    });

    const req = httpMock.expectOne(`${API}/api/admin/poracle/servers/restart-all`);
    expect(req.request.method).toBe('POST');
    req.flush(mockStatuses);
  });

  it('should encode special characters in server host for restart', () => {
    service.restartServer('host:with@chars').subscribe();

    httpMock.expectOne(`${API}/api/admin/poracle/servers/host%3Awith%40chars/restart`).flush({});
  });
});
