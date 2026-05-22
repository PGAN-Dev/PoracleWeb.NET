import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';

import { LevelSelectorComponent } from './level-selector.component';
import { ANY_LEVEL_VALUE } from '../../../core/models/raid-level.models';
import { CustomLevelStore } from '../../../core/services/custom-level-store.service';

const STORAGE_KEY = 'poracle.custom-raid-levels';

describe('LevelSelectorComponent', () => {
  let fixture: ComponentFixture<LevelSelectorComponent>;
  let component: LevelSelectorComponent;
  let store: CustomLevelStore;

  beforeEach(() => {
    localStorage.removeItem(STORAGE_KEY);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [LevelSelectorComponent, NoopAnimationsModule],
      providers: [provideHttpClient(), provideTranslateService()],
    });
    fixture = TestBed.createComponent(LevelSelectorComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(CustomLevelStore);
  });

  it('renders without error in multi-select mode', () => {
    component.multiple = true;
    component.value = [1, 7];
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('seeds custom levels from incoming value', () => {
    component.value = [42, 1];
    expect(store.values()).toEqual([42]);
  });

  it('toggle adds/removes in multi-select', () => {
    component.multiple = true;
    component.value = [];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    (component as unknown as { toggle: (v: number) => void }).toggle(3);
    (component as unknown as { toggle: (v: number) => void }).toggle(5);
    expect(emitted).toEqual([[3], [3, 5]]);

    (component as unknown as { toggle: (v: number) => void }).toggle(3);
    expect(emitted[emitted.length - 1]).toEqual([5]);
  });

  it('toggle replaces in single-select', () => {
    component.multiple = false;
    component.value = [3];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    (component as unknown as { toggle: (v: number) => void }).toggle(5);
    expect(emitted[0]).toEqual([5]);
  });

  it('single-select clears when the active chip is toggled again', () => {
    component.multiple = false;
    component.value = [3];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    (component as unknown as { toggle: (v: number) => void }).toggle(3);
    expect(emitted[0]).toEqual([]);
  });

  it('commitAddInput rejects 0 and negatives via inline error', () => {
    const c = component as unknown as {
      addInputValue: { set: (v: string) => void };
      addInputError: () => string | null;
      commitAddInput: () => void;
    };
    c.addInputValue.set('0');
    c.commitAddInput();
    expect(c.addInputError()).toBe('RAIDS.LEVEL.INVALID');

    c.addInputValue.set('-1');
    c.commitAddInput();
    expect(c.addInputError()).toBe('RAIDS.LEVEL.INVALID');
  });

  it('commitAddInput rejects non-integer input', () => {
    const c = component as unknown as {
      addInputValue: { set: (v: string) => void };
      addInputError: () => string | null;
      commitAddInput: () => void;
    };
    c.addInputValue.set('7.5');
    c.commitAddInput();
    expect(c.addInputError()).toBe('RAIDS.LEVEL.INVALID');
  });

  it('commitAddInput snaps 9000 to ANY chip when showAny is true', () => {
    component.showAny = true;
    component.value = [];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    const c = component as unknown as { addInputValue: { set: (v: string) => void }; commitAddInput: () => void };
    c.addInputValue.set('9000');
    c.commitAddInput();

    expect(emitted[emitted.length - 1]).toEqual([ANY_LEVEL_VALUE]);
    // Should NOT have been added to the custom palette
    expect(store.values()).not.toContain(ANY_LEVEL_VALUE);
  });

  it('commitAddInput selects an existing built-in instead of adding a duplicate', () => {
    component.multiple = true;
    component.value = [];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    const c = component as unknown as { addInputValue: { set: (v: string) => void }; commitAddInput: () => void };
    c.addInputValue.set('5'); // already in STANDARD
    c.commitAddInput();

    expect(emitted[emitted.length - 1]).toEqual([5]);
    expect(store.values()).not.toContain(5);
  });

  it('commitAddInput adds a new custom and selects it', () => {
    component.multiple = true;
    component.value = [];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    const c = component as unknown as { addInputValue: { set: (v: string) => void }; commitAddInput: () => void };
    c.addInputValue.set('42');
    c.commitAddInput();

    expect(store.values()).toContain(42);
    expect(emitted[emitted.length - 1]).toEqual([42]);
  });

  it('removeCustom evicts the value from the palette and the selection', () => {
    component.multiple = true;
    component.value = [42];
    expect(store.values()).toContain(42);

    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    const c = component as unknown as { removeCustom: (v: number, e: MouseEvent) => void };
    c.removeCustom(42, new MouseEvent('click'));

    expect(store.values()).not.toContain(42);
    expect(emitted[emitted.length - 1]).toEqual([]);
  });

  it('Escape cancels the add input and clears state', () => {
    const c = component as unknown as {
      openAddInput: () => void;
      addInputValue: { set: (v: string) => void; (): string };
      addInputOpen: () => boolean;
      onAddKeydown: (e: KeyboardEvent) => void;
    };
    c.openAddInput();
    c.addInputValue.set('99');
    c.onAddKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(c.addInputOpen()).toBe(false);
  });
});
