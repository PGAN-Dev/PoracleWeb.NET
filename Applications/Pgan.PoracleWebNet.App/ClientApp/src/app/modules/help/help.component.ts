import { afterRenderEffect, ChangeDetectionStrategy, Component, computed, ElementRef, inject, signal, viewChildren } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatExpansionModule, MatExpansionPanel } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';

import { HELP_SECTIONS, HelpSection } from './help-sections';
import { I18nService } from '../../core/services/i18n.service';
import { ImageViewerDialogComponent } from '../../shared/components/image-viewer-dialog/image-viewer-dialog.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatExpansionModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  selector: 'app-help',
  styleUrl: './help.component.scss',
  templateUrl: './help.component.html',
})
export class HelpComponent {
  private readonly contentHosts = viewChildren<ElementRef<HTMLElement>>('sectionContent');
  private readonly dialog = inject(MatDialog);
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

  constructor() {
    // Section bodies are injected as raw HTML from the translation bundles, so the screenshots
    // inside them can't carry template bindings. Re-runs whenever the rendered set or the
    // language changes, both of which replace that HTML.
    afterRenderEffect(() => {
      this.filteredSections();
      this.i18n.currentLang();
      const hint = this.i18n.instant('HELP.IMAGE_ENLARGE');
      for (const host of this.contentHosts()) {
        for (const img of host.nativeElement.querySelectorAll<HTMLImageElement>('img.help-screenshot')) {
          img.tabIndex = 0;
          img.setAttribute('role', 'button');
          img.setAttribute('aria-label', img.alt ? `${img.alt} — ${hint}` : hint);
        }
      }
    });
  }

  protected isUntranslated(section: HelpSection): boolean {
    return this.i18n.currentLang() !== 'en' && this.i18n.instant(section.contentKey) === section.contentKey;
  }

  protected onContentClick(event: Event): void {
    const img = this.screenshotFrom(event.target);
    if (img) this.openViewer(img);
  }

  protected onContentKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter' && event.key !== ' ') return;
    const img = this.screenshotFrom(event.target);
    if (!img) return;
    event.preventDefault();
    this.openViewer(img);
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

  private openViewer(img: HTMLImageElement): void {
    this.dialog.open(ImageViewerDialogComponent, {
      maxWidth: '96vw',
      ariaLabel: img.alt,
      data: { alt: img.alt, src: img.src },
      maxHeight: '96vh',
      panelClass: 'image-viewer-panel',
    });
  }

  private screenshotFrom(target: EventTarget | null): HTMLImageElement | null {
    const el = target as HTMLElement | null;
    return el?.tagName === 'IMG' && el.classList.contains('help-screenshot') ? (el as HTMLImageElement) : null;
  }

  private stripHtml(html: string): string {
    return html.replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ');
  }
}
