import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChildren } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule, MatExpansionPanel } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslateModule } from '@ngx-translate/core';

import { HELP_SECTIONS, HelpSection } from './help-sections';
import { I18nService } from '../../core/services/i18n.service';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatExpansionModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslateModule],
  selector: 'app-help',
  styleUrl: './help.component.scss',
  templateUrl: './help.component.html',
})
export class HelpComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly searchQuery = signal('');
  protected readonly sections = HELP_SECTIONS;
  protected readonly filteredSections = computed(() => {
    const query = this.searchQuery().toLowerCase().trim();
    if (!query) return this.sections;
    return this.sections.filter(s => {
      const title = this.i18n.instant(s.titleKey).toLowerCase();
      const subtitle = this.i18n.instant(s.subtitleKey).toLowerCase();
      const content = this.stripHtml(this.i18n.instant(s.contentKey)).toLowerCase();
      return title.includes(query) || subtitle.includes(query) || content.includes(query);
    });
  });

  protected readonly panels = viewChildren(MatExpansionPanel);

  protected isUntranslated(section: HelpSection): boolean {
    return this.i18n.currentLang() !== 'en' && this.i18n.instant(section.contentKey) === section.contentKey;
  }

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

  private stripHtml(html: string): string {
    return html.replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ');
  }
}
