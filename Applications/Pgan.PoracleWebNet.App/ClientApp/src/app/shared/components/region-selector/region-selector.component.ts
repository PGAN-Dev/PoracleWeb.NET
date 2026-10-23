import { Component, effect, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';

export interface RegionOption {
  count?: number;
  id?: number;
  label: string;
  selectedCount?: number;
  totalCount?: number;
  /** The caller's bucket for areas with no group: never folded under a heading, and listed last. */
  ungrouped?: boolean;
}

/** One option as drawn: under a heading it shows only the part after the heading. */
export interface RegionItem {
  option: RegionOption;
  text: string;
}

/** A heading over two or more options, or (heading null) a single option drawn on its own. */
export interface RegionGroup {
  heading: null | string;
  items: RegionItem[];
  key: string;
}

const SEPARATOR = ' - ';

function prefixOf(option: RegionOption): null | string {
  if (option.ungrouped) return null;
  const cut = option.label.lastIndexOf(SEPARATOR);
  if (cut <= 0 || !option.label.slice(cut + SEPARATOR.length).trim()) return null;
  return option.label.slice(0, cut);
}

/**
 * Builds the picker's headings from the names themselves. "US - VA - Richmond" and "US - VA - Norfolk" share
 * "US - VA", so they sit under it as "Richmond" and "Norfolk". A heading is drawn only when it covers two or
 * more options: a name with no " - ", or the only one under its prefix, is listed on its own with its full
 * name. Headings used to be the first two parts of every name, so a plain group name became a heading over
 * itself (#1029).
 */
export function groupRegions(options: RegionOption[]): RegionGroup[] {
  const byPrefix = new Map<string, RegionOption[]>();
  for (const option of options) {
    const prefix = prefixOf(option);
    if (prefix === null) continue;
    if (!byPrefix.has(prefix)) byPrefix.set(prefix, []);
    byPrefix.get(prefix)!.push(option);
  }

  const groups: (RegionGroup & { last: boolean })[] = [];
  for (const [prefix, members] of byPrefix) {
    if (members.length < 2) continue;
    groups.push({
      heading: prefix,
      items: members
        .map(option => ({ option, text: option.label.slice(prefix.length + SEPARATOR.length) }))
        .sort((a, b) => a.text.localeCompare(b.text)),
      key: `heading:${prefix}`,
      last: false,
    });
  }
  for (const option of options) {
    const prefix = prefixOf(option);
    if (prefix !== null && byPrefix.get(prefix)!.length >= 2) continue;
    groups.push({ heading: null, items: [{ option, text: option.label }], key: `option:${option.label}`, last: !!option.ungrouped });
  }

  return groups
    .sort((a, b) => Number(a.last) - Number(b.last) || (a.heading ?? a.items[0].text).localeCompare(b.heading ?? b.items[0].text))
    .map(({ last: _last, ...group }) => group);
}

@Component({
  imports: [
    FormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    TranslatePipe,
  ],
  selector: 'app-region-selector',
  standalone: true,
  styleUrl: './region-selector.component.scss',
  templateUrl: './region-selector.component.html',
})
export class RegionSelectorComponent {
  readonly regions = input<RegionOption[]>([]);
  readonly selectedOption = signal<RegionOption | null>(null);
  /**
   * The region the caller says is already chosen, by id or label.
   */
  /* Declared and then never read: the chip rendered purely off selectedOption, which starts null. So
   * the approval dialog showed an empty picker for a submission that already carried a region, while
   * still submitting the seeded id -- the admin approved a region they were never shown, and any touch
   * of the picker replaced it with 0. See #650. */
  readonly selectedValue = input<string | number | null>(null);

  /** Mirrors selectedValue into the visible selection whenever either side changes. See #650. */
  private readonly seedFromSelectedValue = effect(() => {
    const wanted = this.selectedValue();
    const available = this.regions();
    if (wanted === null || wanted === undefined || wanted === '') {
      return;
    }

    const match = available.find(r => r.id === wanted || r.label === wanted);
    if (match && this.selectedOption()?.id !== match.id) {
      this.selectedOption.set(match);
    }
  });

  readonly searchText = signal('');

  readonly filteredGroups = computed((): RegionGroup[] => {
    const search = this.searchText().toLowerCase();
    const all = this.regions();
    return groupRegions(search ? all.filter(r => r.label.toLowerCase().includes(search)) : all);
  });

  readonly label = input('Select Region');

  readonly placeholder = input('Search regions...');

  readonly regionSelected = output<RegionOption>();

  readonly showCounts = input(false);

  clearSelection(): void {
    this.selectedOption.set(null);
    this.searchText.set('');
    this.regionSelected.emit({ label: '' });
  }

  displayFn(option: RegionOption): string {
    return option?.label ?? '';
  }

  onClearInput(): void {
    this.searchText.set('');
  }

  onInputChange(value: string): void {
    this.searchText.set(value);
  }

  onOptionSelected(option: RegionOption): void {
    // The "All Regions" sentinel has an empty label — treat it as a clear so the field returns to the
    // search input rather than rendering a blank chip (issue #314).
    if (!option.label) {
      this.clearSelection();
      return;
    }
    this.selectedOption.set(option);
    this.searchText.set('');
    this.regionSelected.emit(option);
  }
}
