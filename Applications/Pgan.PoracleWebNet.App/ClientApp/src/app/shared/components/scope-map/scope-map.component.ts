import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  computed,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import * as L from 'leaflet';

import { ScopeDrawing, scopeDrawing } from './scope-drawing';
import { AlarmScope, GeofenceData } from '../../../core/models';
import { AreaService } from '../../../core/services/area.service';
import { BasemapService } from '../../../core/services/basemap.service';
import { PlacesService } from '../../../core/services/places.service';

/**
 * A small, still map under the scope choice that draws where the alarm reaches you: a circle that grows
 * with the radius, the areas chosen, or the profile's areas.
 *
 * Three radio buttons hid the most consequential choice in every alarm dialog. A drawn circle makes an
 * out-of-range radius obvious before anyone saves, and a shaded area says which "Aberdeen" was meant.
 *
 * It sends no request of its own that a `disable_*` switch refuses: the pin and places come from
 * {@link PlacesService}, which the scope picker loads and which never asks under `disable_location`; the
 * polygons come from the area feed, which nothing gates. With nothing it can draw, it draws nothing at all
 * rather than an empty grey box.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  selector: 'app-scope-map',
  standalone: true,
  styleUrl: './scope-map.component.scss',
  template: `
    @if (visible()) {
      <div #mapContainer class="scope-map" role="img" [attr.aria-label]="'WHERE.MAP_LABEL' | translate"></div>
    }
  `,
})
export class ScopeMapComponent {
  private readonly areaService = inject(AreaService);
  private bounds: L.LatLngBounds | null = null;
  private readonly places = inject(PlacesService);
  /** The profile's areas, for "Anywhere in my areas". */
  readonly profileAreas = input<string[]>([]);
  readonly scope = input.required<AlarmScope>();
  /** What to draw, before asking whether the polygons for it have arrived. */
  private readonly drawing = computed<ScopeDrawing>(() =>
    scopeDrawing(this.scope(), this.places.pin(), this.places.named(), this.profileAreas()),
  );

  private readonly injector = inject(Injector);
  private layer: L.LayerGroup | null = null;
  private map: L.Map | null = null;

  private readonly mapContainer = viewChild<ElementRef<HTMLDivElement>>('mapContainer');

  private readonly polygons = signal<GeofenceData[]>([]);

  private polygonsRequested = false;

  private resizeObserver: null | ResizeObserver = null;

  private readonly shapes = computed(() => {
    const wanted = new Set(this.drawing().areas.map(a => a.toLowerCase()));
    const fences = wanted.size === 0 ? [] : this.polygons().filter(g => wanted.has(g.name.toLowerCase()) && (g.path?.length ?? 0) >= 3);
    return { circle: this.drawing().circle, fences };
  });

  readonly visible = computed(() => this.shapes().circle !== null || this.shapes().fences.length > 0);

  constructor() {
    // The polygons are only read once an area scope asks for them: a radius never needs the feed.
    effect(() => {
      if (this.drawing().areas.length === 0 || this.polygonsRequested) return;
      this.polygonsRequested = true;
      this.areaService.getGeofencePolygonsShared().subscribe(polygons => this.polygons.set(polygons));
    });

    effect(() => {
      const container = this.mapContainer()?.nativeElement;
      const shapes = this.shapes();
      if (!container) {
        this.teardown();
        return;
      }
      if (!this.map) this.createMap(container);
      this.draw(shapes.circle, shapes.fences);
    });

    inject(DestroyRef).onDestroy(() => this.teardown());
  }

  private createMap(container: HTMLDivElement): void {
    this.map = L.map(container, {
      boxZoom: false,
      doubleClickZoom: false,
      dragging: false,
      keyboard: false,
      scrollWheelZoom: false,
      touchZoom: false,
      zoomControl: false,
    });
    this.map.attributionControl.setPrefix(false);
    // Asked for here rather than injected up front: only a dialog that actually draws a map needs the
    // basemap service, and constructing it starts the theme and settings tracking for nothing.
    this.injector.get(BasemapService).attach(this.map, { maxZoom: 18 });
    this.layer = L.layerGroup().addTo(this.map);

    // Inside a dialog tab the container is often still 0 wide when the map is made; refit once it has
    // a size, or Leaflet centres everything on the top-left corner.
    if (typeof ResizeObserver !== 'undefined') {
      this.resizeObserver = new ResizeObserver(() => {
        this.map?.invalidateSize();
        this.fit();
      });
      this.resizeObserver.observe(container);
    }
  }

  private draw(circle: ScopeDrawing['circle'], fences: GeofenceData[]): void {
    if (!this.map || !this.layer) return;
    this.layer.clearLayers();

    // The colour itself comes from the stylesheet (`.scope-map-shape`), which can read the theme's tokens;
    // an SVG attribute can't hold a `light-dark()` value. These are the fallback.
    const colour = '#1976d2';
    const bounds = L.latLngBounds([]);

    for (const fence of fences) {
      const polygon = L.polygon(
        fence.path.map(([lat, lon]) => L.latLng(lat, lon)),
        { className: 'scope-map-shape', color: colour, fillColor: colour, fillOpacity: 0.3, weight: 2 },
      );
      polygon.addTo(this.layer);
      bounds.extend(polygon.getBounds());
    }

    if (circle) {
      const centre = L.latLng(circle.latitude, circle.longitude);
      L.circle(centre, {
        className: 'scope-map-shape',
        color: colour,
        fillColor: colour,
        fillOpacity: 0.18,
        radius: circle.radiusMetres,
        weight: 2,
      }).addTo(this.layer);
      L.circleMarker(centre, {
        className: 'scope-map-shape scope-map-centre',
        color: colour,
        fillColor: colour,
        fillOpacity: 1,
        radius: 4,
        weight: 1,
      }).addTo(this.layer);
      // toBounds needs no map, unlike a circle's own getBounds before it has been drawn.
      bounds.extend(centre.toBounds(circle.radiusMetres * 2));
    }

    this.bounds = bounds.isValid() ? bounds : null;
    this.fit();
  }

  private fit(): void {
    if (!this.map || !this.bounds) return;
    const size = this.map.getSize();
    if (size.x === 0 || size.y === 0) return;
    this.map.fitBounds(this.bounds, { animate: false, padding: [12, 12] });
  }

  private teardown(): void {
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
    this.map?.remove();
    this.map = null;
    this.layer = null;
    this.bounds = null;
  }
}
