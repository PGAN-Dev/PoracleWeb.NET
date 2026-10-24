import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { filter } from 'rxjs';

import { LinkedAccountService } from '../../../core/services/linked-account.service';

const DISMISSED_KEY = 'poracle-drift-dismissed';
export const LINKED_ACCOUNTS_URL = '/linked-accounts';

function readDismissed(): Set<string> {
  try {
    const parsed: unknown = JSON.parse(sessionStorage.getItem(DISMISSED_KEY) ?? '[]');
    return new Set(Array.isArray(parsed) ? parsed.filter((v): v is string => typeof v === 'string') : []);
  } catch {
    return new Set();
  }
}

function onLinkedAccounts(url: string): boolean {
  const path = url.split(/[?#]/)[0];
  return path === LINKED_ACCOUNTS_URL || path.startsWith(`${LINKED_ACCOUNTS_URL}/`);
}

/**
 * One line under the toolbar while a linked pair's apps disagree, pointing at Linked accounts, where every
 * difference is listed with its choices. Dismissing hides what has been seen for the rest of the session; a
 * difference that appears later is a new fingerprint and shows again. The list itself never hides.
 *
 * It only appears at a moment the page is changing anyway: the first status read after sign-in (the page is still
 * loading) or a navigation. Drift found by a focus refresh or after a write waits for the next navigation, so
 * nothing the person is reading moves down under them. It goes away at once, which only follows their own action.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslatePipe],
  selector: 'app-pair-drift-banner',
  styleUrl: './pair-drift-banner.component.scss',
  templateUrl: './pair-drift-banner.component.html',
})
export class PairDriftBannerComponent {
  /** Something undismissed differs, as of the last moment the banner was allowed to appear. */
  private readonly armed = signal(false);
  private readonly dismissed = signal<Set<string>>(readDismissed());
  private readonly linked = inject(LinkedAccountService);
  private readonly pending = computed(() => this.linked.drift().some(i => !this.dismissed().has(i.fingerprint)));
  private readonly router = inject(Router);
  private readonly url = signal(this.router.url);

  protected readonly linkedAccountsUrl = LINKED_ACCOUNTS_URL;

  /** Hidden on Linked accounts, which lists the differences itself. */
  protected readonly visible = computed(() => this.armed() && this.pending() && !onLinkedAccounts(this.url()));

  constructor() {
    let firstRead = true;
    effect(() => {
      const status = this.linked.status();
      untracked(() => {
        if (status === null) {
          firstRead = true;
          this.armed.set(false);
        } else if (firstRead) {
          firstRead = false;
          this.armed.set(this.pending());
        }
      });
    });

    this.router.events
      .pipe(
        filter((e): e is NavigationEnd => e instanceof NavigationEnd),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(e => {
        this.url.set(e.urlAfterRedirects);
        this.armed.set(this.pending());
      });
  }

  protected dismiss(): void {
    const next = new Set(this.dismissed());
    for (const item of this.linked.drift()) next.add(item.fingerprint);
    this.dismissed.set(next);
    try {
      sessionStorage.setItem(DISMISSED_KEY, JSON.stringify([...next]));
    } catch {
      // The banner stays hidden for this page load without it.
    }
    // The button that had focus is gone; the page is the sensible next place.
    document.getElementById('main-content')?.focus();
  }
}
