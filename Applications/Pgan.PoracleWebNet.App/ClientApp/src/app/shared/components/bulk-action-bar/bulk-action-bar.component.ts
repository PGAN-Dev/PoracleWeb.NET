import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslatePipe } from '@ngx-translate/core';

import { LinkSide } from '../../../core/models';
import { appKey } from '../../../core/services/alarm-move.service';
import { TranslatePluralPipe } from '../../pipes/translate-plural.pipe';
import { AppGlyphComponent } from '../app-glyph/app-glyph.component';

/** How long the count has to hold still before it is read aloud. */
export const BULK_COUNT_SETTLE_MS = 700;

/**
 * How long the live region stays empty before it repeats a count it already holds. A live region is only
 * read when its text changes, so settling back on the announced count would otherwise say nothing.
 */
export const BULK_COUNT_REANNOUNCE_MS = 100;

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
 * A linked person also gets "Deliver to…", which moves the selection to the other app. On a phone that and
 * Change radius fold into "More", so the pinned row holds two buttons that fit at 360px in every locale
 * instead of three whose labels wrapped and pushed Delete past the edge; an unlinked person keeps Change
 * radius and Delete side by side as before. `allOn` names the app every selected alarm is already on, and
 * that app is offered disabled, saying so, rather than as a move that would do nothing.
 *
 * The list owns the selection and decides what counts: `count` is the selected cards still on screen
 * (`actionIds`), so a search that hides a ticked card takes it out of the count and out of the action.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AppGlyphComponent, MatButtonModule, MatIconModule, MatMenuModule, TranslatePipe, TranslatePluralPipe],
  selector: 'app-bulk-action-bar',
  standalone: true,
  styleUrl: './bulk-action-bar.component.scss',
  templateUrl: './bulk-action-bar.component.html',
})
export class BulkActionBarComponent {
  private timer: null | ReturnType<typeof setTimeout> = null;
  /** The app every selected alarm is already on, or null for a mixed selection (or none). */
  readonly allOn = input<LinkSide | null>(null);
  /**
   * The count a screen reader hears: set once the count has not moved for {@link BULK_COUNT_SETTLE_MS}, so
   * ticking five cards in a row is one announcement, not five. Null until the first change settles.
   */
  protected readonly announced = signal<null | number>(null);
  /** True while anything is ticked, on screen or not, so the way to clear a hidden tick stays offered. */
  readonly anySelected = input(false);
  protected readonly appKey = appKey;
  protected readonly apps: readonly LinkSide[] = ['discord', 'telegram'];
  /** True for a linked person: the bar then offers "Deliver to…" to move the selection to the other app. */
  readonly canMove = input(false);
  readonly changeRadius = output<void>();
  /** Selected cards the actions would act on. */
  readonly count = input.required<number>();
  readonly deleteSelected = output<void>();

  readonly deselectAll = output<void>();

  /** The app the person chose to move the selected alarms to. */
  readonly moveSelected = output<LinkSide>();

  readonly selectAll = output<void>();

  constructor() {
    let first = true;
    effect(() => {
      const count = this.count();
      if (first) {
        first = false;
        return;
      }
      untracked(() => this.settle(count));
    });
    inject(DestroyRef).onDestroy(() => this.clearTimer());
  }

  private clearTimer(): void {
    if (this.timer !== null) clearTimeout(this.timer);
    this.timer = null;
  }

  private settle(count: number): void {
    this.clearTimer();
    this.timer = setTimeout(() => {
      this.timer = null;
      if (this.announced() !== count) {
        this.announced.set(count);
        return;
      }
      this.announced.set(null);
      this.timer = setTimeout(() => {
        this.timer = null;
        this.announced.set(count);
      }, BULK_COUNT_REANNOUNCE_MS);
    }, BULK_COUNT_SETTLE_MS);
  }
}
