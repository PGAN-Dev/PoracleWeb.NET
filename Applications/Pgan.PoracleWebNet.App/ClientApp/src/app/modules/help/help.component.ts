import { ChangeDetectionStrategy, Component, computed, signal, viewChildren } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule, MatExpansionPanel } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

import { HELP_SECTIONS } from './help-sections';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatExpansionModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  selector: 'app-help',
  styleUrl: './help.component.scss',
  templateUrl: './help.component.html',
})
export class HelpComponent {
  protected readonly searchQuery = signal('');
  protected readonly sections = HELP_SECTIONS;
  protected readonly filteredSections = computed(() => {
    const query = this.searchQuery().toLowerCase().trim();
    if (!query) return this.sections;
    return this.sections.filter(
      s => s.title.toLowerCase().includes(query) || s.subtitle.toLowerCase().includes(query) || s.searchText.includes(query),
    );
  });

  protected readonly panels = viewChildren(MatExpansionPanel);

  protected scrollToSection(sectionId: string): void {
    const el = document.getElementById('section-' + sectionId);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
    const idx = this.filteredSections().findIndex(s => s.id === sectionId);
    const panels = this.panels();
    if (idx >= 0 && panels[idx]) {
      panels[idx].open();
    }
  }
}
