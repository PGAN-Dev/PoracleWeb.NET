import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { TemplateSelectorComponent } from './template-selector.component';
import { AuthService } from '../../../core/services/auth.service';
import { ConfigService } from '../../../core/services/config.service';
import { IconService } from '../../../core/services/icon.service';
import { SettingsService } from '../../../core/services/settings.service';
import { TemplateService } from '../../../core/services/template.service';

interface Entry {
  id: number;
  platform: string;
  template: Record<string, unknown>;
  type: string;
}

const DISCORD: Entry = {
  id: 1,
  platform: 'discord',
  template: {
    embed: { description: 'CP {{cp}}\n[Google]({{{googleMapUrl}}})', title: '**{{fullName}}** {{round iv}}%' },
  },
  type: 'monster',
};

function telegram(template: Record<string, unknown>): Entry {
  return { id: 1, platform: 'telegram', template, type: 'monster' };
}

describe('TemplateSelectorComponent', () => {
  let fixture: ComponentFixture<TemplateSelectorComponent>;

  function setup(userType: string, entries: Entry[]): HTMLElement {
    (TemplateSelectorComponent as unknown as { dtsCache: unknown }).dtsCache = null;
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: AuthService, useValue: { user: signal({ type: userType }) } },
        { provide: ConfigService, useValue: { apiHost: '' } },
        { provide: SettingsService, useValue: { siteSettings: signal({ enable_templates: 'true' }) } },
        { provide: TemplateService, useValue: { getTemplatesForType: () => of([1]) } },
        { provide: IconService, useValue: { getPokemonUrl: () => 'icon.png' } },
      ],
      imports: [TemplateSelectorComponent, NoopAnimationsModule],
    });
    fixture = TestBed.createComponent(TemplateSelectorComponent);
    fixture.componentInstance.alarmType = 'monster';
    fixture.componentInstance.value = '1';
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/config/dts').flush(entries);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  describe('Discord', () => {
    it('renders the embed exactly as before', () => {
      const el = setup('discord:user', [DISCORD]);

      const title = el.querySelector('.discord-embed .embed-title');
      expect(title?.innerHTML).toBe('<strong>Pikachu</strong> 98%');
      expect(el.querySelector('.discord-embed .embed-description')?.innerHTML).toBe('CP 3200<br><a href="#" class="embed-link">Google</a>');
      expect(el.querySelector('.telegram-bubble')).toBeNull();
    });

    it('does not use a Telegram template for a Discord user', () => {
      const el = setup('discord:user', [telegram({ content: '*Hi*' })]);
      expect(el.querySelector('.preview-wrapper')).toBeNull();
    });
  });

  describe('Telegram', () => {
    it('draws content in a chat bubble with the placeholders filled in', () => {
      const el = setup('telegram:user', [
        DISCORD,
        telegram({ content: '*{{fullName}}* {{round iv}}%\ncp:{{cp}} _L{{level}}_', parse_mode: 'Markdown' }),
      ]);

      const bubble = el.querySelector('.telegram-bubble');
      expect(bubble).not.toBeNull();
      expect(bubble?.querySelector('strong')?.textContent).toBe('Pikachu');
      expect(bubble?.querySelector('em')?.textContent).toBe('L35');
      expect(bubble?.textContent).toContain('98%');
      expect(bubble?.textContent).toContain('cp:3200');
      expect(el.querySelector('.discord-embed')).toBeNull();
    });

    it('treats a missing parse_mode as Markdown, like PoracleNG', () => {
      const el = setup('telegram:user', [telegram({ content: '*bold*' })]);
      expect(el.querySelector('.telegram-bubble strong')?.textContent).toBe('bold');
    });

    it('renders HTML, MarkdownV2 and links', () => {
      let el = setup('telegram:user', [telegram({ content: '<b>a</b> <u>b</u> <a href="https://x.test">c</a>', parse_mode: 'HTML' })]);
      expect(el.querySelector('.telegram-bubble strong')?.textContent).toBe('a');
      expect(el.querySelector('.telegram-bubble u')?.textContent).toBe('b');
      const link = el.querySelector('.telegram-bubble a');
      expect(link?.getAttribute('href')).toBe('https://x.test');
      expect(link?.getAttribute('rel')).toContain('noopener');

      el = setup('telegram:user', [telegram({ content: '__u__ ~s~', parse_mode: 'MarkdownV2' })]);
      expect(el.querySelector('.telegram-bubble u')?.textContent).toBe('u');
      expect(el.querySelector('.telegram-bubble s')?.textContent).toBe('s');
    });

    it('shows a line for a sticker and for a location pin', () => {
      const el = setup('telegram:user', [telegram({ content: 'hi', location: true, sticker: '{{{stickerUrl}}}' })]);
      expect(el.querySelectorAll('.telegram-extra').length).toBe(2);
    });

    it('shows no extra lines when the template sends neither', () => {
      const el = setup('telegram:user', [telegram({ content: 'hi', location: false })]);
      expect(el.querySelectorAll('.telegram-extra').length).toBe(0);
    });

    it('falls back to the plain text for a parse mode it cannot render', () => {
      const el = setup('telegram:user', [telegram({ content: '*not bold*', parse_mode: 'BBCode' })]);
      const bubble = el.querySelector('.telegram-bubble');
      expect(bubble?.querySelector('strong')).toBeNull();
      expect(bubble?.textContent).toContain('*not bold*');
    });

    it('draws no preview for an entry that has no content', () => {
      const el = setup('telegram:user', [telegram({ sticker: 'x' })]);
      expect(el.querySelector('.preview-wrapper')).toBeNull();
    });

    it('never turns template text into markup', () => {
      const el = setup('telegram:user', [
        telegram({
          content:
            '<script>window.__pwned=1</script><img src=x onerror="window.__pwned=1"> [x](javascript:window.__pwned=1) <a href="javascript:window.__pwned=1">y</a>',
          parse_mode: 'HTML',
        }),
      ]);

      const bubble = el.querySelector('.telegram-bubble') as HTMLElement;
      expect(bubble.querySelector('script, img')).toBeNull();
      expect(bubble.textContent).toContain('<script>');
      for (const a of Array.from(bubble.querySelectorAll('a'))) {
        expect(a.getAttribute('href') ?? '').not.toMatch(/javascript:/i);
      }
      expect((window as unknown as { __pwned?: number }).__pwned).toBeUndefined();
    });

    it('never links a javascript: address written in Markdown', () => {
      const el = setup('telegram:user', [telegram({ content: '[click](javascript:alert(1))', parse_mode: 'Markdown' })]);
      const a = el.querySelector('.telegram-bubble .telegram-link');
      expect(a?.textContent).toBe('click');
      expect(a?.getAttribute('href')).toBeNull();
    });
  });
});
