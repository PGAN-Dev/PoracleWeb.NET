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
  let settingsLoaded: ReturnType<typeof signal<boolean>>;
  let siteSettings: ReturnType<typeof signal<Record<string, string>>>;
  let impersonating: ReturnType<typeof signal<boolean>>;
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
    settingsLoaded = signal(true);
    siteSettings = signal<Record<string, string>>({});
    impersonating = signal(false);
    token = 'jwt';
    now = 1_000_000;
    jest.spyOn(Date, 'now').mockImplementation(() => now);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        { provide: AuthService, useValue: { isImpersonating: impersonating, user } },
        {
          provide: SettingsService,
          useValue: {
            isDisabled: (key: string) => siteSettings()[key]?.toLowerCase() === 'true',
            settingsLoaded,
            siteSettings,
          },
        },
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
    siteSettings.set({ disable_account_linking: 'True' });
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

  it('asks nothing until site settings have loaded, and nothing if they switch linking off', () => {
    settingsLoaded.set(false);
    siteSettings.set({});
    user.set({ id: '200000001' });
    TestBed.tick();
    http.expectNone(STATUS);

    settingsLoaded.set(true);
    siteSettings.set({ disable_account_linking: 'True' });
    TestBed.tick();
    http.expectNone(STATUS);

    siteSettings.set({});
    TestBed.tick();
    http.expectOne(STATUS).flush(linked);
  });

  it('asks nothing while impersonating', () => {
    impersonating.set(true);
    user.set({ id: '200000001' });
    TestBed.tick();
    http.expectNone(STATUS);
  });

  it("forgets the admin's own link the moment viewing as someone else starts, and reads again when it stops", async () => {
    user.set({ id: '100000000000000001' });
    TestBed.tick();
    http.expectOne(STATUS).flush({ ...linked, sides: [linked.sides![0], { side: 'telegram', exists: true, pause: 'none' }] });
    await settle();
    expect(service.canChoose()).toBe(true);

    impersonating.set(true);
    TestBed.tick();

    expect(service.status()).toBeNull();
    expect(service.canChoose()).toBe(false);
    expect(service.drift()).toEqual([]);
    http.expectNone(STATUS);

    impersonating.set(false);
    TestBed.tick();
    http.expectOne(STATUS).flush(linked);
    await settle();
    expect(service.status()).toEqual(linked);
  });

  it('does not store a read that was already in flight when viewing as someone else began', async () => {
    user.set({ id: '100000000000000001' });
    TestBed.tick();
    const req = http.expectOne(STATUS);

    impersonating.set(true);
    TestBed.tick();
    req.flush(linked);
    await settle();

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

  it('exposes the drift items, and none when the server could not compare', async () => {
    const item = { discord: { lat: 1, lon: 2 }, field: 'location' as const, fingerprint: 'abc', telegram: { lat: 3, lon: 4 } };
    let done = service.refresh();
    http.expectOne(STATUS).flush({ ...linked, drift: [item] });
    await done;
    expect(service.drift()).toEqual([item]);

    done = service.refresh();
    http.expectOne(STATUS).flush({ ...linked, drift: null });
    await done;
    expect(service.drift()).toEqual([]);
  });

  it('posts the choice with the fingerprint and reads the status again', async () => {
    const done = service.resolve('location', 'telegram', 'abc');
    const req = http.expectOne(`${API}/api/account-link/resolve`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ expected: 'abc', field: 'location', keep: 'telegram' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    http.expectOne(STATUS).flush(linked);
    await done;
  });

  it('reads the status again after a refused resolve and lets the refusal through', async () => {
    const done = service.resolve('areas', 'discord', 'abc');
    http
      .expectOne(`${API}/api/account-link/resolve`)
      .flush({ error: 'That changed again. Check the banner.' }, { status: 409, statusText: 'Conflict' });
    const failed = expect(done).rejects.toMatchObject({ error: { error: 'That changed again. Check the banner.' }, status: 409 });
    await settle();
    http.expectOne(STATUS).flush(linked);
    await failed;
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
