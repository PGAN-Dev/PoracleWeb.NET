import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl } from '@angular/forms';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';

import { RsvpToggleComponent } from './rsvp-toggle.component';

describe('RsvpToggleComponent', () => {
  let fixture: ComponentFixture<RsvpToggleComponent>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService()],
      imports: [RsvpToggleComponent, NoopAnimationsModule],
    });
    fixture = TestBed.createComponent(RsvpToggleComponent);
  });

  it('should render three toggle options', () => {
    fixture.componentRef.setInput('control', new FormControl<number | null>(0));
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('mat-button-toggle').length).toBe(3);
    expect(el.querySelector('.rsvp-label')?.textContent).toContain('RAIDS.RSVP_LABEL');
    expect(el.querySelector('.rsvp-hint')?.textContent).toContain('RAIDS.RSVP_HINT');
  });

  it('should reflect the bound control value on the toggle group', () => {
    const control = new FormControl<number | null>(2);
    fixture.componentRef.setInput('control', control);
    fixture.detectChanges();

    const group = fixture.nativeElement.querySelector('mat-button-toggle-group');
    const checked = group?.querySelector('mat-button-toggle.mat-button-toggle-checked');
    expect(checked).toBeTruthy();
    expect(checked?.textContent).toContain('RAIDS.RSVP_ONLY');
  });

  it('should propagate user selection back to the bound control', () => {
    const control = new FormControl<number | null>(0);
    fixture.componentRef.setInput('control', control);
    fixture.detectChanges();

    const toggles = fixture.nativeElement.querySelectorAll('mat-button-toggle button');
    // Click the third toggle button (value = 2)
    (toggles[2] as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(control.value).toBe(2);
  });

  it('should not change value when the bound control is disabled', () => {
    const control = new FormControl<number | null>({ disabled: true, value: 1 });
    fixture.componentRef.setInput('control', control);
    fixture.detectChanges();

    const toggles = fixture.nativeElement.querySelectorAll('mat-button-toggle button');
    (toggles[2] as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(control.value).toBe(1);
  });

  it('should associate the toggle group with the visible label via aria-labelledby', () => {
    fixture.componentRef.setInput('control', new FormControl<number | null>(0));
    fixture.detectChanges();

    const label = fixture.nativeElement.querySelector('.rsvp-label');
    const group = fixture.nativeElement.querySelector('mat-button-toggle-group');
    expect(label?.id).toMatch(/^rsvp-toggle-label-\d+$/);
    expect(group?.getAttribute('aria-labelledby')).toBe(label?.id);
    expect(group?.getAttribute('aria-label')).toBeNull();
  });
});
