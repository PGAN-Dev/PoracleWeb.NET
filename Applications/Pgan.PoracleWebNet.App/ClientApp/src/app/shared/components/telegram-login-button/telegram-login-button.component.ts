import { AfterViewInit, Component, ElementRef, input, viewChild } from '@angular/core';

/**
 * Telegram's login widget for one bot, in redirect mode: on success Telegram navigates the page to
 * `authUrl` with the signed fields as query parameters (read them with `readTelegramReturn`).
 *
 * Redirect mode because the callback mode (`data-onauth`) makes the widget `eval` the attribute, which
 * the site's CSP refuses (no 'unsafe-eval'), so the button never drew.
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
export class TelegramLoginButtonComponent implements AfterViewInit {
  private readonly container = viewChild.required<ElementRef<HTMLDivElement>>('container');

  /** Absolute URL Telegram sends the browser back to. */
  readonly authUrl = input.required<string>();
  readonly botUsername = input.required<string>();

  ngAfterViewInit(): void {
    const bot = this.botUsername();
    if (!bot) return;

    const script = document.createElement('script');
    script.src = 'https://telegram.org/js/telegram-widget.js?22';
    script.setAttribute('data-telegram-login', bot);
    script.setAttribute('data-size', 'large');
    script.setAttribute('data-auth-url', this.authUrl());
    script.setAttribute('data-request-access', 'write');
    // Keeps Cloudflare Rocket Loader from deferring or rewriting the widget script.
    script.setAttribute('data-cfasync', 'false');
    script.async = true;
    this.container().nativeElement.appendChild(script);
  }
}
