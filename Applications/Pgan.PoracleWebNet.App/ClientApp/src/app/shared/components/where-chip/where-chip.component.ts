import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateService } from '@ngx-translate/core';

import { AlarmScope, describeScope, scopeOf } from '../../utils/alarm-scope';

/**
 * Where an alarm reaches you, as a sentence fragment: "Anywhere in my areas", "Within 2 km of Home",
 * "Only in Terrigal, Erina".
 *
 * Every alarm has always had an answer to this; before per-alarm scope it was an invisible inherited
 * one. The chip states it on the card and is the way into the Where sheet, so the same control reads
 * and edits the same idea wherever it appears.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, MatTooltipModule],
  selector: 'app-where-chip',
  standalone: true,
  styleUrl: './where-chip.component.scss',
  templateUrl: './where-chip.component.html',
})
export class WhereChipComponent {
  private readonly translate = inject(TranslateService);

  /** The alarm's radius in metres, as PoracleNG stores it. */
  readonly distance = input<number>(0);

  /** False on a read-only surface, where the chip states the scope without offering to change it. */
  readonly editable = input<boolean>(true);

  /** Areas the alarm is confined to, when it has any. */
  readonly overrideAreas = input<null | string[] | undefined>(null);

  /** Saved place the alarm measures its radius from, when it has one. */
  readonly overrideLocationLabel = input<null | string | undefined>(null);

  readonly scope = computed<AlarmScope>(() => scopeOf(this.overrideLocationLabel(), this.overrideAreas(), this.distance()));

  readonly icon = computed(() => {
    switch (this.scope().mode) {
      case 'areas':
        return 'map';
      case 'place':
        return 'place';
      default:
        return 'public';
    }
  });

  /** The inherited case is the quiet one: it is the default, and most cards will show it. */
  readonly isInherited = computed(() => this.scope().mode === 'profile');

  /** Areas the profile subscribes to, used only to describe the inherited case. */
  readonly profileAreas = input<string[]>([]);

  readonly label = computed(() => describeScope(this.scope(), this.profileAreas(), this.translate));
}
