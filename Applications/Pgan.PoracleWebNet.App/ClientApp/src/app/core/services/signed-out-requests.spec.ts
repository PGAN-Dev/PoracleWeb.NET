import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { ConfigService } from './config.service';
import { MuteService } from './mute.service';
import { PokemonAvailabilityService } from './pokemon-availability.service';
import { SettingsService } from './settings.service';
import { SummaryScheduleService } from './summary-schedule.service';
import { ToastService } from './toast.service';
import { disabledFeatureGuard } from '../guards/disabled-feature.guard';

const API = 'http://test-api';

/**
 * Root services outlive the session. After Logout (or a 401 that ended it) the mute list still refetched
 * on every window focus, the availability and summary-capability timers kept ticking, and a guarded
 * route loaded the authenticated settings -- each a request that could only answer 401. Background
 * work now checks for a session first. Requests a signed-in page makes itself are untouched.
 */
describe('background requests after sign-out', () => {
  let httpMock: HttpTestingController;

  const signIn = () => localStorage.setItem('poracle_token', 'jwt');
  const signOut = () => localStorage.removeItem('poracle_token');

  beforeEach(() => {
    jest.useFakeTimers();
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    jest.useRealTimers();
    localStorage.clear();
  });

  describe('MuteService on window focus', () => {
    it('refetches while signed in', () => {
      signIn();
      TestBed.inject(MuteService);

      window.dispatchEvent(new Event('focus'));

      httpMock.expectOne(`${API}/api/mutes`).flush({ capable: true, mutes: [] });
    });

    it('sends nothing once signed out, and forgets the last user’s list', () => {
      signIn();
      const mutes = TestBed.inject(MuteService);
      mutes.refresh(true);
      httpMock.expectOne(`${API}/api/mutes`).flush({
        capable: true,
        mutes: [{ expiresAt: Math.floor(Date.now() / 1000) + 600, remainingSecs: 600, scope: 'gym', value: 'g1' }],
      });
      expect(mutes.hasMutes()).toBe(true);

      signOut();
      window.dispatchEvent(new Event('focus'));

      httpMock.expectNone(`${API}/api/mutes`);
      expect(mutes.hasMutes()).toBe(false);
    });
  });

  describe('PokemonAvailabilityService refresh timer', () => {
    it('keeps refreshing while signed in', () => {
      signIn();
      TestBed.inject(PokemonAvailabilityService).load();
      httpMock.expectOne(`${API}/api/pokemon-availability`).flush({ available: [], enabled: true });

      jest.advanceTimersByTime(300_000);

      httpMock.expectOne(`${API}/api/pokemon-availability`).flush({ available: [], enabled: true });
    });

    it('stops asking once signed out', () => {
      signIn();
      TestBed.inject(PokemonAvailabilityService).load();
      httpMock.expectOne(`${API}/api/pokemon-availability`).flush({ available: [], enabled: true });

      signOut();
      jest.advanceTimersByTime(300_000);

      httpMock.expectNone(`${API}/api/pokemon-availability`);
    });
  });

  describe('SummaryScheduleService capability timer', () => {
    const url = `${API}/api/summary-schedules/capability`;

    it('keeps refreshing while signed in', () => {
      signIn();
      TestBed.inject(SummaryScheduleService).loadCapability();
      httpMock.expectOne(url).flush({ enabled: true });

      jest.advanceTimersByTime(300_000);

      httpMock.expectOne(url).flush({ enabled: true });
    });

    it('stops asking once signed out', () => {
      signIn();
      TestBed.inject(SummaryScheduleService).loadCapability();
      httpMock.expectOne(url).flush({ enabled: true });

      signOut();
      jest.advanceTimersByTime(300_000);

      httpMock.expectNone(url);
    });
  });

  describe('disabledFeatureGuard', () => {
    const run = () => {
      const settings = { isDisabled: jest.fn(() => false), loadOnce: jest.fn(() => of([])) };
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          { provide: SettingsService, useValue: settings },
          { provide: Router, useValue: { createUrlTree: jest.fn() } },
          { provide: ToastService, useValue: { error: jest.fn() } },
          { provide: TranslateService, useValue: { instant: (k: string) => k } },
        ],
      });
      const result = TestBed.runInInjectionContext(() =>
        disabledFeatureGuard('disable_mons')({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
      );
      return { result, settings };
    };

    it('loads the authenticated settings while signed in', async () => {
      signIn();
      const { result, settings } = run();

      await expect(result).resolves.toBe(true);
      expect(settings.loadOnce).toHaveBeenCalled();
    });

    /** authGuard, beside it on every route, is what sends a signed-out visitor to /login. */
    it('leaves a signed-out visitor to authGuard without loading anything', async () => {
      const { result, settings } = run();

      await expect(result).resolves.toBe(true);
      expect(settings.loadOnce).not.toHaveBeenCalled();
    });
  });
});
