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
  shortLabel?: string;
  totalCount?: number;
}

export interface RegionGroup {
  label: string;
  regions: RegionOption[];
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

    const match = available.find(r => r.id === wanted || r.label === wanted || r.shortLabel === wanted);
    if (match && this.selectedOption()?.id !== match.id) {
      this.selectedOption.set(match);
    }
  });

  readonly searchText = signal('');

  readonly filteredGroups = computed((): RegionGroup[] => {
    const search = this.searchText().toLowerCase();
    const all = this.regions();
    const filtered = search
      ? all.filter(r => r.label.toLowerCase().includes(search) || (r.shortLabel?.toLowerCase().includes(search) ?? false))
      : all;

    // Group by country-state prefix (first 2 parts of "US - VA - Richmond")
    const groupMap = new Map<string, RegionOption[]>();
    for (const region of filtered) {
      const parts = region.label.split(' - ');
      const groupKey = parts.length >= 2 ? parts.slice(0, 2).join(' - ') : parts[0] || 'Other';
      if (!groupMap.has(groupKey)) groupMap.set(groupKey, []);
      groupMap.get(groupKey)!.push(region);
    }

    return [...groupMap.entries()].map(([label, regions]) => ({ label, regions })).sort((a, b) => a.label.localeCompare(b.label));
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
    return option?.shortLabel ?? option?.label ?? '';
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
