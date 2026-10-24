import { TestBed } from '@angular/core/testing';

import { TelegramLoginButtonComponent } from './telegram-login-button.component';

describe('TelegramLoginButtonComponent', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
    document.querySelectorAll('script[data-telegram-login]').forEach(s => s.remove());
  });

  function setup(botUsername: string) {
    const fixture = TestBed.createComponent(TelegramLoginButtonComponent);
    fixture.componentRef.setInput('botUsername', botUsername);
    fixture.componentRef.setInput('authUrl', 'https://poracle.example/linked-accounts');
    fixture.detectChanges();
    return fixture;
  }

  const scriptOf = (f: ReturnType<typeof setup>) => (f.nativeElement as HTMLElement).querySelector('.telegram-widget script');

  it('adds the Telegram widget script for the bot, in redirect mode', () => {
    const script = scriptOf(setup('old_town_bot'));
    expect(script?.getAttribute('data-telegram-login')).toBe('old_town_bot');
    expect(script?.getAttribute('data-auth-url')).toBe('https://poracle.example/linked-accounts');
    expect(script?.getAttribute('data-size')).toBe('large');
    expect(script?.getAttribute('data-request-access')).toBe('write');
  });

  it('never sets data-onauth, which the widget would eval under a CSP that refuses it', () => {
    expect(scriptOf(setup('old_town_bot'))?.hasAttribute('data-onauth')).toBe(false);
    expect('onTelegramAuth' in window).toBe(false);
  });

  it('tells Cloudflare Rocket Loader to leave the script alone', () => {
    expect(scriptOf(setup('old_town_bot'))?.getAttribute('data-cfasync')).toBe('false');
  });

  it('adds nothing without a bot username', () => {
    expect(scriptOf(setup(''))).toBeNull();
  });
});
