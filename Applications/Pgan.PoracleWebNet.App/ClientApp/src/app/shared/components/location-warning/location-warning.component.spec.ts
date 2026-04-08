import { ComponentFixture, TestBed } from '@angular/core/testing';

import { LocationWarningComponent } from './location-warning.component';

describe('LocationWarningComponent', () => {
  let component: LocationWarningComponent;
  let fixture: ComponentFixture<LocationWarningComponent>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [LocationWarningComponent],
    });
    fixture = TestBed.createComponent(LocationWarningComponent);
    component = fixture.componentInstance;
  });

  it('should create successfully', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should show warning when hasActiveHours is true and coords are 0,0', () => {
    fixture.componentRef.setInput('hasActiveHours', true);
    fixture.componentRef.setInput('latitude', 0);
    fixture.componentRef.setInput('longitude', 0);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.location-warning')).toBeTruthy();
  });

  it('should hide warning when coords are set', () => {
    fixture.componentRef.setInput('hasActiveHours', true);
    fixture.componentRef.setInput('latitude', 40.7128);
    fixture.componentRef.setInput('longitude', -74.006);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.location-warning')).toBeNull();
  });

  it('should hide warning when no active hours', () => {
    fixture.componentRef.setInput('hasActiveHours', false);
    fixture.componentRef.setInput('latitude', 0);
    fixture.componentRef.setInput('longitude', 0);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.location-warning')).toBeNull();
  });

  it('should hide warning when only latitude is set', () => {
    fixture.componentRef.setInput('hasActiveHours', true);
    fixture.componentRef.setInput('latitude', 40.7128);
    fixture.componentRef.setInput('longitude', 0);
    fixture.detectChanges();

    // Only both at 0 triggers warning
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.location-warning')).toBeNull();
  });
});
