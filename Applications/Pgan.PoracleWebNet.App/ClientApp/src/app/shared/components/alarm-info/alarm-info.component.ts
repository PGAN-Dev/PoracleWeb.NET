import { Component, input } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

import { WhereChipComponent } from '../where-chip/where-chip.component';

/**
 * The one-line summary under an alarm card: where it reaches you.
 *
 * The distance display it used to hold could only say "areas" or a radius, which stopped being the
 * whole truth when alarms gained a per-alarm scope. Delegating to the Where chip means raids, eggs,
 * quests and max battles all describe themselves correctly without four copies of the logic.
 */
@Component({
  imports: [MatChipsModule, MatIconModule, MatTooltipModule, WhereChipComponent],
  selector: 'app-alarm-info',
  standalone: true,
  styleUrl: './alarm-info.component.scss',
  templateUrl: './alarm-info.component.html',
})
export class AlarmInfoComponent {
  clean = input(0);
  distance = input(0);

  /** True where the host card wires a click that opens the scope sheet. */
  editable = input(false);
  overrideAreas = input<null | string[] | undefined>(null);
  overrideLocationLabel = input<null | string | undefined>(null);

  /** The profile's own areas, used only to word the inherited case honestly. */
  profileAreas = input<string[]>([]);
  template = input<string | null>(null);
}
