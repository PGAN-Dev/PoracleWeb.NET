import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatRadioChange, MatRadioModule } from '@angular/material/radio';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { catchError, of } from 'rxjs';

import { LinkSide, LinkSideStatus } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { LinkedAccountService } from '../../core/services/linked-account.service';
import { SettingsService } from '../../core/services/settings.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { TelegramLoginButtonComponent } from '../../shared/components/telegram-login-button/telegram-login-button.component';

/** What a side's card says about it. `this-account` and `not-linked` only occur while nothing is linked. */
export type SideState = 'active' | 'not-linked' | 'paused-admin' | 'stopped' | 'this-account' | 'waiting';

export interface SideCard {
  side: LinkSide;
  state: SideState;
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

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatIconModule,
    MatRadioModule,
    MatSnackBarModule,
    TelegramLoginButtonComponent,
    TranslatePipe,
  ],
  selector: 'app-linked-accounts',
  standalone: true,
  styleUrl: './linked-accounts.component.scss',
  templateUrl: './linked-accounts.component.html',
})
export class LinkedAccountsComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
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
    return SIDES.map(side => ({ side, state: this.stateOf(side, status?.linked ? (status.sides ?? []) : null) }));
  });

  protected readonly defaultDestination = computed(() => this.linked.status()?.defaultDestination ?? null);

  protected readonly discordAvailable = signal(false);
  protected readonly isLinked = computed(() => this.linked.status()?.linked === true);

  /** True once the first status read has finished, so a read that failed is told apart from one still going. */
  protected readonly loaded = signal(false);

  /** Linking, unlinking and the default are refused while viewing as someone else, so they are not offered. */
  protected readonly readOnly = this.auth.isImpersonating;

  /** The translation key of the refusal to show, from a failed link or the Discord callback's fragment. */
  protected readonly refusal = signal<null | string>(null);

  /** Offering where new alerts go only makes sense once there are two places to send them. */
  protected readonly showDefault = computed(() => {
    const sides = this.linked.status()?.sides ?? [];
    return this.isLinked() && !this.readOnly() && sides.length === 2 && sides.every(s => s.exists);
  });

  protected readonly supportUrl = this.settings.supportUrl;

  protected readonly telegramBot = signal('');

  protected readonly telegramAvailable = computed(() => this.telegramBot() !== '');

  protected canLink(side: LinkSide): boolean {
    if (this.readOnly() || this.isLinked() || side === this.ownSide()) return false;
    return side === 'discord' ? this.discordAvailable() : this.telegramAvailable();
  }

  protected async chooseDefault(change: MatRadioChange): Promise<void> {
    await this.run(() => this.linked.setDefault(change.value as LinkSide));
  }

  ngOnInit(): void {
    this.readFragment();
    void this.linked.refresh().then(() => this.loaded.set(true));

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

  protected async onTelegramWidget(data: Record<string, string>): Promise<void> {
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
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('LINKED.UNLINK'),
        message: this.i18n.instant('LINKED.UNLINK_CONFIRM'),
        title: this.i18n.instant('LINKED.UNLINK_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (!confirmed) return;
      void this.run(async () => {
        await this.linked.unlink();
        this.snackBar.open(this.i18n.instant('LINKED.SNACK_UNLINKED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
      });
    });
  }

  /**
   * The Discord callback ends on `/linked-accounts#linked=discord` or `#link_error=<code>`. Read once, then
   * dropped so a reload or a back button does not replay the message.
   */
  private readFragment(): void {
    const params = new URLSearchParams(window.location.hash.replace(/^#/, ''));
    const failed = params.get('link_error');
    const done = params.get('linked');
    if (failed === null && done === null) return;

    window.history.replaceState(null, '', window.location.pathname + window.location.search);
    if (failed !== null) {
      this.refusal.set(Object.hasOwn(REFUSAL_KEYS, failed) ? REFUSAL_KEYS[failed] : REFUSAL_KEYS['link_failed']);
    } else {
      this.snackBar.open(this.i18n.instant('LINKED.SNACK_LINKED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
    }
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
}
