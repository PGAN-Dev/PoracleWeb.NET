import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { AddRefusalComponent } from './add-refusal.component';
import { AddRefusal, ExistingRule } from '../../utils/add-outcome';

describe('AddRefusalComponent', () => {
  let fixture: ComponentFixture<AddRefusalComponent>;

  function render(refusal: AddRefusal | null): HTMLElement {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideTranslateService()], imports: [AddRefusalComponent] });
    fixture = TestBed.createComponent(AddRefusalComponent);
    fixture.componentRef.setInput('refusal', refusal);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders nothing without a refusal', () => {
    expect(render(null).querySelector('[role="alert"]')).toBeNull();
  });

  it('shows the reason alone when no stored rule was named', () => {
    const el = render({ existing: null, message: 'You already have an identical max battle alarm.' });

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('You already have an identical max battle alarm.');
    expect(el.querySelector('.add-refusal__existing')).toBeNull();
    expect(el.querySelector('button')).toBeNull();
  });

  it('names the stored rule and offers to edit it', () => {
    const existing: ExistingRule = { uid: 77, summary: 'Snorlax · Within 2 km of my pin', trackingType: 'pokemon' };
    const el = render({ existing, message: 'Another alarm of this type already uses those settings.' });
    const emitted: ExistingRule[] = [];
    fixture.componentInstance.editExisting.subscribe(rule => emitted.push(rule));

    expect(el.querySelector('.add-refusal__existing')).not.toBeNull();
    (el.querySelector('button') as HTMLButtonElement).click();

    expect(emitted).toEqual([existing]);
  });
});
