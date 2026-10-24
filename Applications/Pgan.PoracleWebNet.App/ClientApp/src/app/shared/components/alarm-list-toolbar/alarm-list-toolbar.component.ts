import { ChangeDetectionStrategy, Component, computed, effect, input, model, output, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

import { LinkSide } from '../../../core/models';
import { appKey } from '../../../core/services/alarm-move.service';
import { TranslatePluralPipe } from '../../pipes/translate-plural.pipe';
import { DestinationFilter } from '../../utils/alarm-list-query';
import { AppGlyphComponent } from '../app-glyph/app-glyph.component';

/** One entry in a list's sort menu: the value the list sorts on, and the i18n key naming it. */
export interface AlarmSortOption {
  label: string;
  value: string;
}

/**
 * The bar above every alarm list: search, the count, sort, and the select-mode toggle.
 *
 * Pokémon had all of this and the other nine had a count and the toggle, so thirty raid rules could
 * not be searched. Each list keeps its own idea of what a search matches and what a sort means (see
 * `alarm-list-query.ts`); the bar only holds the controls. Anything a list adds of its own (Pokémon's
 * quick filters) is projected between the search field and the count.
 *
 * When the rules exist but none match, the bar says so and offers the way back, so a search that
 * finds nothing never reads as "you have no alarms". When the app filter alone hides them all, it says
 * that instead: "Invasion alarms: none are delivered to Telegram."
 *
 * A linked person's app filter is a labelled segmented control ("Delivered to: All | Discord | Telegram"),
 * so it can't be taken for one more of the list's own filter chips.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AppGlyphComponent,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatTooltipModule,
    TranslatePipe,
    TranslatePluralPipe,
  ],
  selector: 'app-alarm-list-toolbar',
  standalone: true,
  styleUrl: './alarm-list-toolbar.component.scss',
  templateUrl: './alarm-list-toolbar.component.html',
})
export class AlarmListToolbarComponent {
  /** i18n key naming the list's alarms ("Invasion alarms"), for the line shown when the app filter hides them all. */
  readonly alarmsLabel = input('');

  protected readonly appKey = appKey;

  /** True for a linked person, who can filter the list by the app each alarm is delivered to. */
  readonly canFilterDestination = input(false);

  /** Emitted by "Clear search" when nothing matches; the list clears any filters of its own too. */
  readonly cleared = output<void>();

  /** All, or one app. Two-way bound to the list, which hides the rows of the other side. */
  readonly destinationFilter = model<DestinationFilter>('all');

  readonly destinationOptions: { label: string; value: 'all' | LinkSide }[] = [
    { label: 'ALARM.FILTER_ALL', value: 'all' },
    { label: 'DELIVER_TO.DISCORD', value: 'discord' },
    { label: 'DELIVER_TO.TELEGRAM', value: 'telegram' },
  ];

  /** True when the list has filters besides the search, so the way back clears those as well. */
  readonly hasFilters = input(false);

  readonly search = model('');

  /** The app filtered on when it, and nothing else, is what leaves the list empty; otherwise null. */
  readonly onlyDestinationFilter = computed<LinkSide | null>(() => {
    const filter = this.destinationFilter();
    if (filter === 'all' || !this.alarmsLabel() || this.search() || this.hasFilters()) return null;
    return filter;
  });

  /** i18n key for the search field's placeholder, naming what the type is searched by. */
  readonly searchPlaceholder = input.required<string>();

  readonly selectMode = input(false);

  readonly selectModeToggle = output<void>();

  /** How many rules the search and filters leave on screen. */
  readonly shown = input(0);

  readonly sort = model('');

  readonly sortOptions = input<AlarmSortOption[]>([]);

  /** How many rules the list holds. */
  readonly total = input(0);

  constructor() {
    // An unlinked pair has no filter to show, and a filter left on Telegram would hide every row for good.
    effect(() => {
      if (!this.canFilterDestination()) untracked(() => this.destinationFilter.set('all'));
    });
  }

  clear(): void {
    this.search.set('');
    this.destinationFilter.set('all');
    this.cleared.emit();
  }

  onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }
}
