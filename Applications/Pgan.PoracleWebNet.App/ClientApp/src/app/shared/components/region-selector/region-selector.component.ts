import { Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslateModule } from '@ngx-translate/core';

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
    TranslateModule,
  ],
  selector: 'app-region-selector',
  standalone: true,
  styleUrl: './region-selector.component.scss',
  templateUrl: './region-selector.component.html',
})
export class RegionSelectorComponent {
  readonly regions = input<RegionOption[]>([]);
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

  readonly selectedOption = signal<RegionOption | null>(null);
  readonly selectedValue = input<string | number | null>(null);

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
    this.selectedOption.set(option);
    this.searchText.set('');
    this.regionSelected.emit(option);
  }
}
