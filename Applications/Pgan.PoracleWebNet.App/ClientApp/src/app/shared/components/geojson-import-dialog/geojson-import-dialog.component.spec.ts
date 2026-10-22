import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';

import { GeoJsonImportDialogComponent, GeoJsonImportDialogData } from './geojson-import-dialog.component';
import { UserGeofenceService } from '../../../core/services/user-geofence.service';

/**
 * A file with a polygon and a line previewed "1 ready to import" and never mentioned the line, and a file
 * that wasn't GeoJSON at all moved on to "Select & Name Geofences" with "0 B" and the error underneath.
 */
describe('GeoJsonImportDialogComponent reading a file', () => {
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

  const setup = () => {
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
        { provide: UserGeofenceService, useValue: { importGeoJson: jest.fn() } },
      ],
      imports: [GeoJsonImportDialogComponent],
    });
    const fixture = TestBed.createComponent(GeoJsonImportDialogComponent);
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
});
