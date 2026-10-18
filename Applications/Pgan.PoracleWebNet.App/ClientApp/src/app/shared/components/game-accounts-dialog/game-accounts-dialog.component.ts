import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

import { PlayerAccount } from '../../../core/models';
import { AuthService } from '../../../core/services/auth.service';
import { PlayerAccountService } from '../../../core/services/player-account.service';
import { PoracleConfigService } from '../../../core/services/poracle-config.service';
import { SettingsService } from '../../../core/services/settings.service';

/** PoracleNG's player id rule: 1-256 ASCII letters or digits. */
const PLAYER_ID = /^[A-Za-z0-9]{1,256}$/;
/** A Pokemon GO friend (trainer) code: twelve digits, often typed with spaces. */
const FRIEND_CODE = /^\d{12}$/;

export interface GameAccountsDialogData {
  /** The name to show when an administrator is managing someone else's accounts. */
  displayName?: string;
  /** Set by an administrator managing another human's accounts; absent for a trainer's own. */
  userId?: string;
}

/**
 * Where a trainer is told to go to link an account: the operator's map (which pushes accounts into
 * PoracleNG), the bot's `!account` when self-service lets trainers use it, or an administrator.
 */
export type LinkRoute = 'admin' | 'bot' | 'map';

/**
 * The game accounts linked to a human, for personal shiny alerts.
 *
 * The player id never surfaces: accounts are listed by label, or "Account N", and addressed by the opaque
 * handle the server answers. Trainers don't link here at all, because the maps people already use (ReactMap's
 * personal shiny support, Diadem) look the player id up from a friend code and push it into PoracleNG; the
 * dialog sends them there (`game_accounts_link_url`), or to the bot. The one place an id is typed is the
 * administrator's view from Admin > Users, masked, for support cases.
 */
