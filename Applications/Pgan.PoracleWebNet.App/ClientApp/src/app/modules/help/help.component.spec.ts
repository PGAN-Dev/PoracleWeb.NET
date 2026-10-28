import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';

import { HelpComponent } from './help.component';
import { AuthService } from '../../core/services/auth.service';
import { SettingsService } from '../../core/services/settings.service';
import { ImageViewerDialogComponent } from '../../shared/components/image-viewer-dialog/image-viewer-dialog.component';

describe('HelpComponent screenshots', () => {
  let fixture: ComponentFixture<HelpComponent>;
  let dialog: { open: jest.Mock };

  const SHOT = '<img class="help-screenshot" src="assets/help/dashboard-overview.png" alt="Dashboard overview" /><p>Body copy.</p>';

  beforeEach(() => {
    dialog = { open: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MatDialog, useValue: dialog },
        { provide: SettingsService, useValue: { supportUrl: signal(null) } },
        { provide: AuthService, useValue: { isAdmin: signal(false) } },
      ],
      imports: [HelpComponent, NoopAnimationsModule],
    });

    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { HELP: { CONTENT_DASHBOARD: SHOT, IMAGE_ENLARGE: 'Click to enlarge' } }, true);
    translate.use('en');

    fixture = TestBed.createComponent(HelpComponent);
    fixture.detectChanges();
  });

  function screenshot(): HTMLImageElement {
    const img = fixture.nativeElement.querySelector('img.help-screenshot') as HTMLImageElement | null;
    expect(img).not.toBeNull();
    return img as HTMLImageElement;
  }

  it('marks injected screenshots as focusable buttons', () => {
    const img = screenshot();

    expect(img.tabIndex).toBe(0);
    expect(img.getAttribute('role')).toBe('button');
    expect(img.getAttribute('aria-label')).toBe('Dashboard overview — Click to enlarge');
  });

  it('opens the viewer when a screenshot is clicked', () => {
    screenshot().click();

    expect(dialog.open).toHaveBeenCalledWith(
      ImageViewerDialogComponent,
      expect.objectContaining({
        data: { alt: 'Dashboard overview', src: expect.stringContaining('assets/help/dashboard-overview.png') },
      }),
    );
  });

  it('opens the viewer on Enter and Space', () => {
    const img = screenshot();

    img.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'Enter' }));
    img.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: ' ' }));

    expect(dialog.open).toHaveBeenCalledTimes(2);
  });

  it('ignores other keys and clicks on surrounding prose', () => {
    const img = screenshot();
    img.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'a' }));
    (fixture.nativeElement.querySelector('.section-content p') as HTMLElement).click();

    expect(dialog.open).not.toHaveBeenCalled();
  });
});

/**
 * The footer used to tell everyone to "reach out to your community admin on Discord", which is wrong for
 * any instance whose members have no such admin. It now links whatever the operator set as `support_url`,
 * and names no destination when there is none rather than inventing one.
 */
describe('HelpComponent footer', () => {
  let fixture: ComponentFixture<HelpComponent>;
  let supportUrl: WritableSignal<string | null>;

  beforeEach(() => {
    supportUrl = signal<string | null>(null);

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: SettingsService, useValue: { supportUrl } },
        { provide: AuthService, useValue: { isAdmin: signal(false) } },
      ],
      imports: [HelpComponent, NoopAnimationsModule],
    });

    const translate = TestBed.inject(TranslateService);
    translate.setTranslation(
      'en',
      { HELP: { FOOTER: 'Still need help?', FOOTER_CONTACT: 'Contact support', FOOTER_NO_LINK: 'Contact the team that runs this site.' } },
      true,
    );
    translate.use('en');

    fixture = TestBed.createComponent(HelpComponent);
  });

  const footer = () => fixture.nativeElement.querySelector('.help-footer') as HTMLElement;

  it('links the operator support page when one is set', () => {
    supportUrl.set('https://example.com/support');
    fixture.detectChanges();

    const link = footer().querySelector('a') as HTMLAnchorElement | null;
    expect(link?.getAttribute('href')).toBe('https://example.com/support');
    expect(link?.textContent?.trim()).toBe('Contact support');
    expect(footer().textContent).toContain('Still need help?');
  });

  it('names no destination and draws no link when none is set', () => {
    fixture.detectChanges();

    expect(footer().querySelector('a')).toBeNull();
    expect(footer().textContent).toContain('Contact the team that runs this site.');
    expect(footer().textContent).not.toMatch(/admin/i);
  });
});

