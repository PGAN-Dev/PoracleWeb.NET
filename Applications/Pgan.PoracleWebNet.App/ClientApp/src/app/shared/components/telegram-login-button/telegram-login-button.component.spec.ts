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
    fixture.detectChanges();
    return fixture;
  }

  it('adds the Telegram widget script for the bot', () => {
    const fixture = setup('old_town_bot');
    const script = (fixture.nativeElement as HTMLElement).querySelector('.telegram-widget script');
    expect(script?.getAttribute('data-telegram-login')).toBe('old_town_bot');
    expect(script?.getAttribute('data-onauth')).toBe('onTelegramAuth(user)');
  });

  it('adds nothing without a bot username', () => {
    expect((setup('').nativeElement as HTMLElement).querySelector('script')).toBeNull();
  });

  it('emits the widget fields when Telegram calls back', () => {
    const fixture = setup('old_town_bot');
    const seen: Record<string, string>[] = [];
    fixture.componentInstance.authenticated.subscribe(v => seen.push(v));
    window.onTelegramAuth({ id: '200000001' });
    expect(seen).toEqual([{ id: '200000001' }]);
  });
});
