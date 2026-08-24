import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';

import { ActiveHoursChipComponent } from './active-hours-chip.component';
import { ActiveHourEntry } from '../../../core/models/active-hours.models';
import { I18nService } from '../../../core/services/i18n.service';

describe('ActiveHoursChipComponent', () => {
  let component: ActiveHoursChipComponent;
  let fixture: ComponentFixture<ActiveHoursChipComponent>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService()],
      imports: [ActiveHoursChipComponent],
    });
    fixture = TestBed.createComponent(ActiveHoursChipComponent);
    component = fixture.componentInstance;
  });

  it('should create successfully', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should show "Manual only" when activeHours is empty array', () => {
    fixture.componentRef.setInput('activeHours', []);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.chip-empty')?.textContent).toContain('ACTIVE_HOURS_CHIP.MANUAL_ONLY');
    expect(el.querySelectorAll('.chip-active')).toHaveLength(0);
  });

  it('should show "Manual only" when activeHours uses default (empty)', () => {
    // Default input value is []
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.chip-empty')?.textContent).toContain('ACTIVE_HOURS_CHIP.MANUAL_ONLY');
  });

  it('should show schedule pills when activeHours has entries', () => {
    const entries: ActiveHourEntry[] = [{ day: 1, hours: 9, mins: 0 }];
    fixture.componentRef.setInput('activeHours', entries);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.chip-empty')).toBeNull();
    const pills = el.querySelectorAll('.chip-active');
    expect(pills).toHaveLength(1);
    expect(pills[0].textContent?.trim()).toContain('Mon');
    expect(pills[0].textContent?.trim()).toContain('9:00 AM');
  });

  it('should group identical times across days', () => {
    const entries: ActiveHourEntry[] = [
      { day: 1, hours: 9, mins: 0 },
      { day: 2, hours: 9, mins: 0 },
      { day: 3, hours: 18, mins: 0 },
    ];
    fixture.componentRef.setInput('activeHours', entries);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const pills = el.querySelectorAll('.chip-active');
    expect(pills).toHaveLength(2);
    // First pill should contain Mon-Tue 9:00 AM (grouped)
    expect(pills[0].textContent?.trim()).toContain('9:00 AM');
    // Second pill should contain Wed 6:00 PM
    expect(pills[1].textContent?.trim()).toContain('6:00 PM');
  });

  describe('repeating ranges (#808)', () => {
    function withEnglishRangeStrings(): void {
      const translate = TestBed.inject(TranslateService);
      translate.use('en');
      translate.setTranslation(
        'en',
        {
          PROFILES: {
            ACTIVE_HOURS_RANGE_EVERY: '{{days}} {{start}}–{{end}}, every {{step}}h',
            ACTIVE_HOURS_RANGE_HOURLY: '{{days}} {{start}}–{{end}}, hourly',
          },
        },
        true,
      );
    }

    it('should label an hourly range', () => {
      withEnglishRangeStrings();
      fixture.componentRef.setInput('activeHours', [{ day: 1, endHours: 17, endMins: 0, hours: 9, mins: 0, step: 1 }] as ActiveHourEntry[]);
      fixture.detectChanges();

      expect(component.pills()[0].label).toBe('Mon 9:00 AM–5:00 PM, hourly');
    });

    it('should label a stepped range', () => {
      withEnglishRangeStrings();
      fixture.componentRef.setInput('activeHours', [
        { day: 6, endHours: 17, endMins: 30, hours: 9, mins: 0, step: 2 },
        { day: 7, endHours: 17, endMins: 30, hours: 9, mins: 0, step: 2 },
      ] as ActiveHourEntry[]);
      fixture.detectChanges();

      expect(component.pills()[0].label).toBe('Weekends 9:00 AM–5:30 PM, every 2h');
    });

    it('follows a live display-language switch', () => {
      // The labels come from translate.instant inside a computed, which only stays current because
      // instant reads the translation store's signals -- an implementation detail of @ngx-translate
      // v18 that older versions did not have. Switching through I18nService is the path the language
      // menu takes, so this fails if either half stops holding.
      const i18n = TestBed.inject(I18nService);
      const translate = TestBed.inject(TranslateService);
      withEnglishRangeStrings();
      translate.setTranslation('it', { PROFILES: { ACTIVE_HOURS_RANGE_HOURLY: '{{days}} {{start}}–{{end}}, ogni ora' } }, true);

      fixture.componentRef.setInput('activeHours', [{ day: 1, endHours: 17, endMins: 0, hours: 9, mins: 0, step: 1 }] as ActiveHourEntry[]);
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.chip-active').textContent).toContain('hourly');

      i18n.use('it');
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.chip-active').textContent).toContain('ogni ora');
      expect(component.pills()[0].label).toBe('Mon 9:00 AM–5:00 PM, ogni ora');
    });

    it('should leave a single fire label untouched', () => {
      withEnglishRangeStrings();
      fixture.componentRef.setInput('activeHours', [{ day: 1, hours: 9, mins: 0 }] as ActiveHourEntry[]);
      fixture.detectChanges();

      expect(component.pills()[0].label).toBe('Mon 9:00 AM');
    });
  });
});
