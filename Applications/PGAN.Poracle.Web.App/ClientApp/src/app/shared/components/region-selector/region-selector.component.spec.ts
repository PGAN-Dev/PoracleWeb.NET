import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RegionOption, RegionSelectorComponent } from './region-selector.component';

describe('RegionSelectorComponent', () => {
  let component: RegionSelectorComponent;
  let fixture: ComponentFixture<RegionSelectorComponent>;

  const sampleRegions: RegionOption[] = [
    { id: 1, label: 'US - VA - Richmond', shortLabel: 'Richmond' },
    { id: 2, label: 'US - VA - Norfolk', shortLabel: 'Norfolk' },
    { id: 3, label: 'US - NC - Charlotte', shortLabel: 'Charlotte' },
    { id: 4, label: 'CA - ON - Toronto', shortLabel: 'Toronto' },
    { id: 5, label: 'Downtown', shortLabel: 'Downtown' },
  ];

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
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

  describe('filteredGroups', () => {
    beforeEach(() => {
      fixture.componentRef.setInput('regions', sampleRegions);
      fixture.detectChanges();
    });

    it('should group regions by country-state prefix', () => {
      const groups = component.filteredGroups();
      const labels = groups.map(g => g.label);
      expect(labels).toContain('US - VA');
      expect(labels).toContain('US - NC');
      expect(labels).toContain('CA - ON');
    });

    it('should put single-part labels in their own group', () => {
      const groups = component.filteredGroups();
      const downtownGroup = groups.find(g => g.label === 'Downtown');
      expect(downtownGroup).toBeTruthy();
      expect(downtownGroup!.regions).toHaveLength(1);
    });

    it('should sort groups alphabetically', () => {
      const groups = component.filteredGroups();
      const labels = groups.map(g => g.label);
      const sorted = [...labels].sort((a, b) => a.localeCompare(b));
      expect(labels).toEqual(sorted);
    });

    it('should filter regions by search text matching label', () => {
      component.searchText.set('richmond');
      const groups = component.filteredGroups();
      const allRegions = groups.flatMap(g => g.regions);
      expect(allRegions).toHaveLength(1);
      expect(allRegions[0].shortLabel).toBe('Richmond');
    });

    it('should filter regions by search text matching shortLabel', () => {
      component.searchText.set('toronto');
      const groups = component.filteredGroups();
      const allRegions = groups.flatMap(g => g.regions);
      expect(allRegions).toHaveLength(1);
      expect(allRegions[0].shortLabel).toBe('Toronto');
    });

    it('should return all regions when search is empty', () => {
      component.searchText.set('');
      const groups = component.filteredGroups();
      const allRegions = groups.flatMap(g => g.regions);
      expect(allRegions).toHaveLength(5);
    });

    it('should return no groups when search matches nothing', () => {
      component.searchText.set('zzzzzzz');
      const groups = component.filteredGroups();
      expect(groups).toHaveLength(0);
    });

    it('should be case-insensitive when filtering', () => {
      component.searchText.set('CHARLOTTE');
      const groups = component.filteredGroups();
      const allRegions = groups.flatMap(g => g.regions);
      expect(allRegions).toHaveLength(1);
      expect(allRegions[0].shortLabel).toBe('Charlotte');
    });

    it('should group VA regions together', () => {
      const groups = component.filteredGroups();
      const vaGroup = groups.find(g => g.label === 'US - VA');
      expect(vaGroup).toBeTruthy();
      expect(vaGroup!.regions).toHaveLength(2);
    });
  });

  describe('selection', () => {
    beforeEach(() => {
      fixture.componentRef.setInput('regions', sampleRegions);
      fixture.detectChanges();
    });

    it('should set selectedOption and emit on onOptionSelected', () => {
      const emitSpy = jest.spyOn(component.regionSelected, 'emit');
      const option: RegionOption = { id: 1, label: 'US - VA - Richmond', shortLabel: 'Richmond' };

      component.onOptionSelected(option);

      expect(component.selectedOption()).toEqual(option);
      expect(component.searchText()).toBe('');
      expect(emitSpy).toHaveBeenCalledWith(option);
    });

    it('should clear selection and emit empty label on clearSelection', () => {
      const emitSpy = jest.spyOn(component.regionSelected, 'emit');
      component.onOptionSelected({ id: 1, label: 'Test', shortLabel: 'Test' });

      component.clearSelection();

      expect(component.selectedOption()).toBeNull();
      expect(component.searchText()).toBe('');
      expect(emitSpy).toHaveBeenCalledWith({ label: '' });
    });
  });

  describe('displayFn', () => {
    it('should return shortLabel when available', () => {
      expect(component.displayFn({ label: 'Full Label', shortLabel: 'Short' })).toBe('Short');
    });

    it('should return label when shortLabel is undefined', () => {
      expect(component.displayFn({ label: 'Full Label' })).toBe('Full Label');
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
