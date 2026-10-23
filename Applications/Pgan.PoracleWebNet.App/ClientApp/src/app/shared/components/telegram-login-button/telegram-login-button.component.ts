import { AfterViewInit, Component, ElementRef, NgZone, OnDestroy, inject, input, output, viewChild } from '@angular/core';

declare global {
  interface Window {
    onTelegramAuth: (user: Record<string, string>) => void;
  }
}

/**
 * Telegram's login widget for one bot. It emits the fields Telegram signs; what to do with them is the
 * caller's: the login page verifies them as a sign-in, the linked accounts page as a link.
 *
 * Telegram's script calls a global by name, so only one of these can be live at a time.
 */
@Component({
  selector: 'app-telegram-login-button',
  standalone: true,
  styles: [
    `
      .telegram-widget {
        display: flex;
        justify-content: center;
        min-height: 40px;
      }
    `,
  ],
  template: '<div #container class="telegram-widget"></div>',
})
export class TelegramLoginButtonComponent implements AfterViewInit, OnDestroy {
  private readonly callback = (user: Record<string, string>): void => this.ngZone.run(() => this.authenticated.emit(user));
  private readonly container = viewChild.required<ElementRef<HTMLDivElement>>('container');
  private readonly ngZone = inject(NgZone);

  readonly authenticated = output<Record<string, string>>();
  readonly botUsername = input.required<string>();

  ngAfterViewInit(): void {
    const bot = this.botUsername();
    if (!bot) return;

    window.onTelegramAuth = this.callback;

    const script = document.createElement('script');
    script.src = 'https://telegram.org/js/telegram-widget.js?22';
    script.setAttribute('data-telegram-login', bot);
    script.setAttribute('data-size', 'large');
    script.setAttribute('data-onauth', 'onTelegramAuth(user)');
    script.setAttribute('data-request-access', 'write');
    script.async = true;
    this.container().nativeElement.appendChild(script);
  }

  ngOnDestroy(): void {
    if (window.onTelegramAuth === this.callback) {
      delete (window as Partial<Window>).onTelegramAuth;
    }
  }
}
