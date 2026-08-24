import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

import { ActiveHourEntry, formatRuleLabel, groupActiveHours } from '../../../core/models/active-hours.models';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, MatTooltipModule, TranslatePipe],
  selector: 'app-active-hours-chip',
  standalone: true,
  styleUrl: './active-hours-chip.component.scss',
  templateUrl: './active-hours-chip.component.html',
})
export class ActiveHoursChipComponent {
  private readonly translate = inject(TranslateService);
  readonly activeHours = input<ActiveHourEntry[]>([]);

  readonly groups = computed(() => groupActiveHours(this.activeHours()));

  readonly isEmpty = computed(() => this.activeHours().length === 0);

  /**
   * Reactive to a display-language switch, and by a route worth stating out loud: `translate.instant`
   * reads the store's `_currentLang` and `_translations` signals, so calling it inside a `computed`
   * registers them as dependencies and switching language invalidates these labels the same way a new
   * schedule does. That is true of @ngx-translate v18 and was not true of the versions before it, where
   * this shape went stale until something else knocked `activeHours`. The spec switches the language and
   * asserts the rendered pill follows; if a future version stops reading those signals it goes red here
   * rather than in front of a user.
   */
  readonly pills = computed(() =>
    this.groups().map(g => ({
      label: formatRuleLabel(g, (key, params) => this.translate.instant(key, params)),
    })),
  );

  readonly tooltipText = computed(() => {
    if (this.isEmpty()) return this.translate.instant('ACTIVE_HOURS_CHIP.NO_SCHEDULE');
    return this.pills()
      .map(p => p.label)
      .join('\n');
  });
}
