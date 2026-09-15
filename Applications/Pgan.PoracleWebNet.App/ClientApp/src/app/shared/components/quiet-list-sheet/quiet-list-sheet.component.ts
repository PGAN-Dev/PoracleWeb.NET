import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

import { MasterDataService } from '../../../core/services/masterdata.service';
import { Mute, MuteService } from '../../../core/services/mute.service';
import { ScannerService } from '../../../core/services/scanner.service';

interface QuietRow {
  countdown: string;
  key: string;
  mute: Mute;
  scopeLabel: string;
  subject: string;
}

/**
 * Everything that is quiet right now, and the way to lift any of it.
 *
 * Renders scopes PoracleWeb.NET cannot create — a pokestop or an everything mute set from the Discord
 * bot shows here and can be lifted here. A list that hides what it cannot make is worse than useless to
 * somebody trying to work out why they are getting nothing.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, TranslatePipe],
  selector: 'app-quiet-list-sheet',
  standalone: true,
  styleUrl: './quiet-list-sheet.component.scss',
  templateUrl: './quiet-list-sheet.component.html',
})
export class QuietListSheetComponent {
  private readonly masterData = inject(MasterDataService);
  /**
   * Ids a lookup has already been fired for, whatever it answered.
   *
   * Separate from resolvedNames on purpose. The effect below reruns every second (the mutes list is
   * recomputed by the countdown ticker, so it hands back a fresh array each tick), and a gym the
   * scanner cannot name never lands in resolvedNames -- so guarding on that map alone reissued the
   * lookup once a second for as long as the sheet stayed open. That is every gym mute with no scanner
   * DB behind it.
   */
  private readonly requestedIds = new Set<string>();

  /** Gym and station ids resolved to names as the scanner answers, keyed by id. */
  private readonly resolvedNames = signal<Record<string, string>>({});
  private readonly scanner = inject(ScannerService);
  private readonly translate = inject(TranslateService);

  protected readonly mutes = inject(MuteService);

  readonly rows = computed<QuietRow[]>(() =>
    this.mutes.mutes().map(mute => ({
      countdown: this.mutes.countdownLabel(Math.max(0, mute.expiresAt - Math.floor(Date.now() / 1000))),
      key: `${mute.scope}:${mute.value ?? ''}`,
      mute,
      scopeLabel: this.translate.instant(`QUIET.SCOPE_${mute.scope.toUpperCase()}`),
      subject: this.subjectFor(mute),
    })),
  );

  constructor() {
    // Names arrive after the list does, so resolve on every change rather than once at construction --
    // the refresh below has not answered yet when this runs. The resolution itself is untracked: it
    // reads and writes resolvedNames, and tracking that would make the effect retrigger itself.
    effect(() => {
      const mutes = this.mutes.mutes();
      untracked(() => this.resolveGymNames(mutes));
    });
    this.mutes.refresh(true);
  }

  resume(row: QuietRow): void {
    this.mutes.resume(row.mute.scope, row.mute.value).subscribe({ error: () => undefined });
  }

  resumeAll(): void {
    this.mutes.resumeAll().subscribe({ error: () => undefined });
  }

  /**
   * Gym names come from the scanner DB, which is optional. When it is absent the row falls back to the
   * id — still liftable, just less readable.
   */
  private resolveGymNames(mutes: Mute[]): void {
    for (const mute of mutes) {
      if (mute.scope !== 'gym' || !mute.value) continue;
      const id = mute.value;
      if (this.requestedIds.has(id)) continue;
      this.requestedIds.add(id);
      this.scanner.getGymById(id).subscribe({
        error: () => undefined,
        next: gym => {
          if (gym?.name) this.resolvedNames.set({ ...this.resolvedNames(), [id]: gym.name });
        },
      });
    }
  }

  private subjectFor(mute: Mute): string {
    if (mute.value === null) return this.translate.instant('QUIET.SUBJECT_EVERYTHING');

    if (mute.scope === 'pokemon') {
      return this.masterData.getPokemonName(Number(mute.value));
    }

    return this.resolvedNames()[mute.value] ?? mute.value;
  }
}
