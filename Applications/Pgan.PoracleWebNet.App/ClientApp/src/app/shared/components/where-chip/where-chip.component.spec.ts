import { ComponentRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { WhereChipComponent } from './where-chip.component';

describe('WhereChipComponent', () => {
  let fixture: ComponentFixture<WhereChipComponent>;
  let ref: ComponentRef<WhereChipComponent>;

  function create(inputs: Record<string, unknown>): WhereChipComponent {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService()],
      imports: [WhereChipComponent],
    });
    fixture = TestBed.createComponent(WhereChipComponent);
    ref = fixture.componentRef;
    Object.entries(inputs).forEach(([key, value]) => ref.setInput(key, value));
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('names the place and radius for a place-scoped alarm', () => {
    const chip = create({ overrideLocationLabel: 'work', distance: 2000 });

    expect(chip.label()).toBe('WHERE.NEAR_PLACE');
    expect(chip.icon()).toBe('place');
    expect(chip.isInherited()).toBe(false);
  });

  it('says the pin, not the areas, for a plain radius', () => {
    // The pre-existing "within N km of me" alarm. Reading it as inherited areas would put the opposite
    // words on the card, which is the whole reason the profile mode carries a radius.
    const chip = create({ distance: 500 });

    expect(chip.label()).toBe('WHERE.NEAR_PIN');
    expect(chip.isInherited()).toBe(true);
  });

  it('recedes for the inherited scope, since nearly every card has it', () => {
    const chip = create({ distance: 0, profileAreas: ['terrigal'] });

    expect(chip.label()).toBe('WHERE.PROFILE_AREAS');
    expect(chip.icon()).toBe('public');
    expect(chip.isInherited()).toBe(true);
  });

  it('does not claim areas the user has not got', () => {
    const chip = create({ distance: 0, profileAreas: [] });

    expect(chip.label()).toBe('WHERE.PROFILE_ANYWHERE');
  });

  it('shows the map icon when the alarm is confined to areas', () => {
    const chip = create({ overrideAreas: ['terrigal'], distance: 0 });

    expect(chip.label()).toBe('WHERE.ONLY_IN');
    expect(chip.icon()).toBe('map');
  });
});
