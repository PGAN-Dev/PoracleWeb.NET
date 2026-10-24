import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatRadioChange, MatRadioModule } from '@angular/material/radio';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { catchError, of } from 'rxjs';

import { LinkSide, LinkSideStatus } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { LinkedAccountService } from '../../core/services/linked-account.service';
import { SettingsService } from '../../core/services/settings.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { PairDriftPanelComponent } from '../../shared/components/pair-drift-panel/pair-drift-panel.component';
import { TelegramLoginButtonComponent } from '../../shared/components/telegram-login-button/telegram-login-button.component';
import { DRIFT_REVIEW_STATE } from '../../shared/utils/pair-drift';
import { clearTelegramReturn, readTelegramReturn } from '../../shared/utils/telegram-return';

/** What a side's card says about it. `this-account` and `not-linked` only occur while nothing is linked. */
export type SideState = 'active' | 'not-linked' | 'paused-admin' | 'stopped' | 'this-account' | 'waiting';

export interface SideCard {
  /** The name on that account, where the site knows it. */
  name: null | string;
  side: LinkSide;
  state: SideState;
}

/** One choice under "New alarms go to". */
export interface DefaultOption {
  disabled: boolean;
  /** The side exists but isn't receiving alerts (stopped, or paused by an admin). */
  paused: boolean;
  side: LinkSide;
}

/** The refusal codes the API and the Discord callback use. Anything else reads as the generic failure. */
const REFUSAL_KEYS: Record<string, string> = {
  link_bad_id: 'LINKED.ERR_LINK_BAD_ID',
  link_cancelled: 'LINKED.ERR_LINK_CANCELLED',
  link_expired: 'LINKED.ERR_LINK_EXPIRED',
  link_failed: 'LINKED.ERR_LINK_FAILED',
  link_not_permitted: 'LINKED.ERR_LINK_NOT_PERMITTED',
  link_provision_failed: 'LINKED.ERR_LINK_PROVISION_FAILED',
  link_taken: 'LINKED.ERR_LINK_TAKEN',
  link_type_mismatch: 'LINKED.ERR_LINK_TYPE_MISMATCH',
  link_wrong_type: 'LINKED.ERR_LINK_WRONG_TYPE',
};

const SIDES: LinkSide[] = ['discord', 'telegram'];

const SIDE_LABEL: Record<LinkSide, string> = { discord: 'LINKED.SIDE_DISCORD', telegram: 'LINKED.SIDE_TELEGRAM' };

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatIconModule,
    MatRadioModule,
    MatSnackBarModule,
    PairDriftPanelComponent,
    TelegramLoginButtonComponent,
    TranslatePipe,
  ],
  selector: 'app-linked-accounts',
  standalone: true,
  styleUrl: './linked-accounts.component.scss',
  templateUrl: './linked-accounts.component.html',
})
export class LinkedAccountsComponent implements OnInit {
  /** Opened from a Review link (the banner or a pair warning): focus starts at the heading, not left on the page. */
  private readonly arrivedToReview = inject(Router).currentNavigation()?.extras.state?.[DRIFT_REVIEW_STATE] === true;
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly i18n = inject(I18nService);
  /** The side the person signed in with. Null for a type that is neither, which cannot link. */
  private readonly ownSide = computed<LinkSide | null>(() => {
    const type = this.auth.user()?.type ?? '';
    if (type.startsWith('discord')) return 'discord';
    if (type.startsWith('telegram')) return 'telegram';
    return null;
  });

  private readonly settings = inject(SettingsService);

  private readonly snackBar = inject(MatSnackBar);
  protected readonly busy = signal(false);
  protected readonly linked = inject(LinkedAccountService);
  protected readonly cards = computed<SideCard[]>(() => {
    const status = this.linked.status();
    const sides = status?.linked ? (status.sides ?? []) : null;
    return SIDES.map(side => {
      const state = this.stateOf(side, sides);
      return { name: this.nameOf(side, state, sides), side, state };
    });
  });

  protected readonly defaultDestination = computed(() => this.linked.status()?.defaultDestination ?? null);

  /**
   * The two choices for new alarms. A side that isn't receiving alerts can't be newly chosen, since nothing sent
   * there would arrive. It stays enabled while it is already the default, so the current choice still reads as
   * selected and the warning under it says what to do. They are not held while a choice saves: disabling the radio
   * that has focus dropped focus to the page, and the next arrow key did nothing.
   */
  protected readonly defaultOptions = computed<DefaultOption[]>(() => {
    const sides = this.linked.status()?.sides ?? [];
    const current = this.defaultDestination();
    return SIDES.map(side => {
      const paused = (sides.find(s => s.side === side)?.pause ?? 'none') !== 'none';
      return { side, disabled: paused && side !== current, paused };
    });
  });

