import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

import {
  ActiveHourEntry,
  compressDayRange,
  formatTime12h,
  groupActiveHours,
} from '../../../core/models/active-hours.models';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, MatTooltipModule],
  selector: 'app-active-hours-chip',
  standalone: true,
  styleUrl: './active-hours-chip.component.scss',
  templateUrl: './active-hours-chip.component.html',
})
export class ActiveHoursChipComponent {
  readonly activeHours = input<ActiveHourEntry[]>([]);

  readonly groups = computed(() => groupActiveHours(this.activeHours()));

  readonly isEmpty = computed(() => this.activeHours().length === 0);

  readonly pills = computed(() =>
    this.groups().map(g => ({
      label: `${compressDayRange(g.days)} ${formatTime12h(g.hours, g.mins)}`,
    })),
  );

  readonly tooltipText = computed(() => {
    if (this.isEmpty()) return 'No schedule set — profile runs manually or always on';
    return this.pills()
      .map(p => p.label)
      .join('\n');
  });
}