@Component({
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatTooltipModule,
    TranslatePipe,
  ],
  selector: 'app-game-accounts-dialog',
  standalone: true,
  styleUrl: './game-accounts-dialog.component.scss',
  templateUrl: './game-accounts-dialog.component.html',
})
export class GameAccountsDialogComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly poracleConfig = inject(PoracleConfigService);
  private readonly settings = inject(SettingsService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  protected readonly accounts = inject(PlayerAccountService);
  readonly busy = signal(false);
  protected readonly data = inject<GameAccountsDialogData | null>(MAT_DIALOG_DATA, { optional: true }) ?? {};

  /** Whether this viewer is an administrator acting on someone else's accounts. */
  readonly isAdminView = !!this.data.userId;

  readonly label = signal('');

  /** Labels are unique per human and compared case-insensitively, as PoracleNG compares them. */
  readonly labelError = computed<null | string>(() => {
    const label = this.label().trim().toLowerCase();
    if (label.length === 0) return null;
    return (this.accounts.accounts() ?? []).some(a => (a.label ?? '').toLowerCase() === label) ? 'GAME_ACCOUNTS.ERROR_LABEL_TAKEN' : null;
  });

  readonly playerId = signal('');

  /** Admin view only. Twelve digits is the shape of a friend code, which people reach for first. */
  readonly looksLikeFriendCode = computed(() => FRIEND_CODE.test(this.playerId().replace(/\s/g, '')));

  /** What is wrong with the typed player id (admin view). The pattern is PoracleNG's own; its refusal is a regex. */
  readonly playerIdError = computed<null | string>(() => {
    const id = this.playerId();
    if (id.length === 0) return null;
    if (!PLAYER_ID.test(id)) return this.looksLikeFriendCode() ? 'GAME_ACCOUNTS.FRIEND_CODE_WARNING' : 'GAME_ACCOUNTS.ERROR_INVALID';
    return null;
  });

  readonly canAdd = computed(
    () => this.isAdminView && !this.busy() && this.playerId().trim().length > 0 && !this.playerIdError() && !this.labelError(),
  );

  /**
   * Relabel and unlink. An administrator may always, their own accounts included. A trainer may only when
   * PoracleNG's self-service setting is on. Nobody may while impersonating, because the API refuses a write
   * there (the account is somebody else's).
   */
  readonly canEdit = computed(
    () =>
      this.isAdminView ||
      (!this.auth.isImpersonating() && (!!this.poracleConfig.serverConfig().playerAccountsSelfService || this.auth.isAdmin())),
  );

  readonly dialogRef = inject(MatDialogRef<GameAccountsDialogComponent>);

  readonly linkUrl = this.settings.gameAccountsLinkUrl;

  /** Read-only because of impersonation, which the API reports before self-service, so it gets its own words. */
  readonly readOnlyByImpersonation = computed(() => !this.isAdminView && this.auth.isImpersonating());

  /** Where the trainer's own view sends them to link an account. Null for an administrator's view of someone else. */
  readonly linkRoute = computed<LinkRoute | null>(() => {
    if (this.isAdminView || this.readOnlyByImpersonation()) return null;
    if (this.linkUrl()) return 'map';
    return this.poracleConfig.serverConfig().playerAccountsSelfService ? 'bot' : 'admin';
  });

  /** The admin's id field is masked; the eye reveals it to check a paste. */
  readonly showPlayerId = signal(false);

  protected readonly supportUrl = this.settings.supportUrl;

  /** An account as people recognise it: its label, or "Account N" in list order. */
  accountName(account: PlayerAccount, index: number): string {
    return account.label?.trim() || this.translate.instant('GAME_ACCOUNTS.UNNAMED', { n: index + 1 });
  }

  add(): void {
    // Never trimmed or case-folded: player ids are case-sensitive, and the pattern already refuses spaces.
    if (!this.canAdd()) return;

    this.busy.set(true);
    this.accounts.add(this.playerId(), this.label().trim(), this.data.userId).subscribe({
      error: () => this.busy.set(false),
      next: () => {
        this.busy.set(false);
        // The id is gone from the page the moment it is linked.
        this.playerId.set('');
        this.label.set('');
        this.showPlayerId.set(false);
      },
    });
  }

  ngOnDestroy(): void {
    this.accounts.reset();
  }

  ngOnInit(): void {
    // Reached from the admin page without the shell having read the server config yet; the load is cached.
    this.poracleConfig.load().subscribe();
    this.accounts.reset();
    this.accounts.load(this.data.userId).subscribe();
  }

  relabel(account: PlayerAccount, value: string): void {
    const label = value.trim();
    if (!this.canEdit() || label === (account.label ?? '')) return;
    this.accounts.relabel(account.id, label, this.data.userId).subscribe({
      error: () => undefined,
      next: () => this.snackBar.open(this.translate.instant('GAME_ACCOUNTS.LABEL_SAVED'), undefined, { duration: 2000 }),
    });
  }

  reload(): void {
    this.accounts.load(this.data.userId).subscribe();
  }

  /**
   * Unlinks at once, with an Undo: removing the last account silently turns every personal rule into one
   * that matches nothing, and a trainer can't link it back here, so a slip has to be cheap to take back.
   * Undo hands the server the sealed token the unlink answered with; the player id never comes back.
   */
  unlink(account: PlayerAccount, index: number): void {
    if (!this.canEdit()) return;
    const name = this.accountName(account, index);
    const userId = this.data.userId;
    this.accounts.remove(account.id, userId).subscribe({
      error: () => undefined,
      next: undoToken => {
        const ref = this.snackBar.open(
          this.translate.instant('GAME_ACCOUNTS.UNLINKED', { name }),
          this.translate.instant('GAME_ACCOUNTS.UNDO'),
          {
            duration: 6000,
          },
        );
        ref.onAction().subscribe(() => this.accounts.restore(undoToken, userId).subscribe({ error: () => undefined }));
      },
    });
  }
}
