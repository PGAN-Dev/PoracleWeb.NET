import { Component, Input, OnChanges, SimpleChanges, inject, signal } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslateModule } from '@ngx-translate/core';

import { AreaService } from '../../../core/services/area.service';
import { LocationService } from '../../../core/services/location.service';

@Component({
  imports: [MatChipsModule, MatIconModule, MatProgressSpinnerModule, TranslateModule],
  selector: 'app-delivery-preview',
  standalone: true,
  styleUrl: './delivery-preview.component.scss',
  templateUrl: './delivery-preview.component.html',
})
export class DeliveryPreviewComponent implements OnChanges {
  private readonly areaService = inject(AreaService);
  private areasLoaded = false;

  private lastDistanceKey = '';
  private readonly locationService = inject(LocationService);

  areas = signal<string[]>([]);
  @Input() distanceKm = 0;
  loading = signal(false);

  mapUrl = signal<string>('');
  @Input() mode: 'areas' | 'distance' = 'areas';

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
