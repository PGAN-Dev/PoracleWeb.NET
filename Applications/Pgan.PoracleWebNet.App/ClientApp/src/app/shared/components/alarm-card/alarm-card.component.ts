import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

import { LinkSide } from '../../../core/models';
import { appKey } from '../../../core/services/alarm-move.service';
import { AlertLanguageService } from '../../../core/services/alert-language.service';
import { I18nService } from '../../../core/services/i18n.service';
import { LinkedAccountService } from '../../../core/services/linked-account.service';
import { RuleEdge } from '../../utils/rule-colours';
import { cleanRuleSummary } from '../../utils/rule-summary';
import { AppGlyphComponent } from '../app-glyph/app-glyph.component';

let nextId = 0;

/** One number per card for every id it hands out, so the ids never depend on the order of the class fields. */
function cardIds(): { destination: string; details: string; title: string } {
  const n = nextId++;
  return { destination: `rule-destination-${n}`, details: `rule-details-${n}`, title: `rule-title-${n}` };
}

/**
 * One alarm rule, the same shape on all ten alarm lists.
 *
 * The subject (sprite or icon, then its name) reads first and largest, with the rule's status on the
 * right: quiet, clean, edit in place. For a linked person the app the rule is delivered to is named under
 * the subject, in words beside its glyph, and is part of the card's accessible name. Below it one line of filters with no eyebrow labels, then the
 * scope chip and the actions on one row. PoracleNG's own sentence for the rule sits behind "Details",
 * where it can stay as verbose as it likes without doubling the height of every card.
 *
 * The list projects each part by attribute (`ruleMedia`, `ruleTitle`, `ruleSubtitle`, `ruleStatus`,
 * `ruleFilters`, `ruleScope`, `ruleActions`; anything else lands between the filters and the footer),
 * so each type keeps its own vocabulary while the shared parts (`.rule-pill`, `.rule-tag`,
 * `.rule-subtitle`, `.rule-sprite`, `.rule-icon`) look the same everywhere.
 *
 * Colour is data or nothing: `edge` is a team, a tier or a species' primary type from
 * `rule-colours.ts`, and a card without one has no coloured edge.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'alarm-card' },
  imports: [AppGlyphComponent, MatButtonModule, MatCardModule, MatCheckboxModule, MatIconModule, MatTooltipModule, TranslatePipe],
  selector: 'app-alarm-card',
  standalone: true,
  styleUrl: './alarm-card.component.scss',
  templateUrl: './alarm-card.component.html',
})
export class AlarmCardComponent {
  private readonly alertLanguage = inject(AlertLanguageService);
  private readonly i18n = inject(I18nService);
  private readonly ids = cardIds();
  private readonly linked = inject(LinkedAccountService);

  protected readonly appKey = appKey;

  /** The raw `description` PoracleNG returned for the rule. */
  readonly summary = input<null | string | undefined>(null);

  readonly cleanedSummary = computed(() => cleanRuleSummary(this.summary()));

  /** The app this rule is delivered to; set only for a linked person, who then sees it named under the subject. */
  readonly destination = input<LinkSide | null | undefined>(null);

  readonly destinationId = this.ids.destination;

  /** True when the app this rule goes to isn't receiving alerts (stopped, disabled, not registered). */
  readonly destinationPaused = computed(() => {
    const app = this.destination();
    const side = app ? this.linked.status()?.sides?.find(s => s.side === app) : undefined;
    return !!side && side.pause !== 'none';
  });

  /** "Delivered to Telegram", or that it is delivered to an app which isn't receiving alerts. */
  readonly destinationSentence = computed(() => (this.destinationPaused() ? 'DELIVER_TO.DELIVERED_TO_PAUSED' : 'DELIVER_TO.DELIVERED_TO'));

  readonly detailsId = this.ids.details;

  readonly detailsOpen = signal(false);

  /** The team, tier or type colour for the card's edge, or null for no edge. */
  readonly edge = input<null | RuleEdge>(null);

  /**
   * Whether the sentence and the interface are in the same language. PoracleNG writes it in the alert
   * language while the card follows the display language, and a card carrying both is worse than one
   * carrying one. Null from `resolved()` means we cannot tell, which counts as a mismatch: guessing is
   * how one language's prose ends up under another language's filters.
   */
  readonly languagesAgree = computed(() => {
    const alert = this.alertLanguage.resolved();
    return alert !== null && alert.toLowerCase() === this.i18n.currentLang().toLowerCase();
  });

  /** Whether there is a sentence worth a Details control at all. */
  readonly hasDetails = computed(() => this.cleanedSummary().length > 0 && this.languagesAgree());

  /** Whether the card shows its select checkbox (the list is in select mode). */
  readonly selectable = input(false);

  readonly selected = input(false);

  /** Emitted when the checkbox is toggled; the list owns the selection. */
  readonly selectedToggle = output<void>();

  /** The checkbox's accessible name, normally "Select <subject>". */
  readonly selectLabel = input('');

  readonly titleId = this.ids.title;

  toggleDetails(): void {
    this.detailsOpen.update(open => !open);
  }
}
