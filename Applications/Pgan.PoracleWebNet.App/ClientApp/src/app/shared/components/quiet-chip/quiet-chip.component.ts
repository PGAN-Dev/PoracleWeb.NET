import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateService } from '@ngx-translate/core';

import { MuteScope, MuteService } from '../../../core/services/mute.service';
import { QuietSheetComponent, QuietSheetData, QuietSheetResult } from '../quiet-sheet/quiet-sheet.component';

/**
 * Quieting one subject for a while, from wherever that subject is already named.
 *
 * Two forms of the same control. On a card it is an icon button that sits in the existing actions row;
 * on a list row it is a chip. Either way, when the subject is quiet it carries a live countdown, and
 * pressing it opens the same sheet.
 *
 * Renders nothing at all on a PoracleNG older than 5.2.0. There is nothing useful to say about a
 * server feature the operator has not got yet, and a control that 404s on every press is worse than
 * its absence.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule],
  selector: 'app-quiet-chip',
  standalone: true,
  styleUrl: './quiet-chip.component.scss',
  templateUrl: './quiet-chip.component.html',
})
export class QuietChipComponent {
  private readonly dialog = inject(MatDialog);
  private readonly translate = inject(TranslateService);
  /** 'chip' on a list row, 'icon' in a card's action row. */
  readonly appearance = input<'chip' | 'icon'>('icon');

  protected readonly mutes = inject(MuteService);

  readonly scope = input.required<MuteScope>();

  /** The identifier PoracleNG matches on: a gym or station id, a dex id as a string, an area name. */
  readonly value = input.required<string>();

  readonly remaining = computed(() => this.mutes.remainingFor(this.scope(), this.value()));

  readonly countdown = computed(() => {
    const remaining = this.remaining();
    return remaining === null ? '' : this.mutes.countdownLabel(remaining);
  });

  readonly isQuiet = computed(() => this.remaining() !== null);

  readonly label = computed(() =>
    this.isQuiet()
      ? this.translate.instant('QUIET.CHIP_QUIET', { countdown: this.countdown() })
      : this.translate.instant('QUIET.CHIP_ACTION'),
  );

  /**
   * The subject in the user's words, for the sheet title and the tooltip. When a surface has no name to
   * give (a station id nothing resolves, a gym the scanner DB does not know), this falls back to a
   * generic phrase rather than showing a raw identifier -- "Quiet this gym" is a sentence, and
   * "Quiet 0f3a91c2e4" is a database row.
   */
  readonly subject = input<string>('');

  /** The subject as the sheet and tooltip name it. */
  readonly subjectLabel = computed(() => this.subject() || this.translate.instant(`QUIET.SUBJECT_${this.scope().toUpperCase()}`));

  readonly tooltip = computed(() => {
    const subject = this.subjectLabel();
    return this.isQuiet()
      ? this.translate.instant('QUIET.TOOLTIP_QUIET', { countdown: this.countdown(), subject })
      : this.translate.instant('QUIET.TOOLTIP_ACTION', { subject });
  });

  constructor() {
    // Every appearance of the chip refreshes the list. The store lives in the processor's memory, so
    // "what was true when the page loaded" is not a safe answer for long.
    this.mutes.refresh();
  }

  open(event?: Event): void {
    event?.stopPropagation();

    const data: QuietSheetData = {
      isQuiet: this.isQuiet(),
      scope: this.scope(),
      subject: this.subjectLabel(),
      value: this.value(),
    };

    this.dialog
      .open<QuietSheetComponent, QuietSheetData, QuietSheetResult>(QuietSheetComponent, { width: '360px', data })
      .afterClosed()
      .subscribe(result => {
        if (!result) return;
        if (result.type === 'resume') {
          this.mutes.resume(this.scope(), this.value()).subscribe({ error: () => undefined });
        } else {
          this.mutes.quiet(this.scope(), this.value(), result.minutes).subscribe({ error: () => undefined });
        }
      });
  }
}
