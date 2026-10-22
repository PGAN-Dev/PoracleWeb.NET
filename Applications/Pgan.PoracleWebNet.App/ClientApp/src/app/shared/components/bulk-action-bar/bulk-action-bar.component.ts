import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

import { TranslatePluralPipe } from '../../pipes/translate-plural.pipe';

/**
 * The bar an alarm list shows in select mode: how many are selected, Select all / Deselect all, and the
 * two things a selection can do.
 *
 * The two actions used to sit side by side as equals, and on a phone Delete was the most prominent
 * button on the screen while Change radius looked like its twin. Now they have a hierarchy: selection
 * controls are text, Change radius is tonal, and Delete is the one filled, error-red button and names
 * how many it deletes. On a phone the bar is pinned to the bottom of the screen, where the thumb is
 * while ticking cards.
 *
 * The list owns the selection and decides what counts: `count` is the selected cards still on screen
 * (`actionIds`), so a search that hides a ticked card takes it out of the count and out of the action.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe, TranslatePluralPipe],
  selector: 'app-bulk-action-bar',
  standalone: true,
  styleUrl: './bulk-action-bar.component.scss',
  templateUrl: './bulk-action-bar.component.html',
})
export class BulkActionBarComponent {
  /** True while anything is ticked, on screen or not, so the way to clear a hidden tick stays offered. */
  readonly anySelected = input(false);
  readonly changeRadius = output<void>();
  /** Selected cards the actions would act on. */
  readonly count = input.required<number>();
  readonly deleteSelected = output<void>();
  readonly deselectAll = output<void>();
  readonly selectAll = output<void>();
}
