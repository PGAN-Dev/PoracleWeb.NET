import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl } from '@angular/forms';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';

import { RaidSettingsSectionComponent } from './raid-settings-section.component';

describe('RaidSettingsSectionComponent', () => {
  let fixture: ComponentFixture<RaidSettingsSectionComponent>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService()],
      imports: [RaidSettingsSectionComponent, NoopAnimationsModule],
    });
    fixture = TestBed.createComponent(RaidSettingsSectionComponent);
    fixture.componentRef.setInput('teamControl', new FormControl<number | null>(4));
    fixture.componentRef.setInput('rsvpControl', new FormControl<number | null>(0));
  });

  it('should render team selector, RSVP toggle, and gym picker', () => {
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('mat-select')).toBeTruthy();
    expect(el.querySelector('app-rsvp-toggle')).toBeTruthy();
    expect(el.querySelector('app-gym-picker')).toBeTruthy();
  });

  it('should bind the team control to the mat-select', () => {
    const team = new FormControl<number | null>(2);
    fixture.componentRef.setInput('teamControl', team);
    fixture.detectChanges();
    team.setValue(3);
    fixture.detectChanges();
    expect(team.value).toBe(3);
  });

  it('should forward rsvp control to the rsvp toggle', () => {
    const rsvp = new FormControl<number | null>(2);
    fixture.componentRef.setInput('rsvpControl', rsvp);
    fixture.detectChanges();
    const toggle = fixture.nativeElement.querySelector('app-rsvp-toggle');
    expect(toggle).toBeTruthy();
    const checked = toggle?.querySelector('mat-button-toggle.mat-button-toggle-checked');
    expect(checked?.textContent).toContain('RAIDS.RSVP_ONLY');
  });

  it('should support two-way gymId binding', () => {
    fixture.componentRef.setInput('gymId', 'gym-123');
    fixture.detectChanges();
    expect(fixture.componentInstance.gymId()).toBe('gym-123');
  });
});
