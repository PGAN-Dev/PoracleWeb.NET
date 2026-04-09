import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

import { ActiveHourEntry, compressDayRange, formatTime12h, groupActiveHours } from '../../../core/models/active-hours.models';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, MatTooltipModule, TranslateModule],
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

  readonly pills = computed(() =>
    this.groups().map(g => ({
      label: `${compressDayRange(g.days)} ${formatTime12h(g.hours, g.mins)}`,
    })),
  );

  readonly tooltipText = computed(() => {
    if (this.isEmpty()) return this.translate.instant('ACTIVE_HOURS_CHIP.NO_SCHEDULE');
    return this.pills()
      .map(p => p.label)
      .join('\n');
  });
}
