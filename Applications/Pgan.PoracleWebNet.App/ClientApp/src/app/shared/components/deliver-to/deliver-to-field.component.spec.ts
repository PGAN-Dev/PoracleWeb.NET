import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';

import { DeliverToFieldComponent } from './deliver-to-field.component';
import { LinkSide, LinkStatus } from '../../../core/models';
import { LinkedAccountService } from '../../../core/services/linked-account.service';

const both: LinkStatus = {
  sides: [
    { side: 'discord', exists: true, pause: 'none' },
    { side: 'telegram', exists: true, pause: 'none' },
  ],
  defaultDestination: 'telegram',
  linked: true,
};

describe('DeliverToFieldComponent', () => {
  let fixture: ComponentFixture<DeliverToFieldComponent>;
  let status: ReturnType<typeof signal<LinkStatus | null>>;
  let control: FormControl<LinkSide | null>;

  const el = () => fixture.nativeElement as HTMLElement;

  function mount(initial: LinkStatus | null, value: LinkSide | null = null): void {
    status = signal(initial);
    control = new FormControl<LinkSide | null>(value);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), provideRouter([]), { provide: LinkedAccountService, useValue: new StubLinked(status) }],
      imports: [DeliverToFieldComponent],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      DELIVER_TO: {
        DISCORD: 'Discord',
        LABEL: 'Which app gets this alarm?',
        PAUSED: "{{app}} isn't receiving alerts, so nothing will arrive there for now.",
        PAUSED_NEXT: 'See why on Linked accounts.',
        TELEGRAM: 'Telegram',
      },
    });
    translate.use('en');
    fixture = TestBed.createComponent(DeliverToFieldComponent);
    fixture.componentRef.setInput('control', control);
    fixture.detectChanges();
  }

  it('renders nothing for a person with no link', () => {
    mount({ linked: false });

    expect(el().querySelector('.deliver-to')).toBeNull();
  });

  it('renders nothing before the status has been read', () => {
    mount(null);

    expect(el().querySelector('.deliver-to')).toBeNull();
  });

  it('renders nothing while one side has no account yet', () => {
    mount({ ...both, sides: [both.sides![0], { side: 'telegram', exists: false, pause: 'not_registered' }] });

    expect(el().querySelector('.deliver-to')).toBeNull();
  });

  it('offers both apps when linked and both sides exist', () => {
    mount(both);

    expect(el().querySelectorAll('mat-radio-button').length).toBe(2);
    expect(el().textContent).toContain('Which app gets this alarm?');
  });

  it('an add dialog starts on the default destination', () => {
    mount(both);

    expect(control.value).toBe('telegram');
  });

  it("an edit dialog keeps the alarm's own destination", () => {
    mount(both, 'discord');

    expect(control.value).toBe('discord');
  });

  it('warns once, in a line, when the chosen side is paused, and keeps it selectable', () => {
    mount({ ...both, sides: [{ side: 'discord', exists: true, pause: 'stopped' }, both.sides![1]] }, 'discord');

    const notes = el().querySelectorAll('.deliver-paused');
    expect(notes.length).toBe(1);
    expect(notes[0].textContent).toContain("Discord isn't receiving alerts");
    expect((el().querySelector('mat-radio-button[value="discord"] input') as HTMLInputElement).disabled).toBe(false);
  });

  it('gives the paused warning a next step: a link to Linked accounts', () => {
    mount({ ...both, sides: [{ side: 'discord', exists: true, pause: 'admin_disabled' }, both.sides![1]] }, 'discord');

    const link = el().querySelector<HTMLAnchorElement>('.deliver-paused a');
    expect(link?.textContent?.trim()).toBe('See why on Linked accounts.');
    expect(link?.getAttribute('href')).toBe('/linked-accounts');
  });

  it('names each app beside its glyph', () => {
    mount(both);

    const glyphs = Array.from(el().querySelectorAll('mat-radio-button app-app-glyph')).map(g => g.getAttribute('data-app'));
    expect(glyphs).toEqual(['discord', 'telegram']);
  });

  it('shows no warning when the chosen side is running', () => {
    mount({ ...both, sides: [{ side: 'discord', exists: true, pause: 'stopped' }, both.sides![1]] }, 'telegram');

    expect(el().querySelector('.deliver-paused')).toBeNull();
  });
});

class StubLinked {
  readonly canChoose;
  constructor(readonly status: ReturnType<typeof signal<LinkStatus | null>>) {
    this.canChoose = () => {
      const s = status();
      return !!s?.linked && (s.sides ?? []).length === 2 && (s.sides ?? []).every(x => x.exists);
    };
  }
}
