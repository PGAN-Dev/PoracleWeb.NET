import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe } from '@ngx-translate/core';

import { PlayerAccount } from '../../../core/models';
import { AuthService } from '../../../core/services/auth.service';
import { PlayerAccountService } from '../../../core/services/player-account.service';
import { PoracleConfigService } from '../../../core/services/poracle-config.service';
import { SettingsService } from '../../../core/services/settings.service';
import { GameAccountsDialogComponent } from '../game-accounts-dialog/game-accounts-dialog.component';

/**
 * What the status line under the toggles says. Only one applies, in this order: a server with no rate
 * feed matches no personal rule whatever else is true, and an account list that could not be read says
 * nothing rather than claiming the trainer has no accounts.
 */
export type PersonalPanelStatus = 'askAdmin' | 'checking' | 'idle' | 'linkAccount' | 'noFeed' | 'unknown';

/**
 * The "shiny for me" block at the top of the Pokemon dialogs' Filters tab. Both dialogs own the two form
 * controls (so a stored bit round-trips whether or not the block is shown); this owns everything the
 * trainer needs to know about whether such a rule can ever fire.
 */
@Component({
  imports: [MatButtonModule, MatIconModule, MatSlideToggleModule, ReactiveFormsModule, TranslatePipe],
  selector: 'app-personal-filter-panel',
  standalone: true,
  styleUrl: './personal-filter-panel.component.scss',
  templateUrl: './personal-filter-panel.component.html',
})
export class PersonalFilterPanelComponent implements OnInit {
  private readonly accountService = inject(PlayerAccountService);
  private readonly auth = inject(AuthService);
  private readonly backgroundOn = signal(false);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly poracleConfig = inject(PoracleConfigService);
  private readonly settings = inject(SettingsService);
  private readonly shinyOn = signal(false);

  readonly accounts = this.accountService.accounts;
  /** The linked accounts as a trainer recognises them: the label, or the end of an unlabelled id. */
  readonly accountNames = computed(() => (this.accounts() ?? []).map(displayName));

  readonly background = input.required<FormControl<boolean | null>>();
  readonly bothOn = computed(() => this.shinyOn() && this.backgroundOn());

  /** Who can link an account from here: an administrator always, a trainer when PoracleNG allows it. Nobody while impersonating. */
  readonly canLink = computed(
    () => !this.auth.isImpersonating() && (!!this.poracleConfig.serverConfig().playerAccountsSelfService || this.auth.isAdmin()),
  );

  readonly shiny = input.required<FormControl<boolean | null>>();

  readonly status = computed<PersonalPanelStatus>(() => {
    if (!this.shinyOn() && !this.backgroundOn()) return 'idle';
    if (!this.poracleConfig.serverConfig().personalRatesConfigured) return 'noFeed';
    const accounts = this.accounts();
    if (accounts === null) return 'unknown';
    if (accounts.length > 0) return 'checking';
    return this.canLink() ? 'linkAccount' : 'askAdmin';
  });

  protected readonly supportUrl = this.settings.supportUrl;

  ngOnInit(): void {
    const shiny = this.shiny();
    const background = this.background();
    // Seeded first, so a value the dialog read from a stored rule counts before anything is touched.
    this.shinyOn.set(!!shiny.value);
    this.backgroundOn.set(!!background.value);
    shiny.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(v => this.shinyOn.set(!!v));
    background.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(v => this.backgroundOn.set(!!v));
    this.accountService.load().subscribe();
  }

  /** Opens the trainer's own game accounts, and re-reads them on close so the status follows. */
  openGameAccounts(): void {
    this.dialog
      .open(GameAccountsDialogComponent)
      .afterClosed()
      .subscribe(() => this.accountService.load().subscribe());
  }
}

/** An account's label, or "…" and the last four characters of its player id when it has none (PoracleNG's own convention). */
export function displayName(account: PlayerAccount): string {
  return account.label?.trim() || `…${account.playerId.slice(-4)}`;
}