/**
 * The admin guide describes pages a member cannot open, so it is listed (and searchable) for admins only.
 * Every other section, the webhook one included, stays visible to everyone.
 */
describe('HelpComponent admin section', () => {
  let isAdmin: WritableSignal<boolean>;
  let fixture: ComponentFixture<HelpComponent>;

  beforeEach(() => {
    isAdmin = signal(false);

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: SettingsService, useValue: { supportUrl: signal(null) } },
        { provide: AuthService, useValue: { isAdmin } },
      ],
      imports: [HelpComponent, NoopAnimationsModule],
    });

    const translate = TestBed.inject(TranslateService);
    translate.setTranslation(
      'en',
      { HELP: { SECTION_ADMIN: 'For Admins', SECTION_FAQ: 'Frequently Asked Questions', SECTION_WEBHOOKS: 'Webhooks You Manage' } },
      true,
    );
    translate.use('en');

    fixture = TestBed.createComponent(HelpComponent);
  });

  const tocTitles = () =>
    Array.from(fixture.nativeElement.querySelectorAll('.toc-item span') as NodeListOf<HTMLElement>).map(el => el.textContent?.trim());

  it('leaves the admin guide out for a member but keeps the webhook and FAQ sections', () => {
    fixture.detectChanges();

    expect(tocTitles()).not.toContain('For Admins');
    expect(tocTitles()).toContain('Webhooks You Manage');
    expect(tocTitles()).toContain('Frequently Asked Questions');
    expect(fixture.nativeElement.querySelector('#section-admin')).toBeNull();
  });

  // Colour is data or nothing: a section's icon names it, and a per-section hue said nothing while the
  // darker ones fell to about 2:1 on the dark panel.
  it('draws every section and contents icon in the neutral icon colour', () => {
    fixture.detectChanges();
    const icons = Array.from(
      fixture.nativeElement.querySelectorAll('.section-icon, .section-icon mat-icon, .toc-item mat-icon') as NodeListOf<HTMLElement>,
    );

    expect(icons.length).toBeGreaterThan(0);
    for (const icon of icons) expect(icon.style.color).toBe('');
  });

  it('lists the admin guide for an admin', () => {
    isAdmin.set(true);
    fixture.detectChanges();

    expect(tocTitles()).toContain('For Admins');
    expect(fixture.nativeElement.querySelector('#section-admin')).not.toBeNull();
  });
});

/**
 * The Help page's sample filter pills arrive through [innerHTML], and Angular's sanitizer strips their
 * inline `style`, so the class has to draw the pill itself. Without a background they read as bold text,
 * not as the grey pills on the alarm cards they explain.
 */
describe('HelpComponent pill samples', () => {
  const css = (file: string): string => readFileSync(join(__dirname, file), 'utf8');
  const rule = (source: string, selector: string): string => {
    const start = source.indexOf(`${selector} {`);
    if (start < 0) throw new Error(`no ${selector} rule`);
    return source.slice(start, source.indexOf('}', start));
  };

  it('draws the pill with the alarm card pill’s own background and text colour', () => {
    const sample = rule(css('help.component.scss'), '.pill-sample');
    const card = rule(css('../../shared/components/alarm-card/alarm-card.component.scss'), '.rule-pill');
    const declared = (block: string, prop: string): string | undefined =>
      new RegExp(String.raw`(?:^|\n)\s*${prop}:\s*([^;]+);`).exec(block)?.[1];

    expect(declared(sample, 'background')).toBe(declared(card, 'background'));
    expect(declared(sample, 'color')).toBe(declared(card, 'color'));
  });
});

/**
 * A delegate choosing Send test notification on a destination's alarm expects the sample in their own DMs,
 * and it lands in the destination instead. The section says so in every language. See #1038.
 */
describe('Help: destinations you manage', () => {
  const locales = ['da', 'de', 'en', 'es', 'fr', 'it', 'nl', 'pl', 'pt', 'pt-BR', 'sv'];
  const content = (locale: string): string =>
    JSON.parse(readFileSync(join(__dirname, `../../../assets/i18n/${locale}.json`), 'utf8')).HELP.CONTENT_WEBHOOKS;

  it('says a test alert posts into the destination', () => {
    expect(content('en')).toContain('A test alert posts into the destination, not to your DMs.');
  });

  /** The managing paragraph gained a sentence; a locale left at two sentences was not translated. */
  it.each(locales)('%s carries the extra sentence in the managing paragraph', locale => {
    const managing = content(locale).match(/<p>(.*?)<\/p>/g)?.[2] ?? '';

    expect(managing.split(/(?<=[.!?])\s+/)).toHaveLength(3);
  });
});
