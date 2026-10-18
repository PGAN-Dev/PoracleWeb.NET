import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe } from '@ngx-translate/core';

import { PlayerAccountService } from '../../../core/services/player-account.service';
import { PoracleConfigService } from '../../../core/services/poracle-config.service';
import { SettingsService } from '../../../core/services/settings.service';

/**
 * What the status line under the toggles says. Only one applies, in this order: a server with no rate
 * feed matches no personal rule whatever else is true, and an account list that could not be read says
 * nothing rather than claiming the trainer has no accounts. With no account linked, the line says where
 * linking happens: the operator's map, the bot, or an administrator. Never here.
 */
export type PersonalPanelStatus = 'askAdmin' | 'checking' | 'idle' | 'linkOnMap' | 'linkWithBot' | 'noFeed' | 'unknown';

/** An account as the status line names it: its label, or its place in the list for "Account N". */
export interface PanelAccountName {
  label: null | string;
  n: number;
}

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
  private readonly backgroundOn = signal(false);
  private readonly destroyRef = inject(DestroyRef);
  private readonly poracleConfig = inject(PoracleConfigService);
  private readonly settings = inject(SettingsService);
  private readonly shinyOn = signal(false);

  readonly accounts = this.accountService.accounts;
  /** The linked accounts as a trainer recognises them. The player id is never one of them. */
  readonly accountNames = computed<PanelAccountName[]>(() =>
    (this.accounts() ?? []).map((a, i) => ({ label: a.label?.trim() || null, n: i + 1 })),
  );

  readonly background = input.required<FormControl<boolean | null>>();
  readonly bothOn = computed(() => this.shinyOn() && this.backgroundOn());
  readonly linkUrl = this.settings.gameAccountsLinkUrl;
  readonly shiny = input.required<FormControl<boolean | null>>();

  readonly status = computed<PersonalPanelStatus>(() => {
    if (!this.shinyOn() && !this.backgroundOn()) return 'idle';
    if (!this.poracleConfig.serverConfig().personalRatesConfigured) return 'noFeed';
    const accounts = this.accounts();
    if (accounts === null) return 'unknown';
    if (accounts.length > 0) return 'checking';
    if (this.linkUrl()) return 'linkOnMap';
    return this.poracleConfig.serverConfig().playerAccountsSelfService ? 'linkWithBot' : 'askAdmin';
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
}
