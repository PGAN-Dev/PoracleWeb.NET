import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

import { AlertLanguageService } from '../../../core/services/alert-language.service';
import { I18nService } from '../../../core/services/i18n.service';
import { cleanRuleSummary, ruleSummaryNeedsExpanding } from '../../utils/rule-summary';

/**
 * What this rule does, in Poracle's own words.
 *
 * The filter pills above it answer "which of these forty is the one I want" — same chips, same place on
 * every card, so they read first and this reads second. This line is what you read once you have stopped
 * on a card: a whole sentence, stating things the pills leave out (attack/defence floors, weight, the
 * PVP CP cap) at the cost of restating things they already show.
 *
 * It shows nothing at all when there is nothing worth showing: no description from Poracle, or a
 * Poracle too old to render one, and the card is exactly what it was before. The same nothing when the
 * alert language and the display language disagree — Poracle localises this sentence with the alert
 * language while the pills follow the display language, and a card carrying both is worse than a card
 * carrying one.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, MatTooltipModule, TranslatePipe],
  selector: 'app-rule-summary',
  standalone: true,
  styleUrl: './rule-summary.component.scss',
  templateUrl: './rule-summary.component.html',
})
export class RuleSummaryComponent {
  private readonly alertLanguage = inject(AlertLanguageService);
  private readonly i18n = inject(I18nService);

  /** The raw `description` PoracleNG returned on the alarm. */
  readonly text = input<null | string | undefined>(null);

  readonly cleaned = computed(() => cleanRuleSummary(this.text()));

  /** Long enough that the two-line clamp will bite, so the line is worth a control. */
  readonly expandable = computed(() => ruleSummaryNeedsExpanding(this.cleaned()));

  readonly expanded = signal(false);

  /**
   * Whether the sentence and the interface are in the same language. Null from `resolved()` means we
   * cannot tell what Poracle will write in, which counts as a mismatch: guessing is how you end up
   * putting one language's prose under another language's chips.
   */
  readonly languagesAgree = computed(() => {
    const alert = this.alertLanguage.resolved();
    return alert !== null && alert.toLowerCase() === this.i18n.currentLang().toLowerCase();
  });

  readonly visible = computed(() => this.cleaned().length > 0 && this.languagesAgree());

  toggle(): void {
    this.expanded.update(open => !open);
  }
}
