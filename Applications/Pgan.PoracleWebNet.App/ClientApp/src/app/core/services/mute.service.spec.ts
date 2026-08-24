import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';

import { ConfigService } from './config.service';
import { Mute, MuteService } from './mute.service';

describe('MuteService', () => {
  let service: MuteService;
  let httpMock: HttpTestingController;
  let snackBar: jest.Mocked<MatSnackBar>;
  const API = 'http://test-api';

  const nowSecs = () => Math.floor(Date.now() / 1000);

  const aMute = (overrides: Partial<Mute> = {}): Mute => ({
    expiresAt: nowSecs() + 3600,
    remainingSecs: 3600,
    scope: 'gym',
    value: 'abc123',
    ...overrides,
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
    service = TestBed.inject(MuteService);
    httpMock = TestBed.inject(HttpTestingController);
    snackBar = TestBed.inject(MatSnackBar) as jest.Mocked<MatSnackBar>;
  });

  afterEach(() => httpMock.verify());

  /** Loads a list into the service and returns once the signal holds it. */
  const seed = (mutes: Mute[], capable = true) => {
    service.load().subscribe();
    httpMock.expectOne(`${API}/api/mutes`).flush({ capable, mutes });
  };

  it('reads capability and the list from one call', () => {
    seed([aMute()]);

    expect(service.capable()).toBe(true);
    expect(service.mutes()).toHaveLength(1);
    expect(service.hasMutes()).toBe(true);
  });

  it('reports nothing quiet on a server that cannot do it', () => {
    seed([], false);

    expect(service.capable()).toBe(false);
    expect(service.hasMutes()).toBe(false);
  });

  // ──────────────────────────────────────────────────────────────
  // Matching a subject to its quiet period
  // ──────────────────────────────────────────────────────────────

  it('finds the quiet period covering a subject', () => {
    seed([aMute({ scope: 'pokemon', value: '25' })]);

    expect(service.isQuiet('pokemon', '25')).toBe(true);
    expect(service.remainingFor('pokemon', '25')).toBeGreaterThan(3500);
  });

  it('does not confuse the same value under a different scope', () => {
    seed([aMute({ scope: 'pokemon', value: '25' })]);

    expect(service.isQuiet('gym', '25')).toBe(false);
  });

  /**
   * The one that would silently break the areas page. PoracleNG canonicalises an area name to the
   * geofence's own casing ("aberdeen" comes back "Aberdeen") while PoracleWeb.NET holds area names
   * lowercase everywhere, so an exact comparison lights no chip at all.
   */
  it('matches an area whose stored casing differs from the local one', () => {
    seed([aMute({ scope: 'area', value: 'Aberdeen' })]);

    expect(service.isQuiet('area', 'aberdeen')).toBe(true);
  });

  it('drops a quiet period once its expiry has passed', () => {
    seed([aMute({ expiresAt: nowSecs() - 1 })]);

    expect(service.mutes()).toEqual([]);
    expect(service.isQuiet('gym', 'abc123')).toBe(false);
  });

  // ──────────────────────────────────────────────────────────────
  // Writes
  // ──────────────────────────────────────────────────────────────

  it('posts scope, value and duration, and holds the result optimistically', () => {
    seed([]);
    service.quiet('gym', 'abc123', 240).subscribe();

    const req = httpMock.expectOne(`${API}/api/mutes`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ durationMinutes: 240, scope: 'gym', value: 'abc123' });
    req.flush({ mute: aMute(), replaced: false });

    expect(service.isQuiet('gym', 'abc123')).toBe(true);
    expect(snackBar.open).toHaveBeenCalledWith('QUIET.TOAST_QUIETED', 'OK', expect.anything());
  });

  /** Saying which happened is what stops the control feeling like it might have made a second one. */
  it('says the quiet period was extended when the server replaced one', () => {
    seed([aMute()]);
    service.quiet('gym', 'abc123', 240).subscribe();
    httpMock.expectOne(`${API}/api/mutes`).flush({ mute: aMute(), replaced: true });

    expect(snackBar.open).toHaveBeenCalledWith('QUIET.TOAST_EXTENDED', 'OK', expect.anything());
    // Extended, not duplicated.
    expect(service.mutes()).toHaveLength(1);
  });

  it('surfaces the server sentence on a 422', () => {
    seed([]);
    service.quiet('area', 'narnia', 60).subscribe({ error: () => undefined });
    httpMock.expectOne(`${API}/api/mutes`).flush({ error: 'unknown area: narnia' }, { status: 422, statusText: 'Unprocessable Entity' });

    expect(snackBar.open).toHaveBeenCalledWith('unknown area: narnia', 'OK', expect.anything());
  });

  // ──────────────────────────────────────────────────────────────
  // Resume
  // ──────────────────────────────────────────────────────────────

  /**
   * Sends the value the SERVER holds, not the caller's: the upstream delete is an exact string match,
   * so resuming with the lowercase local name 404s and the quiet period cannot be lifted until it lapses.
   */
  it('resumes an area using the casing the server returned', () => {
    seed([aMute({ scope: 'area', value: 'Aberdeen' })]);
    service.resume('area', 'aberdeen').subscribe();

    const req = httpMock.expectOne(r => r.url === `${API}/api/mutes` && r.method === 'DELETE');
    expect(req.request.params.get('value')).toBe('Aberdeen');
    expect(req.request.params.get('scope')).toBe('area');
    req.flush({ deleted: [] });

    expect(service.isQuiet('area', 'aberdeen')).toBe(false);
  });

  it('sends no value when resuming an everything mute', () => {
    seed([aMute({ scope: 'everything', value: null })]);
    service.resume('everything', null).subscribe();

    const req = httpMock.expectOne(r => r.url === `${API}/api/mutes` && r.method === 'DELETE');
    expect(req.request.params.has('value')).toBe(false);
    req.flush({ deleted: [] });
  });

  /** The store expires entries itself, so a 404 here means it already lapsed. Not an error. */
  it('treats a 404 on resume as success and says nothing', () => {
    seed([aMute()]);
    service.resume('gym', 'abc123').subscribe();
    httpMock
      .expectOne(r => r.url === `${API}/api/mutes` && r.method === 'DELETE')
      .flush({ error: 'mute not found' }, { status: 404, statusText: 'Not Found' });

    expect(service.isQuiet('gym', 'abc123')).toBe(false);
    expect(snackBar.open).not.toHaveBeenCalledWith('QUIET.TOAST_FAILED', 'OK', expect.anything());
  });

  it('clears everything on resume all', () => {
    seed([aMute(), aMute({ scope: 'area', value: 'Aberdeen' })]);
    service.resumeAll().subscribe();
    httpMock.expectOne(r => r.url === `${API}/api/mutes` && r.method === 'DELETE').flush({ deleted: [] });

    expect(service.mutes()).toEqual([]);
  });

  // ──────────────────────────────────────────────────────────────
  // Freshness and failure
  // ──────────────────────────────────────────────────────────────

  it('does not refetch a list it read a moment ago', () => {
    service.refresh();
    httpMock.expectOne(`${API}/api/mutes`).flush({ capable: true, mutes: [] });

    service.refresh();
    httpMock.expectNone(`${API}/api/mutes`);
  });

  it('refetches when forced, because a restart can empty the store between page views', () => {
    service.refresh();
    httpMock.expectOne(`${API}/api/mutes`).flush({ capable: true, mutes: [aMute()] });

    service.refresh(true);
    httpMock.expectOne(`${API}/api/mutes`).flush({ capable: true, mutes: [] });

    expect(service.mutes()).toEqual([]);
  });

  /** A failed read must not blank a list the user is looking at, nor claim the server cannot do this. */
  it('keeps the last known list when a read fails', () => {
    seed([aMute()]);

    service.load().subscribe();
    httpMock.expectOne(`${API}/api/mutes`).flush('boom', { status: 500, statusText: 'Server Error' });

    expect(service.capable()).toBe(true);
    expect(service.mutes()).toHaveLength(1);
  });

  // ──────────────────────────────────────────────────────────────
  // Labels
  // ──────────────────────────────────────────────────────────────

  it('names a duration by its own key so no locale has to pluralise', () => {
    const translate = TestBed.inject(TranslateService) as jest.Mocked<TranslateService>;
    translate.instant.mockImplementation((key: string | string[]) => (key === 'QUIET.DURATION_60' ? '1 hour' : key));

    expect(service.durationLabel(60)).toBe('1 hour');
  });

  /**
   * ngx-translate answers an unknown key with the key itself, which would otherwise reach the button as
   * "QUIET.DURATION_7". Only the six offered durations have keys, so anything else needs the fallback.
   */
  it('falls back to bare minutes for a duration with no key', () => {
    expect(service.durationLabel(7)).toBe('7 min');
  });

  it('counts down in relative units, never a clock time', () => {
    expect(service.countdownLabel(30)).toBe('QUIET.COUNTDOWN_UNDER_MINUTE');
    expect(service.countdownLabel(120)).toBe('QUIET.COUNTDOWN_MINUTES');
    expect(service.countdownLabel(7200)).toBe('QUIET.COUNTDOWN_HOURS');
    expect(service.countdownLabel(172_800)).toBe('QUIET.COUNTDOWN_DAYS');
  });
});
