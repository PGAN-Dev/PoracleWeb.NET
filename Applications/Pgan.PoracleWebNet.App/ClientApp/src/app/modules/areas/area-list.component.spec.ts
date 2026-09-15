import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AreaListComponent } from './area-list.component';
import { AreaDefinition } from '../../core/models';
import { AreaService } from '../../core/services/area.service';

/**
 * Subscriptions are stored lowercased because Poracle matches area names case-sensitively, so the
 * lowercase form is what matches. Rendering that stored string verbatim is why the chips and the quiet
 * dialog read "mechanicsville" when Poracle publishes "Mechanicsville". See #870.
 */
describe('AreaListComponent area display names', () => {
  let component: AreaListComponent;

  const available = (...names: string[]): AreaDefinition[] => names.map(name => ({ name, group: 'Richmond', userSelectable: true }));

  beforeEach(async () => {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        {
          provide: AreaService,
          useValue: {
            getAvailable: () => of([]),
            getGeofencePolygons: () => of([]),
            getMapUrl: () => of(null),
            getSelected: () => of([]),
          },
        },
      ],
      imports: [AreaListComponent, NoopAnimationsModule],
    }).compileComponents();

    component = TestBed.createComponent(AreaListComponent).componentInstance;
  });

  it('shows the name Poracle publishes, not the lowercased one it stores', () => {
    component.availableAreas.set(available('Mechanicsville', 'Bon Air - Robious'));

    expect(component.areaDisplayName('mechanicsville')).toBe('Mechanicsville');
    expect(component.areaDisplayName('bon air - robious')).toBe('Bon Air - Robious');
  });

  /**
   * An acronym is the case that rules out title-casing as a shortcut: it would render ACLT as "Aclt".
   */
  it('leaves an acronym alone rather than title-casing it', () => {
    component.availableAreas.set(available('ACLT'));

    expect(component.areaDisplayName('aclt')).toBe('ACLT');
  });

  /**
   * A subscription to an area that has since been deleted has nothing to resolve against, and has to
   * keep rendering — it is the only way the user can see it and remove it.
   */
  it('falls back to the stored name when no area matches', () => {
    component.availableAreas.set(available('Mechanicsville'));

    expect(component.areaDisplayName('an area since deleted')).toBe('an area since deleted');
  });

  /**
   * User-drawn geofences are lowercase by design — `UserGeofenceService.CreateAsync` lowercases the
   * koji name on purpose — so resolving one to itself is correct, not a miss.
   */
  it('leaves a genuinely lowercase name as it is', () => {
    component.availableAreas.set(available('my back garden'));

    expect(component.areaDisplayName('my back garden')).toBe('my back garden');
  });

  it('resolves nothing before the area list has loaded', () => {
    expect(component.areaDisplayName('mechanicsville')).toBe('mechanicsville');
  });
});
