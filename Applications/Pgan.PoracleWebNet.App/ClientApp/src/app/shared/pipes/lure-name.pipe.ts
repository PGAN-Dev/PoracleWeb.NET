import { inject, Pipe, PipeTransform } from '@angular/core';

import { I18nService } from '../../core/services/i18n.service';

@Pipe({
  name: 'lureName',
  standalone: true,
})
export class LureNamePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(lureId: number): string {
    switch (lureId) {
      case 501:
        return this.i18n.instant('LURES.TYPE_NORMAL');
      case 502:
        return this.i18n.instant('LURES.TYPE_GLACIAL');
      case 503:
        return this.i18n.instant('LURES.TYPE_MOSSY');
      case 504:
        return this.i18n.instant('LURES.TYPE_MAGNETIC');
      case 505:
        return this.i18n.instant('LURES.TYPE_RAINY');
      case 506:
        return this.i18n.instant('LURES.TYPE_GOLDEN');
      default:
        return this.i18n.instant('LURES.TYPE_UNKNOWN', { id: lureId });
    }
  }
}
