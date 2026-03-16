import { Component, Input, OnChanges, SimpleChanges, inject, signal } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AreaService } from '../../../core/services/area.service';
import { LocationService } from '../../../core/services/location.service';

@Component({
  selector: 'app-delivery-preview',
  standalone: true,
  imports: [MatChipsModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    @if (mode === 'areas') {
      <div class="preview-box areas-preview">
        <div class="preview-label">
          <mat-icon>map</mat-icon>
          <span>Notifications will be sent for these areas:</span>
        </div>
        @if (areas().length > 0) {
          <mat-chip-set class="area-chips">
            @for (area of areas(); track area) {
              <mat-chip highlighted color="primary">{{ area }}</mat-chip>
            }
          </mat-chip-set>
        } @else {
          <p class="no-areas">No areas configured. <a href="/areas">Set up areas</a> first.</p>
        }
      </div>
    } @else if (mode === 'distance' && distanceKm > 0) {
      <div class="preview-box distance-preview">
        <div class="preview-label">
          <mat-icon>straighten</mat-icon>
          <span>Notifications within {{ distanceKm }} km of your location:</span>
        </div>
        @if (mapUrl()) {
          <img [src]="mapUrl()" class="distance-map" alt="Distance radius map" loading="lazy" />
        } @else if (loading()) {
          <div class="map-loading">
            <mat-spinner diameter="24"></mat-spinner>
          </div>
        }
      </div>
    }
  `,
  styles: [`
    .preview-box {
      margin: 12px 0;
      padding: 12px;
      border-radius: 8px;
      background: var(--skeleton-bg, rgba(0,0,0,0.03));
      border: 1px solid var(--divider, rgba(0,0,0,0.08));
    }
    .preview-label {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      color: var(--text-secondary, rgba(0,0,0,0.54));
      margin-bottom: 8px;
    }
    .preview-label mat-icon {
      font-size: 16px; width: 16px; height: 16px;
    }
    .area-chips {
      display: flex; flex-wrap: wrap; gap: 4px;
    }
    .no-areas {
      font-size: 13px;
      color: var(--text-hint, rgba(0,0,0,0.38));
      margin: 4px 0 0;
    }
    .no-areas a { color: #1976d2; }
    .distance-map {
      width: 100%;
      max-height: 180px;
      object-fit: cover;
      border-radius: 6px;
      border: 1px solid var(--divider, rgba(0,0,0,0.08));
    }
    .map-loading {
      display: flex;
      justify-content: center;
      padding: 24px;
    }
  `],
})
export class DeliveryPreviewComponent implements OnChanges {
  @Input() mode: 'areas' | 'distance' = 'areas';
  @Input() distanceKm = 0;

  private readonly areaService = inject(AreaService);
  private readonly locationService = inject(LocationService);

  areas = signal<string[]>([]);
  mapUrl = signal<string>('');
  loading = signal(false);

  private areasLoaded = false;
  private lastDistanceKey = '';

  ngOnChanges(changes: SimpleChanges): void {
    if (this.mode === 'areas' && !this.areasLoaded) {
      this.areasLoaded = true;
      this.areaService.getSelected().subscribe(a => this.areas.set(a));
    }

    if (this.mode === 'distance' && this.distanceKm > 0) {
      const distanceMeters = Math.round(this.distanceKm * 1000);
      const key = `${distanceMeters}`;
      if (key !== this.lastDistanceKey) {
        this.lastDistanceKey = key;
        this.mapUrl.set('');
        this.loading.set(true);

        // Get user location first, then fetch distance map
        this.locationService.getLocation().subscribe(loc => {
          if (loc && (loc.latitude !== 0 || loc.longitude !== 0)) {
            this.locationService.getDistanceMapUrl(loc.latitude, loc.longitude, distanceMeters).subscribe(result => {
              this.loading.set(false);
              if (result?.url) this.mapUrl.set(result.url);
            });
          } else {
            this.loading.set(false);
          }
        });
      }
    }
  }
}
