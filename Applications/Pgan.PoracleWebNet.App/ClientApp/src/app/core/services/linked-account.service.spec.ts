import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { AuthService } from './auth.service';
import { ConfigService } from './config.service';
import { LinkedAccountService } from './linked-account.service';
import { SettingsService } from './settings.service';
import { TokenStoreService } from './token-store.service';
import { LinkStatus } from '../models';

const API = 'http://test-api';
const STATUS = `${API}/api/account-link/status`;

describe('LinkedAccountService', () => {
  let http: HttpTestingController;
  let service: LinkedAccountService;
  let user: ReturnType<typeof signal<unknown>>;
  let disabled: boolean;
  let token: string | null;
  let now: number;

  const linked: LinkStatus = {
    sides: [
      { side: 'discord', exists: true, pause: 'none' },
      { side: 'telegram', exists: false, pause: 'not_registered' },
    ],
    defaultDestination: 'discord',
    linked: true,
  };

  const settle = async () => {
    for (let i = 0; i < 5; i++) await Promise.resolve();
  };

  beforeEach(() => {
    user = signal<unknown>(null);
    disabled = false;
    token = 'jwt';
    now = 1_000_000;
    jest.spyOn(Date, 'now').mockImplementation(() => now);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        { provide: AuthService, useValue: { isImpersonating: signal(false), user } },
        { provide: SettingsService, useValue: { isDisabled: () => disabled } },
        { provide: TokenStoreService, useValue: { getAccessToken: () => token } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(LinkedAccountService);
  });

  afterEach(() => {
    http.verify();
    jest.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('reads the status into the signal', async () => {
    const done = service.refresh();
    http.expectOne(STATUS).flush(linked);
    await done;
    expect(service.status()).toEqual(linked);
  });

  it('makes one request for two focus events inside a minute', () => {
    window.dispatchEvent(new Event('focus'));
    http.expectOne(STATUS).flush(linked);
    now += 30_000;
    window.dispatchEvent(new Event('focus'));
    http.expectNone(STATUS);
  });

  it('refreshes again on a focus after a minute has passed', () => {
    window.dispatchEvent(new Event('focus'));
    http.expectOne(STATUS).flush(linked);
    now += 60_001;
    window.dispatchEvent(new Event('focus'));
    http.expectOne(STATUS).flush(linked);
  });

  it('asks for nothing on focus when signed out or when linking is switched off', () => {
    token = null;
    window.dispatchEvent(new Event('focus'));
    token = 'jwt';
    disabled = true;
    now += 120_000;
    window.dispatchEvent(new Event('focus'));
    http.expectNone(STATUS);
  });

  it('refreshes when a user arrives, and forgets the status when they leave', async () => {
    user.set({ id: '200000001' });
    TestBed.tick();
    http.expectOne(STATUS).flush(linked);
    await settle();
    expect(service.status()).toEqual(linked);
    user.set(null);
    TestBed.tick();
    expect(service.status()).toBeNull();
  });

  it('starts a Discord link with cookies and navigates to the returned URL', async () => {
    const go = jest.spyOn(service as unknown as { navigate: (url: string) => void }, 'navigate').mockImplementation(() => undefined);
    const done = service.startDiscordLink();
    const req = http.expectOne(`${API}/api/account-link/discord/start`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBe(true);
    req.flush({ url: 'https://discord.example/authorize?x=1' });
    await done;
    expect(go).toHaveBeenCalledWith('https://discord.example/authorize?x=1');
  });

  it('posts the Telegram widget fields and then refreshes', async () => {
    const done = service.linkTelegram({ id: '200000001', auth_date: '1', hash: 'h' });
    const req = http.expectOne(`${API}/api/account-link/telegram`);
    expect(req.request.body).toEqual({ id: '200000001', auth_date: '1', hash: 'h' });
    req.flush({});
    await settle();
    http.expectOne(STATUS).flush(linked);
    await done;
    expect(service.status()?.linked).toBe(true);
  });

  it('lets a refused Telegram link reach the caller with its code', async () => {
    const done = service.linkTelegram({ id: '1' });
    http
      .expectOne(`${API}/api/account-link/telegram`)
      .flush({ code: 'link_taken', error: 'taken' }, { status: 409, statusText: 'Conflict' });
    await expect(done).rejects.toMatchObject({ error: { code: 'link_taken' }, status: 409 });
  });

  it('unlinks and refreshes', async () => {
    const done = service.unlink();
    const req = http.expectOne(`${API}/api/account-link`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    http.expectOne(STATUS).flush({ linked: false });
    await done;
    expect(service.status()).toEqual({ linked: false });
  });

  it('sets the default destination and refreshes', async () => {
    const done = service.setDefault('telegram');
    const req = http.expectOne(`${API}/api/account-link/default`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ destination: 'telegram' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    http.expectOne(STATUS).flush(linked);
    await done;
  });
});
