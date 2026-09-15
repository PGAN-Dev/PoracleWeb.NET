import { ComponentRef, WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { RuleSummaryComponent } from './rule-summary.component';
import { AlertLanguageService } from '../../../core/services/alert-language.service';
import { I18nService } from '../../../core/services/i18n.service';

const LIVE_POKEMON =
  '**Bulbasaur**  | distance: 5000m | iv: 90%-100% | cp: 1200-4000 | level: 20-35 | stats: 0/0/0 - 15/15/15 | pvp ranking: greatpvp top100 (@0+) | size: XXS-XXL ';

describe('RuleSummaryComponent', () => {
  let fixture: ComponentFixture<RuleSummaryComponent>;
  let ref: ComponentRef<RuleSummaryComponent>;
  let displayLang: WritableSignal<string>;
  let alertLang: WritableSignal<null | string>;

  function create(text: null | string | undefined, display = 'en', alert: null | string = 'en'): RuleSummaryComponent {
    displayLang = signal(display);
    alertLang = signal<null | string>(alert);

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: I18nService, useValue: { currentLang: displayLang } },
        { provide: AlertLanguageService, useValue: { resolved: alertLang } },
      ],
      imports: [RuleSummaryComponent],
    });
    fixture = TestBed.createComponent(RuleSummaryComponent);
    ref = fixture.componentRef;
    ref.setInput('text', text);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  function rendered(): string {
    return (fixture.nativeElement as HTMLElement).textContent?.trim() ?? '';
  }

  it('prints the rule as a sentence, with the Discord markdown taken out', () => {
    const summary = create('Reward: **Pikachu** | distance: 500m ');

    expect(summary.visible()).toBe(true);
    expect(rendered()).toContain('Reward: Pikachu | distance: 500m');
    expect(rendered()).not.toContain('**');
  });

  it('renders nothing when Poracle sent no description, so the pills carry on alone', () => {
    const summary = create(null);

    expect(summary.visible()).toBe(false);
    expect((fixture.nativeElement as HTMLElement).querySelector('.rule-summary')).toBeNull();
  });

  it('renders nothing for a blank description rather than an empty hairline', () => {
    expect(create('   ').visible()).toBe(false);
  });

  it('stays out of the way when the alert language is not the display language', () => {
    // Poracle localises this sentence with the alert language; the pills above it follow the display
    // language. A card must not carry both.
    const summary = create('Reward: **Pikachu** | distance: 500m ', 'de', 'en');

    expect(summary.visible()).toBe(false);
  });

  it('shows it when the two languages agree, whatever they are', () => {
    // The legitimate case: a German user whose alerts are German still gets the line.
    expect(create('Belohnung: **Pikachu** | distance: 500m ', 'de', 'de').visible()).toBe(true);
  });

  it('treats an unresolvable alert language as a mismatch instead of guessing English', () => {
    // resolved() is null when Poracle's own locale maps onto no UI language. Guessing would put ja
    // prose under en chips.
    expect(create('Reward: **Pikachu** | distance: 500m ', 'en', null).visible()).toBe(false);
  });

  it('offers an expand control on a long rule and collapses again', () => {
    const summary = create(LIVE_POKEMON);
    const host = fixture.nativeElement as HTMLElement;

    expect(summary.expandable()).toBe(true);
    const button = host.querySelector('button');
    expect(button).not.toBeNull();
    expect(button?.getAttribute('aria-expanded')).toBe('false');
    expect(host.querySelector('.rule-summary-clamped')).not.toBeNull();

    button?.click();
    fixture.detectChanges();

    expect(summary.expanded()).toBe(true);
    expect(host.querySelector('.rule-summary-clamped')).toBeNull();
  });

  it('offers no control on a short rule, because there is nothing to expand', () => {
    const summary = create('**Level 5 raids**  without rsvp updates');

    expect(summary.expandable()).toBe(false);
    expect((fixture.nativeElement as HTMLElement).querySelector('button')).toBeNull();
  });
});
