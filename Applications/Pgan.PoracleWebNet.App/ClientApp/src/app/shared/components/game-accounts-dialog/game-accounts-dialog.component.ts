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
 * Links game accounts (player ids) for personal shiny alerts, for a trainer or for an administrator
 * acting on any human. Player ids are case-sensitive and are sent exactly as typed.
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
  /**
   * An administrator may always edit, their own accounts included. A trainer may only when PoracleNG's
   * self-service setting is on. Nobody may while impersonating, because the API refuses a write there (the
   * account is somebody else's).
   */
  readonly canEdit = computed(
    () =>
      this.isAdminView ||
      (!this.auth.isImpersonating() && (!!this.poracleConfig.serverConfig().playerAccountsSelfService || this.auth.isAdmin())),
  );

  readonly label = signal('');

  /** Labels are unique per human and compared case-insensitively, as PoracleNG compares them. */
  readonly labelError = computed<null | string>(() => {
    const label = this.label().trim().toLowerCase();
    if (label.length === 0) return null;
    return (this.accounts.accounts() ?? []).some(a => (a.label ?? '').toLowerCase() === label) ? 'GAME_ACCOUNTS.ERROR_LABEL_TAKEN' : null;
  });

  readonly playerId = signal('');

  /** Twelve digits is the shape of a friend code, which people reach for first. Allowed, but worth a word. */
  readonly looksLikeFriendCode = computed(() => FRIEND_CODE.test(this.playerId().replace(/\s/g, '')));

  /**
   * What is wrong with the typed player id, checked before anything is sent. The pattern is PoracleNG's
   * own (letters and digits, 1-256); its refusal is a regex nobody should have to read. A duplicate is
   * exact, because player ids are case-sensitive.
   */
  readonly playerIdError = computed<null | string>(() => {
    const id = this.playerId();
    if (id.length === 0) return null;
    // A friend code typed with its spaces fails the pattern; naming what it is helps more than the rule does.
    if (!PLAYER_ID.test(id)) return this.looksLikeFriendCode() ? 'GAME_ACCOUNTS.FRIEND_CODE_WARNING' : 'GAME_ACCOUNTS.ERROR_INVALID';
    if ((this.accounts.accounts() ?? []).some(a => a.playerId === id)) return 'GAME_ACCOUNTS.ERROR_DUPLICATE';
    return null;
  });

  readonly canAdd = computed(
    () => this.canEdit() && !this.busy() && this.playerId().trim().length > 0 && !this.playerIdError() && !this.labelError(),
  );

  readonly dialogRef = inject(MatDialogRef<GameAccountsDialogComponent>);

  /** Read-only because of impersonation, which the API reports before self-service, so it gets its own words. */
  readonly readOnlyByImpersonation = computed(() => !this.isAdminView && this.auth.isImpersonating());

  protected readonly supportUrl = this.settings.supportUrl;

  /** The link is for a trainer blocked by the server setting, who can ask someone to link the account. */
  readonly showSupportLink = computed(() => !this.canEdit() && !this.readOnlyByImpersonation() && !!this.supportUrl());

  add(): void {
    const playerId = this.playerId();
    // Never trimmed or case-folded: player ids are case-sensitive, and the pattern already refuses spaces.
    if (!this.canAdd()) return;

    this.busy.set(true);
    this.accounts.add(playerId, this.label().trim(), this.data.userId).subscribe({
      error: () => this.busy.set(false),
      next: () => {
        this.busy.set(false);
        this.playerId.set('');
        this.label.set('');
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
    this.accounts.relabel(account.playerId, label, this.data.userId).subscribe({
      error: () => undefined,
      next: () => this.snackBar.open(this.translate.instant('GAME_ACCOUNTS.LABEL_SAVED'), undefined, { duration: 2000 }),
    });
  }

  reload(): void {
    this.accounts.load(this.data.userId).subscribe();
  }

  /**
   * Unlinks at once, with an Undo: removing the last account silently turns every personal rule into one
   * that matches nothing, so a slip has to be cheap to take back. Undo re-links with the same label.
   */
  unlink(playerId: string): void {
    if (!this.canEdit()) return;
    const label = (this.accounts.accounts() ?? []).find(a => a.playerId === playerId)?.label ?? '';
    const userId = this.data.userId;
    this.accounts.remove(playerId, userId).subscribe({
      error: () => undefined,
      next: () => {
        const ref = this.snackBar.open(
          this.translate.instant('GAME_ACCOUNTS.UNLINKED', { name: label || playerId }),
          this.translate.instant('GAME_ACCOUNTS.UNDO'),
          { duration: 6000 },
        );
        ref.onAction().subscribe(() => this.accounts.add(playerId, label, userId).subscribe({ error: () => undefined }));
      },
    });
  }
}
