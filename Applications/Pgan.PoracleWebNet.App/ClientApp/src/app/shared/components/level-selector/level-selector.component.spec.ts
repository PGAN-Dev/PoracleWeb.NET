import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';

import { LevelSelectorComponent } from './level-selector.component';
import { ANY_LEVEL_VALUE } from '../../../core/models/raid-level.models';
import { CustomLevelStore } from '../../../core/services/custom-level-store.service';

const STORAGE_PREFIX = 'poracle.custom-levels';

describe('LevelSelectorComponent', () => {
  let fixture: ComponentFixture<LevelSelectorComponent>;
  let component: LevelSelectorComponent;
  let store: CustomLevelStore;

  beforeEach(() => {
    for (const k of Object.keys(localStorage)) {
      if (k.startsWith(STORAGE_PREFIX)) localStorage.removeItem(k);
    }
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService()],
      imports: [LevelSelectorComponent, NoopAnimationsModule],
    });
    fixture = TestBed.createComponent(LevelSelectorComponent);
    component = fixture.componentInstance;
    component.pickerType = 'raid';
    store = TestBed.inject(CustomLevelStore);
  });

  it('renders without error', () => {
    component.value = [1, 7];
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('seeds custom levels from incoming value into the keyed palette', () => {
    component.pickerType = 'raid';
    component.value = [42, 1];
    expect(store.values('raid')).toEqual([42]);
  });

  it('toggle adds/removes in raid (multi-select) mode', () => {
    const emitted: number[][] = [];
    component.value = [];
    component.valueChange.subscribe(v => emitted.push(v));

    (component as unknown as { toggle: (v: number) => void }).toggle(3);
    (component as unknown as { toggle: (v: number) => void }).toggle(5);
    expect(emitted).toEqual([[3], [3, 5]]);

    (component as unknown as { toggle: (v: number) => void }).toggle(3);
    expect(emitted[emitted.length - 1]).toEqual([5]);
  });

  it('boss picker is single-select', () => {
    component.pickerType = 'boss';
    component.value = [3];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    (component as unknown as { toggle: (v: number) => void }).toggle(5);
    expect(emitted[0]).toEqual([5]);
  });

  it('boss picker clears when the active chip is toggled again', () => {
    component.pickerType = 'boss';
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

  it('commitAddInput snaps 9000 to ANY chip on raid picker', () => {
    component.value = [];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    const c = component as unknown as { addInputValue: { set: (v: string) => void }; commitAddInput: () => void };
    c.addInputValue.set('9000');
    c.commitAddInput();

    expect(emitted[emitted.length - 1]).toEqual([ANY_LEVEL_VALUE]);
    expect(store.values('raid')).not.toContain(ANY_LEVEL_VALUE);
  });

  it('commitAddInput selects an existing known level instead of adding a duplicate', () => {
    component.value = [];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    const c = component as unknown as { addInputValue: { set: (v: string) => void }; commitAddInput: () => void };
    c.addInputValue.set('5');
    c.commitAddInput();

    expect(emitted[emitted.length - 1]).toEqual([5]);
    expect(store.values('raid')).not.toContain(5);
  });

  it('commitAddInput adds a new custom into the keyed palette and selects it', () => {
    component.value = [];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    const c = component as unknown as { addInputValue: { set: (v: string) => void }; commitAddInput: () => void };
    c.addInputValue.set('42');
    c.commitAddInput();

    expect(store.values('raid')).toContain(42);
    expect(emitted[emitted.length - 1]).toEqual([42]);
  });

  it('palette is scoped per pickerType — raid does not leak to egg/boss', () => {
    component.pickerType = 'raid';
    component.value = [];
    const c = component as unknown as { addInputValue: { set: (v: string) => void }; commitAddInput: () => void };
    c.addInputValue.set('42');
    c.commitAddInput();

    expect(store.values('raid')).toContain(42);
    expect(store.values('egg')).not.toContain(42);
    expect(store.values('boss')).not.toContain(42);
  });

  it('removeCustom evicts the value from the keyed palette and the selection', () => {
    component.value = [42];
    expect(store.values('raid')).toContain(42);

    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    const c = component as unknown as { removeCustom: (v: number, e: MouseEvent) => void };
    c.removeCustom(42, new MouseEvent('click'));

    expect(store.values('raid')).not.toContain(42);
    expect(emitted[emitted.length - 1]).toEqual([]);
  });

  it('Escape cancels the add input', () => {
    const c = component as unknown as {
      openAddInput: () => void;
      addInputValue: { set: (v: string) => void; (): string };
      isAddClosed: () => boolean;
      onAddKeydown: (e: KeyboardEvent) => void;
    };
    c.openAddInput();
    c.addInputValue.set('99');
    c.onAddKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(c.isAddClosed()).toBe(true);
  });

  describe('pickerType-driven primary/overflow split', () => {
    it('raid picker shows star + mega in primary, special/shadow/etc. in overflow', () => {
      component.pickerType = 'raid';
      const primary = (component as unknown as { primaryLevels: () => { value: number }[] }).primaryLevels();
      const overflow = (component as unknown as { overflowLevels: () => { value: number }[] }).overflowLevels();
      expect(primary.map(l => l.value)).toEqual([1, 2, 3, 4, 5, 6, 7]);
      expect(overflow.map(l => l.value)).toEqual([8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19]);
    });

    it('egg picker shows star-only in primary and empty overflow', () => {
      component.pickerType = 'egg';
      const primary = (component as unknown as { primaryLevels: () => { value: number }[] }).primaryLevels();
      const overflow = (component as unknown as { overflowLevels: () => { value: number }[] }).overflowLevels();
      expect(primary.map(l => l.value)).toEqual([1, 2, 3, 4, 5]);
      expect(overflow).toEqual([]);
    });

    it('boss picker mirrors raid for chip composition', () => {
      component.pickerType = 'boss';
      const primary = (component as unknown as { primaryLevels: () => { value: number }[] }).primaryLevels();
      expect(primary.map(l => l.value)).toEqual([1, 2, 3, 4, 5, 6, 7]);
    });
  });
});
