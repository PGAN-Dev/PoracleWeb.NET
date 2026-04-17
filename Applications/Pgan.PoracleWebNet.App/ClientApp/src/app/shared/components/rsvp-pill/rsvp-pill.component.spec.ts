import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { RsvpPillComponent } from './rsvp-pill.component';

describe('RsvpPillComponent', () => {
  let fixture: ComponentFixture<RsvpPillComponent>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService()],
      imports: [RsvpPillComponent],
    });
    fixture = TestBed.createComponent(RsvpPillComponent);
  });

  it('should render nothing when value is 0', () => {
    fixture.componentRef.setInput('value', 0);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.rsvp-stat')).toBeNull();
  });

  it('should render nothing when value is null', () => {
    fixture.componentRef.setInput('value', null);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.rsvp-stat')).toBeNull();
  });

  it('should render nothing when value is undefined', () => {
    fixture.componentRef.setInput('value', undefined);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.rsvp-stat')).toBeNull();
  });

  it('should render include label when value is 1', () => {
    fixture.componentRef.setInput('value', 1);
    fixture.detectChanges();
    const value = fixture.nativeElement.querySelector('.rsvp-stat .stat-value');
    expect(value?.textContent).toContain('RAIDS.RSVP_PILL_INCLUDE');
  });

  it('should render only label when value is 2', () => {
    fixture.componentRef.setInput('value', 2);
    fixture.detectChanges();
    const value = fixture.nativeElement.querySelector('.rsvp-stat .stat-value');
    expect(value?.textContent).toContain('RAIDS.RSVP_PILL_ONLY');
  });

  it('should render static RSVP label', () => {
    fixture.componentRef.setInput('value', 1);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.rsvp-stat .stat-label')?.textContent).toBe('RSVP');
  });
});
