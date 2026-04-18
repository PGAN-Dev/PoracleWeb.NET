import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl } from '@angular/forms';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';

import { RaidDeliverySectionComponent } from './raid-delivery-section.component';

describe('RaidDeliverySectionComponent', () => {
  let fixture: ComponentFixture<RaidDeliverySectionComponent>;
  let component: RaidDeliverySectionComponent;

  let distanceMode: FormControl<'areas' | 'distance' | null>;
  let distanceKm: FormControl<number | null>;
  let ping: FormControl<string | null>;
  let template: FormControl<string | null>;
  let clean: FormControl<boolean | null>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService()],
      imports: [RaidDeliverySectionComponent, NoopAnimationsModule],
    });
    fixture = TestBed.createComponent(RaidDeliverySectionComponent);
    component = fixture.componentInstance;

    distanceMode = new FormControl<'areas' | 'distance' | null>('areas');
    distanceKm = new FormControl<number | null>(0);
    ping = new FormControl<string | null>('');
    template = new FormControl<string | null>('');
    clean = new FormControl<boolean | null>(false);

    fixture.componentRef.setInput('distanceMode', distanceMode);
    fixture.componentRef.setInput('distanceKm', distanceKm);
    fixture.componentRef.setInput('ping', ping);
    fixture.componentRef.setInput('template', template);
    fixture.componentRef.setInput('clean', clean);
  });

  it('should render the location-mode radios and template selector', () => {
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('mat-radio-button').length).toBe(2);
    expect(el.querySelector('app-template-selector')).toBeTruthy();
    expect(el.querySelector('mat-slide-toggle')).toBeTruthy();
  });

  it('should hide the distance input when mode is areas', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input[type="number"]')).toBeNull();
  });

  it('should show the distance input when mode is distance', () => {
    distanceMode.setValue('distance');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input[type="number"]')).toBeTruthy();
  });

  it('should hide the ping input unless showPing is set', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input[placeholder]')).toBeNull();

    fixture.componentRef.setInput('showPing', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('mat-form-field input[matInput]:not([type="number"])')).toBeTruthy();
  });

  it('onDistanceModeChange should zero km when switching to areas', () => {
    distanceMode.setValue('distance');
    distanceKm.setValue(5);
    distanceMode.setValue('areas');
    component.onDistanceModeChange();
    expect(distanceKm.value).toBe(0);
  });

  it('onDistanceModeChange should default km to 1 when switching to distance from zero', () => {
    distanceMode.setValue('areas');
    distanceKm.setValue(0);
    distanceMode.setValue('distance');
    component.onDistanceModeChange();
    expect(distanceKm.value).toBe(1);
  });

  it('onDistanceModeChange should preserve a non-zero km when switching to distance', () => {
    distanceMode.setValue('distance');
    distanceKm.setValue(3);
    component.onDistanceModeChange();
    expect(distanceKm.value).toBe(3);
  });

  it('should wrap the conditional distance input in a polite live region', () => {
    fixture.detectChanges();
    const liveRegion: HTMLElement | null = fixture.nativeElement.querySelector('[aria-live="polite"]');
    expect(liveRegion).toBeTruthy();
    expect(liveRegion?.getAttribute('aria-atomic')).toBe('true');

    distanceMode.setValue('distance');
    fixture.detectChanges();
    expect(liveRegion?.querySelector('input[type="number"]')).toBeTruthy();
  });
});
