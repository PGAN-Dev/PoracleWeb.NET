import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

import { PlayerAccount } from '../../../core/models';
import { AuthService } from '../../../core/services/auth.service';
import { PlayerAccountService } from '../../../core/services/player-account.service';
import { PoracleConfigService } from '../../../core/services/poracle-config.service';
import { SettingsService } from '../../../core/services/settings.service';

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

  readonly dialogRef = inject(MatDialogRef<GameAccountsDialogComponent>);

  readonly label = signal('');
  readonly playerId = signal('');

  /** Read-only because of impersonation, which the API reports before self-service, so it gets its own words. */
  readonly readOnlyByImpersonation = computed(() => !this.isAdminView && this.auth.isImpersonating());

  protected readonly supportUrl = this.settings.supportUrl;

  /** The link is for a trainer blocked by the server setting, who can ask someone to link the account. */
  readonly showSupportLink = computed(() => !this.canEdit() && !this.readOnlyByImpersonation() && !!this.supportUrl());

  add(): void {
    const playerId = this.playerId();
    // Blank is the only thing refused here. The id itself is never trimmed or case-folded: the server is
    // the judge of what a valid player id is, and it is case-sensitive.
    if (!this.canEdit() || this.busy() || playerId.trim().length === 0) return;

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
    this.accounts.relabel(account.playerId, label, this.data.userId).subscribe({ error: () => undefined });
  }

  reload(): void {
    this.accounts.load(this.data.userId).subscribe();
  }

  unlink(playerId: string): void {
    if (!this.canEdit()) return;
    this.accounts.remove(playerId, this.data.userId).subscribe({ error: () => undefined });
  }
}
