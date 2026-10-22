import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { Subject } from 'rxjs';

import { GeoJsonImportDialogComponent, GeoJsonImportDialogData } from './geojson-import-dialog.component';
import { GeoJsonImportResult, UserGeofence } from '../../../core/models';
import { UserGeofenceService } from '../../../core/services/user-geofence.service';

/**
 * A file with a polygon and a line previewed "1 ready to import" and never mentioned the line, and a file
 * that wasn't GeoJSON at all moved on to "Select & Name Geofences" with "0 B" and the error underneath.
 */
describe('GeoJsonImportDialogComponent reading a file', () => {
  let fixture: ComponentFixture<GeoJsonImportDialogComponent>;
  const square = {
    geometry: {
      coordinates: [
        [
          [-81.37, 28.54],
          [-81.36, 28.54],
          [-81.36, 28.55],
          [-81.37, 28.55],
          [-81.37, 28.54],
        ],
      ],
      type: 'Polygon',
    },
    properties: { name: 'Lake Eola' },
    type: 'Feature',
  };
  const line = {
    geometry: {
      coordinates: [
        [-81.37, 28.54],
        [-81.36, 28.55],
      ],
      type: 'LineString',
    },
    properties: { name: 'Path' },
    type: 'Feature',
  };
  const point = { geometry: { coordinates: [-81.37, 28.54], type: 'Point' }, properties: {}, type: 'Feature' };

  let service: { importGeoJson: jest.Mock };

  const setup = () => {
    service = { importGeoJson: jest.fn() };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideTranslateService(),
        {
          provide: MAT_DIALOG_DATA,
          useValue: { currentGeofenceCount: 0, existingNames: [], maxGeofences: 10, regions: [] } as GeoJsonImportDialogData,
        },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: UserGeofenceService, useValue: service },
      ],
      imports: [GeoJsonImportDialogComponent],
    });
    fixture = TestBed.createComponent(GeoJsonImportDialogComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  };

  /** FileReader is asynchronous; resolve once it has called back. */
  const load = async (component: GeoJsonImportDialogComponent, content: string) => {
    const input = { files: [new File([content], 'fences.geojson')], value: 'x' } as unknown as HTMLInputElement;
    component.onFileSelected({ target: input } as unknown as Event);
    for (let i = 0; i < 50 && component.step() === 'upload' && !component.parseError(); i++) {
      await new Promise(resolve => setTimeout(resolve, 5));
    }
  };

  const collection = (...features: unknown[]) => JSON.stringify({ features, type: 'FeatureCollection' });

  it('says how many features it skipped because they are not polygons', async () => {
    const component = setup();

    await load(component, collection(square, line, point));

    expect(component.step()).toBe('preview');
    expect(component.previewFeatures()).toHaveLength(1);
    expect(component.skippedCount()).toBe(2);
  });

  it('skips nothing in a file of polygons', async () => {
    const component = setup();

    await load(component, collection(square));

    expect(component.step()).toBe('preview');
    expect(component.skippedCount()).toBe(0);
  });

  it('stays on the upload step for JSON that is not GeoJSON', async () => {
    const component = setup();

    await load(component, JSON.stringify({ hello: 'world' }));

    expect(component.step()).toBe('upload');
    expect(component.parseError()).toBe('GEOJSON_IMPORT.ERR_INVALID_GEOJSON');
  });

  it('stays on the upload step for GeoJSON with no polygons in it', async () => {
    const component = setup();

    await load(component, collection(line));

    expect(component.step()).toBe('upload');
    expect(component.parseError()).toBe('GEOJSON_IMPORT.ERR_NO_POLYGONS');
  });

  it('stays on the upload step for a file that is not JSON', async () => {
    const component = setup();

    await load(component, '{ not json');

    expect(component.step()).toBe('upload');
    expect(component.parseError()).toBe('GEOJSON_IMPORT.ERR_INVALID_JSON');
  });

  /** The preview read the first 50 features and dropped the rest without a word. */
  it('says how many features past the first 50 were left out', async () => {
    const component = setup();
    const named = (i: number) => ({ ...square, properties: { name: `Fence ${i}` } });

    await load(component, collection(...Array.from({ length: 53 }, (_, i) => named(i))));

    expect(component.previewFeatures()).toHaveLength(50);
    expect(component.overLimitCount()).toBe(3);
  });

  it('leaves nothing out of a file of 50 or fewer', async () => {
    const component = setup();

    await load(component, collection(square, square));

    expect(component.overLimitCount()).toBe(0);
  });

  describe('the import itself', () => {
    const fence = (id: number, displayName: string, activationFailed?: boolean) =>
      ({ id, activationFailed, displayName, kojiName: displayName.toLowerCase() }) as UserGeofence;

    const run = async (outcome: Subject<GeoJsonImportResult>) => {
      const component = setup();
      service.importGeoJson.mockReturnValue(outcome);
      await load(component, collection(square));
      component.updateRegion(0, 1);
      component.startImport();
      return component;
    };

    it('lists the fences saved but not switched on, with the retry hint', async () => {
      const outcome = new Subject<GeoJsonImportResult>();
      const component = await run(outcome);

      outcome.next({ created: [fence(1, 'Lake Eola'), fence(2, 'Lake Ivanhoe', true)], errors: [] });
      fixture.detectChanges();

      expect(component.notActivated().map(f => f.displayName)).toEqual(['Lake Ivanhoe']);
      const row = (fixture.nativeElement as HTMLElement).querySelector('.not-activated');
      expect(row?.textContent).toContain('GEOJSON_IMPORT.RESULT_NOT_ACTIVE');
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Lake Ivanhoe');
    });

    it('lists nothing extra when every fence was switched on', async () => {
      const outcome = new Subject<GeoJsonImportResult>();
      const component = await run(outcome);

      outcome.next({ created: [fence(1, 'Lake Eola')], errors: [] });
      fixture.detectChanges();

      expect(component.notActivated()).toEqual([]);
      expect((fixture.nativeElement as HTMLElement).querySelector('.not-activated')).toBeNull();
    });

    /** Closing the dialog mid-request must not lose the outcome: the fences are created regardless. */
    it('reports the outcome on settled even after the dialog is gone', async () => {
      const outcome = new Subject<GeoJsonImportResult>();
      const component = await run(outcome);
      const seen: (GeoJsonImportResult | null)[] = [];
      component.settled.subscribe(r => seen.push(r));

      fixture.destroy();
      const result = { created: [fence(1, 'Lake Eola')], errors: [] };
      outcome.next(result);

      expect(seen).toEqual([result]);
    });
  });
});
