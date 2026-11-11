import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

import { PoracleConfigService } from '../../../core/services/poracle-config.service';
import { isEdit } from '../../utils/clean-flags';

/** The alert types PoracleNG's `[tracking] default_edit` may list. */
export type EditDefaultType = 'egg' | 'incident' | 'lure' | 'pokemon' | 'raid';

/**
 * Says when a rule edits its alert in place because the server decided so, not the rule.
 *
 * PoracleNG 5.4.0's `[tracking] default_edit` lists alert types whose rules edit the original alert
 * even without the edit bit, existing rules included. A card that only reads the bit would show such
 * a rule as a fresh alert every change, which is not what the member gets. So beside the "Edits in
 * place" tag, which still means the rule's own bit, this adds "Edits in place (server default)" when
 * the type is listed and the bit is clear. A rule carrying the bit shows the usual tag alone: it
 * edits either way, and the member chose it.
 *
 * Two appearances. `tag` is the status chip on a list card; `hint` is a line under a dialog's edit
 * toggle, shown whenever the type is listed, telling the member the toggle changes nothing right now.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, MatTooltipModule, TranslatePipe],
  selector: 'app-edit-default-tag',
  standalone: true,
  styleUrl: './edit-default-tag.component.scss',
  templateUrl: './edit-default-tag.component.html',
})
export class EditDefaultTagComponent {
  private readonly poracleConfig = inject(PoracleConfigService);

  readonly appearance = input<'hint' | 'tag'>('tag');
  /** The rule's `clean` bitmask; the tag stays hidden when its own edit bit is set. */
  readonly clean = input<number | null | undefined>(0);
  /**
   * Whether edits of this type happen at all. Pokemon passes `tracking.pokemon_edit` here: with it off
   * PoracleNG drops every pokemon edit, default or not, so the tag would promise what never comes.
   */
  readonly enabled = input(true);
  readonly type = input.required<EditDefaultType>();

  /** PoracleNG edits this type in place regardless of the rule. */
  readonly serverDefault = computed(() => this.enabled() && (this.poracleConfig.serverConfig().defaultEdit ?? []).includes(this.type()));

  readonly showTag = computed(() => this.serverDefault() && !isEdit(this.clean() ?? 0));

  constructor() {
    // Idempotent: the first caller fetches, every later one shares the answer through the signal.
    this.poracleConfig.load().pipe(takeUntilDestroyed()).subscribe();
  }
}
