import { inject, Pipe, PipeTransform } from '@angular/core';

import { I18nService } from '../../core/services/i18n.service';

@Pipe({
  name: 'distanceDisplay',
  standalone: true,
})
export class DistanceDisplayPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(distance: number): string {
    if (distance === 0) {
      return this.i18n.instant('COMMON.USING_AREAS');
    }
    const km = distance / 1000;
    if (km >= 1) {
      const value = km % 1 === 0 ? km : km.toFixed(1);
      return this.i18n.instant('COMMON.DISTANCE_KM', { value });
    }
    return this.i18n.instant('COMMON.DISTANCE_M', { value: distance });
  }
}
