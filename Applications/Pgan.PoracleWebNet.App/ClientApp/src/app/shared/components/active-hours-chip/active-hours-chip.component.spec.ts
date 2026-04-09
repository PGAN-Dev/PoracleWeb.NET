import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { ActiveHoursChipComponent } from './active-hours-chip.component';
import { ActiveHourEntry } from '../../../core/models/active-hours.models';

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
});