  /** Set when the default points at a side that isn't receiving alerts. */
  protected readonly defaultPausedWarning = computed<{ app: string; other: string } | null>(() => {
    const current = this.defaultDestination();
    if (!current || !this.defaultOptions().find(o => o.side === current)?.paused) return null;
    const other: LinkSide = current === 'discord' ? 'telegram' : 'discord';
    return { app: SIDE_LABEL[current], other: SIDE_LABEL[other] };
  });

  protected readonly discordAvailable = signal(false);
  protected readonly isLinked = computed(() => this.linked.status()?.linked === true);

  protected readonly legacyProfileZero = computed<{ app: string; other: string } | null>(() => {
    const side = this.linked.status()?.legacyProfileZero;
    if (side !== 'discord' && side !== 'telegram') {
      return null;
    }
    return side === 'discord'
      ? { app: 'LINKED.SIDE_DISCORD', other: 'LINKED.SIDE_TELEGRAM' }
      : { app: 'LINKED.SIDE_TELEGRAM', other: 'LINKED.SIDE_DISCORD' };
  });

  /** True once the first status read has finished, so a read that failed is told apart from one still going. */
  protected readonly loaded = signal(false);

  /** Linking, unlinking and the default are refused while viewing as someone else, so they are not offered. */
  protected readonly readOnly = this.auth.isImpersonating;

  /** The translation key of the refusal to show, from a failed link or the Discord callback's fragment. */
  protected readonly refusal = signal<null | string>(null);

  /** Offering where new alarms go only makes sense once there are two places to send them. */
  protected readonly showDefault = computed(() => {
    const sides = this.linked.status()?.sides ?? [];
    return this.isLinked() && !this.readOnly() && sides.length === 2 && sides.every(s => s.exists);
  });

  /** Linked, but one side isn't registered yet: say when the choice arrives rather than leaving a gap. */
  protected readonly showDefaultWaiting = computed(() => this.isLinked() && !this.readOnly() && !this.showDefault());

  protected readonly sideLabel = SIDE_LABEL;

  protected readonly supportUrl = this.settings.supportUrl;
  /** Where Telegram sends the browser back to after a sign-in. */
  protected readonly telegramAuthUrl = `${window.location.origin}/linked-accounts`;

  protected readonly telegramBot = signal('');

  protected readonly telegramAvailable = computed(() => this.telegramBot() !== '');

  protected readonly telegramRegistrationUrl = this.settings.telegramRegistrationUrl;

  protected canLink(side: LinkSide): boolean {
    if (this.readOnly() || this.isLinked() || side === this.ownSide()) return false;
    return side === 'discord' ? this.discordAvailable() : this.telegramAvailable();
  }

  protected async chooseDefault(change: MatRadioChange): Promise<void> {
    const side = change.value as LinkSide;
    await this.run(async () => {
      await this.linked.setDefault(side);
      this.snackBar.open(
        this.i18n.instant('LINKED.SNACK_DEFAULT', { app: this.i18n.instant(SIDE_LABEL[side]) }),
        this.i18n.instant('TOAST.OK'),
        { duration: 3000 },
      );
    });
    this.keepRadioFocus(side);
  }

  /** Closes the refusal. Its Dismiss button goes with it, so focus moves to the page heading. */
  protected dismissRefusal(): void {
    this.refusal.set(null);
    this.focusHeading();
  }

