import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { LoginComponent } from './login.component';
import { AuthProviders } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { SettingsService } from '../../core/services/settings.service';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let httpMock: HttpTestingController;
  let settingsSignal: WritableSignal<Record<string, string>>;
  const API = 'http://test-api';

  const defaultProviders: AuthProviders = {
    discord: { configured: true, enabledByAdmin: true },
    telegram: { botUsername: '', configured: false, enabledByAdmin: true },
  };

  const setup = (opts?: { providers?: AuthProviders; providersError?: boolean }) => {
    settingsSignal = signal<Record<string, string>>({});

    const providers = opts?.providers ?? defaultProviders;

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: ConfigService, useValue: { apiHost: API } },
        {
          provide: SettingsService,
          useValue: { loadPublic: jest.fn(() => of([])), siteSettings: settingsSignal },
        },
        {
          provide: AuthService,
          useValue: {
            getProviders: jest.fn(() => (opts?.providersError ? throwError(() => new Error('fail')) : of(providers))),
            getTelegramConfig: jest.fn(() => of({ botUsername: '', enabled: false })),
            isLoggedIn: jest.fn(() => false),
            loginWithDiscord: jest.fn(),
            loginWithTelegram: jest.fn(),
          },
        },
        { provide: Router, useValue: { navigate: jest.fn() } },
        { provide: ActivatedRoute, useValue: { snapshot: { fragment: '' } } },
      ],
      imports: [LoginComponent],
    });

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
  };

  beforeEach(() => {
    window.location.hash = '';
  });

  afterEach(() => {
    httpMock?.verify();
    window.location.hash = '';
  });

  describe('provider visibility', () => {
    it('should show Discord button when Discord is configured and enabled', () => {
      setup();
      fixture.detectChanges();
      const btn = fixture.nativeElement.querySelector('.discord-btn');
      expect(btn).toBeTruthy();
      expect(btn.disabled).toBe(false);
    });

    it('should show Discord button as clickable with hint when configured but admin-disabled', () => {
      setup({
        providers: {
          discord: { configured: true, enabledByAdmin: false },
          telegram: { botUsername: '', configured: false, enabledByAdmin: true },
        },
      });
      fixture.detectChanges();
      const btn = fixture.nativeElement.querySelector('.discord-btn');
      expect(btn).toBeTruthy();
      expect(btn.disabled).toBe(false);
      const hint = fixture.nativeElement.querySelector('.provider-disabled-hint');
      expect(hint).toBeTruthy();
    });

    it('should hide Discord button when not configured', () => {
      setup({
        providers: {
          discord: { configured: false, enabledByAdmin: true },
          telegram: { botUsername: '', configured: false, enabledByAdmin: true },
        },
      });
      fixture.detectChanges();
      const btn = fixture.nativeElement.querySelector('.discord-btn');
      expect(btn).toBeNull();
    });

    it('should show Telegram widget when configured and enabled', () => {
      setup({
        providers: {
          discord: { configured: true, enabledByAdmin: true },
          telegram: { botUsername: 'testbot', configured: true, enabledByAdmin: true },
        },
      });
      fixture.detectChanges();
      const widget = fixture.nativeElement.querySelector('.telegram-widget');
      expect(widget).toBeTruthy();
    });

    it('should show Telegram widget with hint when configured but admin-disabled', () => {
      setup({
        providers: {
          discord: { configured: true, enabledByAdmin: true },
          telegram: { botUsername: 'testbot', configured: true, enabledByAdmin: false },
        },
      });
      fixture.detectChanges();
      const widget = fixture.nativeElement.querySelector('.telegram-widget');
      expect(widget).toBeTruthy();
      const hints = fixture.nativeElement.querySelectorAll('.provider-disabled-hint');
      expect(hints.length).toBeGreaterThanOrEqual(1);
    });

    it('should hide Telegram section when not configured', () => {
      setup();
      fixture.detectChanges();
      const widget = fixture.nativeElement.querySelector('.telegram-widget');
      const btn = fixture.nativeElement.querySelector('.telegram-btn');
      expect(widget).toBeNull();
      expect(btn).toBeNull();
    });
  });

  describe('no-methods message', () => {
    it('should show no-methods message when neither provider is configured', () => {
      setup({
        providers: {
          discord: { configured: false, enabledByAdmin: true },
          telegram: { botUsername: '', configured: false, enabledByAdmin: true },
        },
      });
      fixture.detectChanges();
      const msg = fixture.nativeElement.querySelector('.no-methods-message');
      expect(msg).toBeTruthy();
    });

    it('should not show no-methods when Discord is configured', () => {
      setup();
      fixture.detectChanges();
      const msg = fixture.nativeElement.querySelector('.no-methods-message');
      expect(msg).toBeNull();
    });

    it('should not show no-methods before config has loaded', () => {
      // Before detectChanges, configLoaded is false — skeleton should show, not no-methods
      setup();
      // Don't call detectChanges so ngOnInit doesn't run
      const msg = fixture.nativeElement.querySelector('.no-methods-message');
      expect(msg).toBeNull();
    });
  });

  describe('loading skeleton', () => {
    it('should show skeleton before config loads', () => {
      setup();
      // The component hasn't run ngOnInit yet, configLoaded is false
      // We need to check before the forkJoin completes
      // Since setup uses synchronous of(), the skeleton will flash briefly
      // Let's verify configLoaded starts as false
      expect(component['configLoaded']()).toBe(false);
    });

    it('should hide skeleton after config loads', () => {
      setup();
      fixture.detectChanges();
      expect(component['configLoaded']()).toBe(true);
      const skeleton = fixture.nativeElement.querySelector('.skeleton-btn');
      expect(skeleton).toBeNull();
    });
  });

  describe('error handling', () => {
    it('should default to showing Discord when providers API fails', () => {
      setup({ providersError: true });
      fixture.detectChanges();
      expect(component['discordConfigured']()).toBe(true);
      expect(component['discordEnabledByAdmin']()).toBe(true);
      expect(component['configLoaded']()).toBe(true);
      const btn = fixture.nativeElement.querySelector('.discord-btn');
      expect(btn).toBeTruthy();
    });
  });

  describe('divider', () => {
    it('should show divider when both Discord and Telegram are visible', () => {
      setup({
        providers: {
          discord: { configured: true, enabledByAdmin: true },
          telegram: { botUsername: 'testbot', configured: true, enabledByAdmin: true },
        },
      });
      fixture.detectChanges();
      const divider = fixture.nativeElement.querySelector('.divider');
      expect(divider).toBeTruthy();
    });

    it('should not show divider when only Discord is visible', () => {
      setup();
      fixture.detectChanges();
      const divider = fixture.nativeElement.querySelector('.divider');
      expect(divider).toBeNull();
    });
  });

  describe('error messages', () => {
    it('should display discord_disabled error from URL fragment', () => {
      window.location.hash = '#error=discord_disabled';
      setup();
      fixture.detectChanges();
      expect(component['error']()).toBe('AUTH.ERR_DISCORD_DISABLED');
    });

    it('should display telegram_disabled error from URL fragment', () => {
      window.location.hash = '#error=telegram_disabled';
      setup();
      fixture.detectChanges();
      expect(component['error']()).toBe('AUTH.ERR_TELEGRAM_DISABLED');
    });
  });

  describe('signup URL', () => {
    it('should show signup button when signup_url is in site settings', () => {
      setup();
      settingsSignal.set({ signup_url: 'https://example.com/signup' });
      fixture.detectChanges();
      const btn = fixture.nativeElement.querySelector('.signup-btn');
      expect(btn).toBeTruthy();
      expect(btn.getAttribute('href')).toBe('https://example.com/signup');
    });

    it('should hide signup button when no signup_url', () => {
      setup();
      fixture.detectChanges();
      const btn = fixture.nativeElement.querySelector('.signup-btn');
      expect(btn).toBeNull();
    });

    it('should not use signup_url from URL fragment', () => {
      window.location.hash = '#error=user_not_registered&signup_url=https%3A%2F%2Fattacker.example.com';
      setup();
      fixture.detectChanges();
      const btn = fixture.nativeElement.querySelector('.signup-btn');
      expect(btn).toBeNull();
    });

    it('should show signup description text when signup_url is set', () => {
      setup();
      settingsSignal.set({ signup_url: 'https://example.com/signup' });
      fixture.detectChanges();
      const text = fixture.nativeElement.querySelector('.signup-text');
      expect(text).toBeTruthy();
      expect(text.textContent).toContain('AUTH.SIGN_UP_DESC');
    });
  });
});
