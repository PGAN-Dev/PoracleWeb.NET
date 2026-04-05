import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';

import { LoginComponent } from './login.component';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { SettingsService } from '../../core/services/settings.service';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let httpMock: HttpTestingController;
  let settingsSignal: WritableSignal<Record<string, string>>;
  const API = 'http://test-api';

  const setup = (opts?: { telegramEnabled?: boolean }) => {
    settingsSignal = signal<Record<string, string>>({});

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
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
            getTelegramConfig: jest.fn(() =>
              of({ botUsername: opts?.telegramEnabled ? 'testbot' : '', enabled: opts?.telegramEnabled ?? false }),
            ),
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

  describe('discordEnabled', () => {
    it('should default to true when enable_discord setting is absent', () => {
      setup();
      fixture.detectChanges();
      expect(component['discordEnabled']()).toBe(true);
    });

    it('should be true when enable_discord is "True"', () => {
      setup();
      settingsSignal.set({ enable_discord: 'True' });
      fixture.detectChanges();
      expect(component['discordEnabled']()).toBe(true);
    });

    it('should be false when enable_discord is "False"', () => {
      setup();
      settingsSignal.set({ enable_discord: 'False' });
      fixture.detectChanges();
      expect(component['discordEnabled']()).toBe(false);
    });

    it('should be false when enable_discord is "false" (lowercase)', () => {
      setup();
      settingsSignal.set({ enable_discord: 'false' });
      fixture.detectChanges();
      expect(component['discordEnabled']()).toBe(false);
    });

    it('should be true when enable_discord is empty string', () => {
      setup();
      settingsSignal.set({ enable_discord: '' });
      fixture.detectChanges();
      expect(component['discordEnabled']()).toBe(true);
    });
  });

  describe('template rendering', () => {
    it('should show Discord button when enabled', () => {
      setup();
      settingsSignal.set({ enable_discord: 'True' });
      fixture.detectChanges();
      const btn = fixture.nativeElement.querySelector('.discord-btn');
      expect(btn).toBeTruthy();
      expect(btn.textContent).toContain('Sign in with Discord');
    });

    it('should hide Discord button when disabled', () => {
      setup();
      settingsSignal.set({ enable_discord: 'False' });
      fixture.detectChanges();
      const btn = fixture.nativeElement.querySelector('.discord-btn');
      expect(btn).toBeNull();
    });

    it('should show no-methods message when both methods are disabled', () => {
      setup();
      settingsSignal.set({ enable_discord: 'False' });
      fixture.detectChanges();
      const msg = fixture.nativeElement.querySelector('.no-methods-message');
      expect(msg).toBeTruthy();
      expect(msg.textContent).toContain('No login methods are currently enabled');
    });

    it('should not show no-methods message when Discord is enabled', () => {
      setup();
      fixture.detectChanges();
      const msg = fixture.nativeElement.querySelector('.no-methods-message');
      expect(msg).toBeNull();
    });

    it('should show divider when both Discord and Telegram are enabled', () => {
      setup({ telegramEnabled: true });
      settingsSignal.set({ enable_discord: 'True' });
      fixture.detectChanges();
      const divider = fixture.nativeElement.querySelector('.divider');
      expect(divider).toBeTruthy();
    });

    it('should not show divider when only Discord is enabled', () => {
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
      expect(component['error']()).toBe('Discord login is currently disabled.');
    });
  });
});
