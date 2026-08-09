import { Component, Input, OnChanges, SimpleChanges, inject, signal } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { catchError, of } from 'rxjs';

import { AreaService } from '../../../core/services/area.service';
import { LocationService } from '../../../core/services/location.service';

@Component({
  imports: [RouterLink, MatChipsModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe],
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

        // Get user location first, then fetch distance map. The error arm matters: with disable_location
        // on, GET /api/location answers 403, and a next-only subscriber left `loading` set -- so the
        // preview inside every add and edit alarm dialog span forever the moment distance mode was
        // picked. No location means no map, which is what an empty mapUrl already renders. See #617.
        this.locationService
          .getLocation()
          .pipe(catchError(() => of(null)))
          .subscribe(loc => {
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
