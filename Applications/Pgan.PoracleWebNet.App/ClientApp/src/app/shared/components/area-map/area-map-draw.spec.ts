import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';
import 'leaflet-draw';

import { AreaMapComponent } from './area-map.component';

/**
 * Draw mode's banner says "click on the map to place polygon points", but entering it only added the
 * Leaflet.draw toolbar: clicks did nothing until the user found the small polygon button in the corner.
 * Entering draw mode now starts the polygon tool itself. The toolbar stays, for its Finish, Delete last
 * point and Cancel actions and so a cancelled shape can be restarted.
 *
 * leaflet-draw extends the global `L`, and a namespace import of leaflet's UMD bundle does not see
 * properties added to it afterwards -- `L.Draw` is undefined through the import, here and in the app.
 */
const drawPolygonPrototype = () => (window as unknown as { L: { Draw: { Polygon: { prototype: object } } } }).L.Draw.Polygon.prototype;

describe('AreaMapComponent draw mode', () => {
  let fixture: ComponentFixture<AreaMapComponent>;
  let enable: jest.SpyInstance;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService()],
      imports: [AreaMapComponent],
    });
    jest.spyOn(L.Map.prototype, 'getSize').mockReturnValue(L.point(1200, 400));
    enable = jest.spyOn(drawPolygonPrototype() as { enable: () => void }, 'enable');

    fixture = TestBed.createComponent(AreaMapComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    jest.restoreAllMocks();
  });

  const container = () => fixture.nativeElement as HTMLElement;

  it('does not draw while draw mode is off', () => {
    expect(enable).not.toHaveBeenCalled();
    expect(container().querySelector('.leaflet-draw')).toBeNull();
  });

  it('starts the polygon tool as soon as draw mode is on, so the first map click places a point', () => {
    fixture.componentRef.setInput('drawMode', true);
    fixture.detectChanges();

    expect(enable).toHaveBeenCalledTimes(1);
    // The handler it enabled is the toolbar's own, so the toolbar's actions drive the same shape.
    expect(container().querySelector('.leaflet-draw')).not.toBeNull();
  });

  it('stops the polygon tool and removes the toolbar when draw mode ends', () => {
    const disable = jest.spyOn(drawPolygonPrototype() as { disable: () => void }, 'disable');
    fixture.componentRef.setInput('drawMode', true);
    fixture.detectChanges();

    fixture.componentRef.setInput('drawMode', false);
    fixture.detectChanges();

    expect(disable).toHaveBeenCalled();
    expect(container().querySelector('.leaflet-draw')).toBeNull();
  });
});
