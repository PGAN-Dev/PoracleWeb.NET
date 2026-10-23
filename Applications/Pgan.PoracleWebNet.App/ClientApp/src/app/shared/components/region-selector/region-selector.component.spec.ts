import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { groupRegions, RegionGroup, RegionOption, RegionSelectorComponent } from './region-selector.component';

describe('RegionSelectorComponent', () => {
  let component: RegionSelectorComponent;
  let fixture: ComponentFixture<RegionSelectorComponent>;

  const sampleRegions: RegionOption[] = [
    { id: 1, label: 'US - VA - Richmond' },
    { id: 2, label: 'US - VA - Norfolk' },
    { id: 3, label: 'US - NC - Charlotte' },
    { id: 4, label: 'CA - ON - Toronto' },
    { id: 5, label: 'Downtown' },
  ];

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService()],
      imports: [RegionSelectorComponent],
    });

    fixture = TestBed.createComponent(RegionSelectorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should default to empty regions', () => {
    expect(component.regions()).toEqual([]);
  });

  it('should default to empty search text', () => {
    expect(component.searchText()).toBe('');
  });

  it('should have no selected option by default', () => {
    expect(component.selectedOption()).toBeNull();
  });

  describe('groupRegions (#1029)', () => {
    const opt = (label: string, extra: Partial<RegionOption> = {}): RegionOption => ({ label, ...extra });
    const headings = (groups: RegionGroup[]) => groups.map(g => g.heading);
    const texts = (group: RegionGroup) => group.items.map(i => i.text);

    it('lists plain names flat, with no heading repeating the name', () => {
      const groups = groupRegions([opt('Northside'), opt('Downtown'), opt('Eastside')]);
      expect(headings(groups)).toEqual([null, null, null]);
      expect(groups.map(texts)).toEqual([['Downtown'], ['Eastside'], ['Northside']]);
    });

    it('puts names sharing a prefix under one heading and shows only the last part', () => {
      const groups = groupRegions([opt('US - VA - Richmond'), opt('US - VA - Norfolk')]);
      expect(headings(groups)).toEqual(['US - VA']);
      expect(texts(groups[0])).toEqual(['Norfolk', 'Richmond']);
      expect(groups[0].items.map(i => i.option.label)).toEqual(['US - VA - Norfolk', 'US - VA - Richmond']);
    });

    it('draws no heading over a single entry: the entry keeps its full name', () => {
      const groups = groupRegions([opt('US - HI - Oahu'), opt('NZL - Auckland')]);
      expect(headings(groups)).toEqual([null, null]);
      expect(groups.map(texts)).toEqual([['NZL - Auckland'], ['US - HI - Oahu']]);
    });

    it('groups two-part names by their first part', () => {
      const groups = groupRegions([opt('AUS - Sydney'), opt('AUS - Melbourne')]);
      expect(headings(groups)).toEqual(['AUS']);
      expect(texts(groups[0])).toEqual(['Melbourne', 'Sydney']);
    });

    it('nests four-part names under everything before the last part', () => {
      const groups = groupRegions([opt('US - VA - Richmond - North'), opt('US - VA - Richmond - South')]);
      expect(headings(groups)).toEqual(['US - VA - Richmond']);
      expect(texts(groups[0])).toEqual(['North', 'South']);
    });

    it('sorts headed groups and flat entries together, ungrouped last', () => {
      const groups = groupRegions([
        opt('Ungrouped', { ungrouped: true }),
        opt('US - MD - Catonsville'),
        opt('US - HI - Oahu'),
        opt('Richmond North, VA'),
        opt('US - GA - Atlanta'),
        opt('US - GA - Savannah'),
        opt('US - MD - Baltimore Northeast'),
      ]);
      expect(groups.map(g => g.heading ?? g.items[0].text)).toEqual([
        'Richmond North, VA',
        'US - GA',
        'US - HI - Oahu',
        'US - MD',
        'Ungrouped',
      ]);
    });

    it('never folds the ungrouped bucket into a heading', () => {
      const groups = groupRegions([opt('A - Ungrouped', { ungrouped: true }), opt('A - Other')]);
      expect(headings(groups)).toEqual([null, null]);
    });

    it('keeps a name ending in the separator whole', () => {
      const groups = groupRegions([opt('US - '), opt('US - VA')]);
      expect(groups.flatMap(texts)).toContain('US - ');
    });

    it('gives every group a distinct key', () => {
      const groups = groupRegions([opt('US - VA - Richmond'), opt('US - VA - Norfolk'), opt('US - VA')]);
      expect(new Set(groups.map(g => g.key)).size).toBe(groups.length);
    });
  });

  describe('filteredGroups', () => {
    beforeEach(() => {
      fixture.componentRef.setInput('regions', sampleRegions);
      fixture.detectChanges();
    });

    it('groups shared prefixes and leaves single entries flat', () => {
      expect(component.filteredGroups().map(g => g.heading ?? g.items[0].text)).toEqual([
        'CA - ON - Toronto',
        'Downtown',
        'US - NC - Charlotte',
        'US - VA',
      ]);
    });

    it('matches the full name, so a state code finds its cities', () => {
      component.searchText.set('va');
      const groups = component.filteredGroups();
      expect(groups.map(g => g.heading)).toEqual(['US - VA']);
      expect(groups[0].items.map(i => i.text)).toEqual(['Norfolk', 'Richmond']);
    });

    it('shows a lone search match with its full name', () => {
      component.searchText.set('RICHMOND');
      const groups = component.filteredGroups();
      expect(groups).toHaveLength(1);
      expect(groups[0].heading).toBeNull();
      expect(groups[0].items[0].text).toBe('US - VA - Richmond');
    });

    it('returns every region when search is empty', () => {
      expect(component.filteredGroups().flatMap(g => g.items)).toHaveLength(5);
    });

    it('returns no groups when search matches nothing', () => {
      component.searchText.set('zzzzzzz');
      expect(component.filteredGroups()).toHaveLength(0);
    });
  });

  describe('rendering', () => {
    it('draws a heading only over a shared prefix', () => {
      fixture.componentRef.setInput('regions', sampleRegions);
      fixture.detectChanges();
      const input: HTMLInputElement = fixture.nativeElement.querySelector('input');
      input.dispatchEvent(new Event('focusin'));
      fixture.detectChanges();

      expect(document.querySelectorAll('mat-optgroup')).toHaveLength(1);
      const options = [...document.querySelectorAll('mat-option')].map(o => o.textContent?.trim());
      expect(options).toEqual(expect.arrayContaining(['CA - ON - Toronto', 'Downtown', 'Norfolk', 'Richmond', 'US - NC - Charlotte']));
    });
  });

  describe('selection', () => {
    beforeEach(() => {
      fixture.componentRef.setInput('regions', sampleRegions);
      fixture.detectChanges();
    });

    it('should set selectedOption and emit on onOptionSelected', () => {
      const emitSpy = jest.spyOn(component.regionSelected, 'emit');
      const option: RegionOption = { id: 1, label: 'US - VA - Richmond' };

      component.onOptionSelected(option);

      expect(component.selectedOption()).toEqual(option);
      expect(component.searchText()).toBe('');
      expect(emitSpy).toHaveBeenCalledWith(option);
    });

    it('should clear selection (not render a blank chip) when the "All Regions" sentinel is picked (#314)', () => {
      const emitSpy = jest.spyOn(component.regionSelected, 'emit');
      component.onOptionSelected({ id: 1, label: 'Test' });
      emitSpy.mockClear();

      component.onOptionSelected({ label: '' });

      expect(component.selectedOption()).toBeNull();
      expect(component.searchText()).toBe('');
      expect(emitSpy).toHaveBeenCalledWith({ label: '' });
    });

    it('should clear selection and emit empty label on clearSelection', () => {
      const emitSpy = jest.spyOn(component.regionSelected, 'emit');
      component.onOptionSelected({ id: 1, label: 'Test' });

      component.clearSelection();

      expect(component.selectedOption()).toBeNull();
      expect(component.searchText()).toBe('');
      expect(emitSpy).toHaveBeenCalledWith({ label: '' });
    });
  });

  describe('displayFn', () => {
    it('returns the full label', () => {
      expect(component.displayFn({ label: 'US - VA - Richmond' })).toBe('US - VA - Richmond');
    });

    it('should return empty string for null/undefined', () => {
      expect(component.displayFn(null as unknown as RegionOption)).toBe('');
      expect(component.displayFn(undefined as unknown as RegionOption)).toBe('');
    });
  });

  describe('input handling', () => {
    it('should update search text on onInputChange', () => {
      component.onInputChange('test search');
      expect(component.searchText()).toBe('test search');
    });

    it('should clear search text on onClearInput', () => {
      component.searchText.set('some text');
      component.onClearInput();
      expect(component.searchText()).toBe('');
    });
  });

  describe('inputs', () => {
    it('should accept custom label', () => {
      fixture.componentRef.setInput('label', 'Choose Area');
      fixture.detectChanges();
      expect(component.label()).toBe('Choose Area');
    });

    it('should accept custom placeholder', () => {
      fixture.componentRef.setInput('placeholder', 'Type to filter...');
      fixture.detectChanges();
      expect(component.placeholder()).toBe('Type to filter...');
    });

    it('should have default label', () => {
      expect(component.label()).toBe('Select Region');
    });

    it('should have default placeholder', () => {
      expect(component.placeholder()).toBe('Search regions...');
    });
  });
});
