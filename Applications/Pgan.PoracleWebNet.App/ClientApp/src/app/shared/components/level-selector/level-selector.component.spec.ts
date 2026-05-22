import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';

import { LevelSelectorComponent } from './level-selector.component';
import { ANY_LEVEL_VALUE } from '../../../core/models/raid-level.models';

describe('LevelSelectorComponent', () => {
  let fixture: ComponentFixture<LevelSelectorComponent>;
  let component: LevelSelectorComponent;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService()],
      imports: [LevelSelectorComponent, NoopAnimationsModule],
    });
    fixture = TestBed.createComponent(LevelSelectorComponent);
    component = fixture.componentInstance;
    component.pickerType = 'raid';
  });

  // Type-narrowing helper for protected members exercised in tests.
  function withInternals(c: LevelSelectorComponent) {
    return c as unknown as {
      toggle(v: number): void;
      removeCustom(v: number, e: MouseEvent): void;
      openAddInput(): void;
      onAddKeydown(e: KeyboardEvent): void;
      commitAddInput(): void;
      addInputValue: { set(v: string): void; (): string };
      addInputError(): string | null;
      isAddClosed(): boolean;
      palette(): { value: number }[];
      primaryLevels(): { value: number }[];
      overflowLevels(): { value: number }[];
    };
  }

  it('renders without error', () => {
    component.value = [1, 7];
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('seeds the local palette from a custom value on incoming `value`', () => {
    component.value = [42, 1];
    expect(
      withInternals(component)
        .palette()
        .map(o => o.value),
    ).toEqual([42]);
  });

  it('does NOT persist the palette between component instances', () => {
    component.value = [42];
    // Fresh component instance simulates dialog close+reopen
    const fresh = TestBed.createComponent(LevelSelectorComponent).componentInstance;
    fresh.pickerType = 'raid';
    expect(withInternals(fresh).palette()).toEqual([]);
  });

  it('toggle adds/removes in raid (multi-select) mode', () => {
    const emitted: number[][] = [];
    component.value = [];
    component.valueChange.subscribe(v => emitted.push(v));

    withInternals(component).toggle(3);
    withInternals(component).toggle(5);
    expect(emitted).toEqual([[3], [3, 5]]);

    withInternals(component).toggle(3);
    expect(emitted[emitted.length - 1]).toEqual([5]);
  });

  it('boss picker is single-select', () => {
    component.pickerType = 'boss';
    component.value = [3];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    withInternals(component).toggle(5);
    expect(emitted[0]).toEqual([5]);
  });

  it('boss picker clears when the active chip is toggled again', () => {
    component.pickerType = 'boss';
    component.value = [3];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    withInternals(component).toggle(3);
    expect(emitted[0]).toEqual([]);
  });

  it('commitAddInput rejects 0 and negatives via inline error', () => {
    const c = withInternals(component);
    c.addInputValue.set('0');
    c.commitAddInput();
    expect(c.addInputError()).toBe('RAIDS.LEVEL.INVALID');

    c.addInputValue.set('-1');
    c.commitAddInput();
    expect(c.addInputError()).toBe('RAIDS.LEVEL.INVALID');
  });

  it('commitAddInput rejects non-integer input', () => {
    const c = withInternals(component);
    c.addInputValue.set('7.5');
    c.commitAddInput();
    expect(c.addInputError()).toBe('RAIDS.LEVEL.INVALID');
  });

  it('commitAddInput snaps 9000 to the ANY chip on raid picker', () => {
    component.value = [];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    const c = withInternals(component);
    c.addInputValue.set('9000');
    c.commitAddInput();

    expect(emitted[emitted.length - 1]).toEqual([ANY_LEVEL_VALUE]);
    expect(c.palette().map(o => o.value)).not.toContain(ANY_LEVEL_VALUE);
  });

  it('commitAddInput selects an existing known level instead of adding a duplicate', () => {
    component.value = [];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    const c = withInternals(component);
    c.addInputValue.set('5');
    c.commitAddInput();

    expect(emitted[emitted.length - 1]).toEqual([5]);
    expect(c.palette().map(o => o.value)).not.toContain(5);
  });

  it('commitAddInput adds a new custom into the local palette and selects it', () => {
    component.value = [];
    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    const c = withInternals(component);
    c.addInputValue.set('42');
    c.commitAddInput();

    expect(c.palette().map(o => o.value)).toContain(42);
    expect(emitted[emitted.length - 1]).toEqual([42]);
  });

  it('removeCustom removes from the local palette and the selection', () => {
    component.value = [42];
    const c = withInternals(component);
    expect(c.palette().map(o => o.value)).toContain(42);

    const emitted: number[][] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    c.removeCustom(42, new MouseEvent('click'));

    expect(c.palette().map(o => o.value)).not.toContain(42);
    expect(emitted[emitted.length - 1]).toEqual([]);
  });

  it('Escape cancels the add input', () => {
    const c = withInternals(component);
    c.openAddInput();
    c.addInputValue.set('99');
    c.onAddKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(c.isAddClosed()).toBe(true);
  });

  describe('pickerType-driven primary/overflow split', () => {
    it('raid picker shows star + mega in primary, special/shadow/etc. in overflow', () => {
      component.pickerType = 'raid';
      const c = withInternals(component);
      expect(c.primaryLevels().map(l => l.value)).toEqual([1, 2, 3, 4, 5, 6, 7]);
      expect(c.overflowLevels().map(l => l.value)).toEqual([8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19]);
    });

    it('egg picker shows star-only in primary and empty overflow', () => {
      component.pickerType = 'egg';
      const c = withInternals(component);
      expect(c.primaryLevels().map(l => l.value)).toEqual([1, 2, 3, 4, 5]);
      expect(c.overflowLevels()).toEqual([]);
    });

    it('boss picker mirrors raid for chip composition', () => {
      component.pickerType = 'boss';
      const c = withInternals(component);
      expect(c.primaryLevels().map(l => l.value)).toEqual([1, 2, 3, 4, 5, 6, 7]);
    });
  });
});
