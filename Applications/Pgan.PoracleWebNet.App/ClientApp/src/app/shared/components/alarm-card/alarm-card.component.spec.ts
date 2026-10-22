import { Component, WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { AlarmCardComponent } from './alarm-card.component';
import { AlertLanguageService } from '../../../core/services/alert-language.service';
import { I18nService } from '../../../core/services/i18n.service';
import { RuleEdge, teamEdge } from '../../utils/rule-colours';

const LIVE_POKEMON =
  '**Bulbasaur**  | distance: 5000m | iv: 90%-100% | cp: 1200-4000 | level: 20-35 | stats: 0/0/0 - 15/15/15 | pvp ranking: greatpvp top100 (@0+) | size: XXS-XXL ';

@Component({
  imports: [AlarmCardComponent],
  standalone: true,
  template: `
    <app-alarm-card
      [edge]="edge()"
      [selectable]="selectable()"
      [selected]="selected()"
      selectLabel="Select Bulbasaur"
      [summary]="summary()"
      (selectedToggle)="toggles = toggles + 1">
      <img ruleMedia class="rule-sprite" alt="" />
      <span ruleTitle>Bulbasaur</span>
      <div ruleSubtitle class="rule-subtitle">#1</div>
      <span ruleStatus class="rule-tag">Clean</span>
      <div ruleFilters class="rule-pills"><span class="rule-pill">IV 90-100%</span></div>
      <span ruleScope class="scope">Anywhere in my areas</span>
      <button ruleActions class="edit">Edit</button>
    </app-alarm-card>
  `,
})
class HostComponent {
  readonly edge = signal<null | RuleEdge>(null);
  readonly selectable = signal(false);
  readonly selected = signal(false);
  readonly summary = signal<null | string>(null);
  toggles = 0;
}

describe('AlarmCardComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let displayLang: WritableSignal<string>;
  let alertLang: WritableSignal<null | string>;

  function create(display = 'en', alert: null | string = 'en'): HTMLElement {
    displayLang = signal(display);
    alertLang = signal<null | string>(alert);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: I18nService, useValue: { currentLang: displayLang } },
        { provide: AlertLanguageService, useValue: { resolved: alertLang } },
      ],
      imports: [HostComponent],
    });
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function card(el: HTMLElement): HTMLElement {
    return el.querySelector('.rule-card') as HTMLElement;
  }

  it('puts the subject in the heading, the status beside it, and scope and actions in the footer', () => {
    const el = create();

    expect(el.querySelector('app-alarm-card')?.classList).toContain('alarm-card');
    expect(el.querySelector('.rule-title')?.textContent?.trim()).toBe('Bulbasaur');
    expect(el.querySelector('.rule-media .rule-sprite')).not.toBeNull();
    expect(el.querySelector('.rule-status .rule-tag')?.textContent).toBe('Clean');
    expect(el.querySelector('.rule-foot .rule-scope .scope')).not.toBeNull();
    expect(el.querySelector('.rule-foot .rule-actions .edit')).not.toBeNull();
  });

  it('draws no edge unless it is given one', () => {
    const el = create();
    expect(card(el).classList).not.toContain('rule-card-edged');

    host.edge.set(teamEdge(2));
    fixture.detectChanges();

    expect(card(el).classList).toContain('rule-card-edged');
    expect(card(el).style.getPropertyValue('--rule-edge-light')).toBe(teamEdge(2).light);
    expect(card(el).style.getPropertyValue('--rule-edge-dark')).toBe(teamEdge(2).dark);
  });

  it('shows a labelled checkbox only in select mode, and reports a toggle to the list', () => {
    const el = create();
    expect(el.querySelector('mat-checkbox')).toBeNull();

    host.selectable.set(true);
    fixture.detectChanges();
    const input = el.querySelector('mat-checkbox input') as HTMLInputElement;
    expect(input.getAttribute('aria-label')).toBe('Select Bulbasaur');

    input.click();
    fixture.detectChanges();
    expect(host.toggles).toBe(1);

    host.selected.set(true);
    fixture.detectChanges();
    expect(card(el).classList).toContain('rule-card-selected');
  });

  describe('details', () => {
    function toggle(el: HTMLElement): HTMLButtonElement | null {
      return el.querySelector('.rule-details-toggle');
    }

    it('keeps the rule sentence closed until asked, then shows it without the Discord markdown', () => {
      const el = create();
      host.summary.set(LIVE_POKEMON);
      fixture.detectChanges();

      // Rendered but hidden, so aria-controls always names an element that exists.
      expect((el.querySelector('.rule-details') as HTMLElement).hidden).toBe(true);
      expect(toggle(el)?.getAttribute('aria-controls')).toBe(el.querySelector('.rule-details')?.id);
      expect(toggle(el)?.getAttribute('aria-expanded')).toBe('false');

      toggle(el)?.click();
      fixture.detectChanges();

      const details = el.querySelector('.rule-details') as HTMLElement;
      expect(details.hidden).toBe(false);
      expect(toggle(el)?.getAttribute('aria-expanded')).toBe('true');
      expect(toggle(el)?.getAttribute('aria-controls')).toBe(details?.id);
      expect(details?.textContent).toContain('Bulbasaur | distance: 5000m');
      expect(details?.textContent).not.toContain('**');

      toggle(el)?.click();
      fixture.detectChanges();
      expect((el.querySelector('.rule-details') as HTMLElement).hidden).toBe(true);
    });

    it('offers no control when PoracleNG sent nothing, or only blanks', () => {
      const el = create();
      expect(toggle(el)).toBeNull();

      host.summary.set('   ');
      fixture.detectChanges();
      expect(toggle(el)).toBeNull();
    });

    it('stays out of the way when the alert language is not the display language', () => {
      // PoracleNG localises the sentence with the alert language; the card follows the display one.
      const el = create('de', 'en');
      host.summary.set('Reward: **Pikachu** | distance: 500m ');
      fixture.detectChanges();

      expect(toggle(el)).toBeNull();
    });

    it('shows it when the two languages agree, whatever they are', () => {
      const el = create('de', 'de');
      host.summary.set('Belohnung: **Pikachu** | distance: 500m ');
      fixture.detectChanges();

      expect(toggle(el)).not.toBeNull();
    });

    it('treats an unresolvable alert language as a mismatch instead of guessing English', () => {
      const el = create('en', null);
      host.summary.set('Reward: **Pikachu** | distance: 500m ');
      fixture.detectChanges();

      expect(toggle(el)).toBeNull();
    });
  });
});