  ngOnInit(): void {
    const discordReturned = this.readFragment();
    const telegramReturn = this.takeTelegramReturn();
    if (this.arrivedToReview) this.focusHeading();
    void this.linked.refresh().then(async () => {
      this.loaded.set(true);
      if (discordReturned) this.confirmDiscordLink();
      // Not while viewing as someone else, nor once linked: the actions the page offers are the ones it allows.
      if (telegramReturn && !this.readOnly() && !this.isLinked()) await this.onTelegramReturn(telegramReturn);
    });

    this.auth
      .getProviders()
      .pipe(
        catchError(() => of(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(providers => {
        this.discordAvailable.set(!!providers && providers.discord.configured && providers.discord.enabledByAdmin);
        if (providers && providers.telegram.configured && providers.telegram.enabledByAdmin) {
          this.telegramBot.set(providers.telegram.botUsername ?? '');
        }
      });
  }

  protected async onTelegramReturn(data: Record<string, string>): Promise<void> {
    await this.run(async () => {
      await this.linked.linkTelegram(data);
      this.snackBar.open(this.i18n.instant('LINKED.SNACK_LINKED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
    });
  }

  protected async retry(): Promise<void> {
    await this.linked.refresh();
  }

  protected startDiscord(): Promise<void> {
    return this.run(() => this.linked.startDiscordLink());
  }

  protected unlink(): void {
    // Unlinking deletes nothing, so the confirm is neutral: no warning triangle and no trash can.
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('LINKED.UNLINK'),
        icon: 'link_off',
        message: this.i18n.instant('LINKED.UNLINK_CONFIRM'),
        title: this.i18n.instant('LINKED.UNLINK_TITLE'),
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (!confirmed) return;
      void this.run(async () => {
        await this.linked.unlink();
        this.snackBar.open(this.i18n.instant('LINKED.SNACK_UNLINKED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        // The Unlink button that had focus is gone now.
        this.focusHeading();
      });
    });
  }

  /**
   * `#linked=discord` only says the callback finished; the status read after it decides what the person is told.
   * Linked, the toast. Read but not linked, the generic refusal, because "Accounts linked" above "Not linked" is
   * wrong. A read that failed says nothing here: the page shows its own load failure.
   */
  private confirmDiscordLink(): void {
    const status = this.linked.status();
    if (status?.linked) {
      this.snackBar.open(this.i18n.instant('LINKED.SNACK_LINKED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
    } else if (status) {
      this.refusal.set(REFUSAL_KEYS['link_failed']);
    }
  }

  /**
   * Where focus goes when the control that had it disappears (a dismissed message, the Unlink button). After
   * the change renders, so the old control is gone. The heading takes focus without joining the tab order.
   */
  private focusHeading(): void {
    setTimeout(() => {
      const heading = this.host.nativeElement.querySelector<HTMLElement>('h1');
      if (!heading) return;
      heading.setAttribute('tabindex', '-1');
      heading.focus();
    });
  }

  /** After a save, focus stays on the chosen radio, so arrow keys keep working in the group. Only if it went nowhere. */
  private keepRadioFocus(side: LinkSide): void {
    setTimeout(() => {
      const active = document.activeElement;
      if (active && active !== document.body) return;
      this.host.nativeElement.querySelector<HTMLInputElement>(`[data-test="default-${side}"] input[type="radio"]`)?.focus();
    });
  }

  private nameOf(side: LinkSide, state: SideState, sides: LinkSideStatus[] | null): null | string {
    if (state === 'this-account') return this.auth.user()?.username || null;
    if (state === 'not-linked' || state === 'waiting') return null;
    return sides?.find(s => s.side === side)?.name || null;
  }

  /**
   * The Discord callback ends on `/linked-accounts#linked=discord` or `#link_error=<code>`. Read once, then
   * dropped so a reload or a back button does not replay the message. True when the callback says it linked,
   * which the status read then confirms or refutes.
   */
  private readFragment(): boolean {
    const params = new URLSearchParams(window.location.hash.replace(/^#/, ''));
    const failed = params.get('link_error');
    const done = params.get('linked');
    if (failed === null && done === null) return false;

    window.history.replaceState(null, '', window.location.pathname + window.location.search);
    if (failed !== null) {
      this.refusal.set(Object.hasOwn(REFUSAL_KEYS, failed) ? REFUSAL_KEYS[failed] : REFUSAL_KEYS['link_failed']);
      return false;
    }
    return true;
  }

  /** Runs one action with the buttons held, and turns a coded refusal into the page's message. */
  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.refusal.set(null);
    try {
      await action();
    } catch (err) {
      const code = err instanceof HttpErrorResponse ? (err.error as { code?: string } | null)?.code : undefined;
      this.refusal.set(code && Object.hasOwn(REFUSAL_KEYS, code) ? REFUSAL_KEYS[code] : REFUSAL_KEYS['link_failed']);
    } finally {
      this.busy.set(false);
    }
  }

  private stateOf(side: LinkSide, sides: LinkSideStatus[] | null): SideState {
    if (sides === null) return side === this.ownSide() ? 'this-account' : 'not-linked';
    const found = sides.find(s => s.side === side);
    if (!found || !found.exists || found.pause === 'not_registered') return 'waiting';
    if (found.pause === 'admin_disabled') return 'paused-admin';
    if (found.pause === 'stopped') return 'stopped';
    return 'active';
  }

  /**
   * Telegram's redirect sign-in returns here with the signed fields in the query. The query is dropped at
   * once so the hash is not left in the address bar or history.
   */
  private takeTelegramReturn(): Record<string, string> | null {
    const data = readTelegramReturn(new URLSearchParams(window.location.search));
    if (data) clearTelegramReturn();
    return data;
  }
}
