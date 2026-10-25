import { AfterViewInit, Component, DestroyRef, ElementRef, inject, input, viewChild } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

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
  private readonly destroyRef = inject(DestroyRef);
  private readonly translate = inject(TranslateService);

  /** Absolute URL Telegram sends the browser back to. */
  readonly authUrl = input.required<string>();
  readonly botUsername = input.required<string>();
  /** The accessible name of the iframe Telegram draws. Empty falls back to "Sign in with Telegram". */
  readonly frameTitle = input('');

  ngAfterViewInit(): void {
    const bot = this.botUsername();
    if (!bot) return;

    const container = this.container().nativeElement;
    this.titleFrames(container);

    const script = document.createElement('script');
    script.src = 'https://telegram.org/js/telegram-widget.js?22';
    script.setAttribute('data-telegram-login', bot);
    script.setAttribute('data-size', 'large');
    script.setAttribute('data-auth-url', this.authUrl());
    script.setAttribute('data-request-access', 'write');
    // Keeps Cloudflare Rocket Loader from deferring or rewriting the widget script.
    script.setAttribute('data-cfasync', 'false');
    script.async = true;
    container.appendChild(script);
  }

  /**
   * Telegram's script draws an iframe with no title, so a screen reader announced only "frame" (axe
   * frame-title, serious). The frame arrives whenever the script loads, so it is titled when it appears.
   */
  private titleFrames(container: HTMLElement): void {
    if (typeof MutationObserver === 'undefined') return;
    const observer = new MutationObserver(() => {
      const title = this.frameTitle() || this.translate.instant('AUTH.SIGN_IN_TELEGRAM');
      container.querySelectorAll('iframe:not([title])').forEach(frame => frame.setAttribute('title', title));
    });
    observer.observe(container, { childList: true, subtree: true });
    this.destroyRef.onDestroy(() => observer.disconnect());
  }
}
