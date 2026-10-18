import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';

import { ConfigService } from './config.service';
import { PlayerAccountService } from './player-account.service';
import { PlayerAccount } from '../models';

describe('PlayerAccountService', () => {
  let service: PlayerAccountService;
  let httpMock: HttpTestingController;
  let snackBar: jest.Mocked<MatSnackBar>;
  const API = 'http://test-api';

  const account = (playerId: string, label: string | null = null): PlayerAccount => ({
    createdAt: '2026-10-01T00:00:00Z',
    label,
    playerId,
  });

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        { provide: TranslateService, useValue: { instant: jest.fn((key: string) => key) } },
      ],
    });
    service = TestBed.inject(PlayerAccountService);
    httpMock = TestBed.inject(HttpTestingController);
    snackBar = TestBed.inject(MatSnackBar) as jest.Mocked<MatSnackBar>;
  });

  afterEach(() => httpMock.verify());

  it('loads own accounts', () => {
    expect(service.accounts()).toBeNull();

    service.load().subscribe();
    httpMock.expectOne(`${API}/api/accounts`).flush([account('Ash_01', 'Main')]);

    expect(service.accounts()).toEqual([account('Ash_01', 'Main')]);
    expect(service.loadFailed()).toBe(false);
  });

  it("loads a user's accounts for an admin", () => {
    service.load('123456789').subscribe();
    httpMock.expectOne(`${API}/api/admin/accounts?id=123456789`).flush([account('x')]);

    expect(service.accounts()).toEqual([account('x')]);
  });

  it('marks loadFailed and keeps accounts null on error', () => {
    service.load().subscribe();
    httpMock.expectOne(`${API}/api/accounts`).flush({ error: 'boom' }, { status: 500, statusText: 'Server Error' });

    expect(service.accounts()).toBeNull();
    expect(service.loadFailed()).toBe(true);
  });

  it('clears loadFailed once a later load succeeds', () => {
    service.load().subscribe();
    httpMock.expectOne(`${API}/api/accounts`).flush({}, { status: 500, statusText: 'Server Error' });
    service.load().subscribe();
    httpMock.expectOne(`${API}/api/accounts`).flush([]);

    expect(service.loadFailed()).toBe(false);
    expect(service.accounts()).toEqual([]);
  });

  it('adds an account with its case intact and appends it to the list', () => {
    service.load().subscribe();
    httpMock.expectOne(`${API}/api/accounts`).flush([]);

    service.add('AbC_Xyz', 'Alt').subscribe();
    const req = httpMock.expectOne(`${API}/api/accounts`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ label: 'Alt', playerId: 'AbC_Xyz' });
    req.flush(account('AbC_Xyz', 'Alt'));

    expect(service.accounts()).toEqual([account('AbC_Xyz', 'Alt')]);
  });

  it('adds through the admin route when a user is given, sending no label when it is empty', () => {
    service.add('p1', '', '42').subscribe();
    const req = httpMock.expectOne(`${API}/api/admin/accounts?id=42`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ label: null, playerId: 'p1' });
    req.flush(account('p1'));
  });

  it('relabels an account and encodes the player id in the path', () => {
    service.load().subscribe();
    httpMock.expectOne(`${API}/api/accounts`).flush([account('a/b', null)]);

    service.relabel('a/b', 'Main').subscribe();
    const req = httpMock.expectOne(`${API}/api/accounts/a%2Fb`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ label: 'Main' });
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(service.accounts()).toEqual([account('a/b', 'Main')]);
  });

  it('removes an account from the list', () => {
    service.load().subscribe();
    httpMock.expectOne(`${API}/api/accounts`).flush([account('One'), account('Two')]);

    service.remove('One').subscribe();
    const req = httpMock.expectOne(`${API}/api/accounts/One`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(service.accounts()).toEqual([account('Two')]);
  });

  it('removes through the admin route with the user in the query', () => {
    service.remove('One', '42').subscribe();
    const req = httpMock.expectOne(`${API}/api/admin/accounts/One?id=42`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it("words a 409 itself rather than showing PoracleNG's lowercase text", () => {
    // PoracleNG answers "player id already linked" / "label already in use", untranslated.
    service.add('dup', '').subscribe({ error: () => undefined });
    httpMock.expectOne(`${API}/api/accounts`).flush({ error: 'player id already linked' }, { status: 409, statusText: 'Conflict' });

    expect(snackBar.open).toHaveBeenCalledWith('GAME_ACCOUNTS.ERROR_CONFLICT', 'OK', { duration: 4000 });
  });

  it("words a 422 itself rather than showing the schema's regex", () => {
    service.add('bad id', '').subscribe({ error: () => undefined });
    httpMock
      .expectOne(`${API}/api/accounts`)
      .flush({ error: 'player_id: expected string to match pattern ^[A-Za-z0-9]+$' }, { status: 422, statusText: 'Unprocessable' });

    expect(snackBar.open).toHaveBeenCalledWith('GAME_ACCOUNTS.ERROR_INVALID', 'OK', { duration: 4000 });
  });

  it('shows SELF_SERVICE_OFF on the coded 403', () => {
    service.add('p', '').subscribe({ error: () => undefined });
    httpMock
      .expectOne(`${API}/api/accounts`)
      .flush({ code: 'self_service_disabled', error: 'off' }, { status: 403, statusText: 'Forbidden' });

    expect(snackBar.open).toHaveBeenCalledWith('GAME_ACCOUNTS.SELF_SERVICE_OFF', 'OK', { duration: 4000 });
  });

  it('shows the generic failure on an uncoded 403 (impersonating)', () => {
    service.add('p', '').subscribe({ error: () => undefined });
    httpMock.expectOne(`${API}/api/accounts`).flush({ error: 'no' }, { status: 403, statusText: 'Forbidden' });

    expect(snackBar.open).toHaveBeenCalledWith('GAME_ACCOUNTS.FAILED', 'OK', { duration: 4000 });
  });

  it('shows RATE_LIMITED on 429', () => {
    service.remove('p').subscribe({ error: () => undefined });
    httpMock.expectOne(`${API}/api/accounts/p`).flush({}, { status: 429, statusText: 'Too Many Requests' });

    expect(snackBar.open).toHaveBeenCalledWith('GAME_ACCOUNTS.RATE_LIMITED', 'OK', { duration: 4000 });
  });

  it('shows FAILED on anything else', () => {
    service.relabel('p', 'x').subscribe({ error: () => undefined });
    httpMock.expectOne(`${API}/api/accounts/p`).flush({}, { status: 500, statusText: 'Server Error' });

    expect(snackBar.open).toHaveBeenCalledWith('GAME_ACCOUNTS.FAILED', 'OK', { duration: 4000 });
  });

  it('forgets the list so the next person does not see it', () => {
    service.load().subscribe();
    httpMock.expectOne(`${API}/api/accounts`).flush([account('x')]);

    service.reset();

    expect(service.accounts()).toBeNull();
    expect(service.loadFailed()).toBe(false);
  });
});
