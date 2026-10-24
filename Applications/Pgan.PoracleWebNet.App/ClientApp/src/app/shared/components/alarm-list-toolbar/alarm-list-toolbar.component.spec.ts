import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';

import { AlarmListToolbarComponent } from './alarm-list-toolbar.component';

describe('AlarmListToolbarComponent delivery filter', () => {
  let fixture: ComponentFixture<AlarmListToolbarComponent>;
  const el = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), provideNoopAnimations()],
      imports: [AlarmListToolbarComponent],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      ALARM: { ALARMS_COUNT_OTHER: '{{count}} alarms', FILTER_ALL: 'All', SORT_BY: 'Sort by' },
      DELIVER_TO: { DISCORD: 'Discord', LABEL: 'Deliver to', TELEGRAM: 'Telegram' },
    });
    translate.use('en');
    fixture = TestBed.createComponent(AlarmListToolbarComponent);
    fixture.componentRef.setInput('searchPlaceholder', 'X');
    fixture.componentRef.setInput('total', 3);
    fixture.componentRef.setInput('shown', 3);
  });

  it('has no filter for a person with no link', () => {
    fixture.detectChanges();

    expect(el().querySelector('.destination-filter')).toBeNull();
  });

  it('offers All, Discord and Telegram when linked, and writes the choice back', () => {
    fixture.componentRef.setInput('canFilterDestination', true);
    fixture.detectChanges();

    const buttons = Array.from(el().querySelectorAll<HTMLButtonElement>('.destination-filter button'));
    expect(buttons.map(b => b.textContent?.trim())).toEqual(['All', 'Discord', 'Telegram']);

    buttons[2].click();
    fixture.detectChanges();

    expect(fixture.componentInstance.destinationFilter()).toBe('telegram');
  });

  it('goes back to All when the filter stops being offered (the pair was unlinked)', () => {
    fixture.componentRef.setInput('canFilterDestination', true);
    fixture.componentInstance.destinationFilter.set('telegram');
    fixture.detectChanges();

    fixture.componentRef.setInput('canFilterDestination', false);
    fixture.detectChanges();

    expect(fixture.componentInstance.destinationFilter()).toBe('all');
  });

  it('Clear filters puts the delivery filter back on All', () => {
    fixture.componentRef.setInput('canFilterDestination', true);
    fixture.componentInstance.destinationFilter.set('discord');

    fixture.componentInstance.clear();

    expect(fixture.componentInstance.destinationFilter()).toBe('all');
  });
});
