import { inject, Pipe, PipeTransform } from '@angular/core';

import { I18nService } from '../../core/services/i18n.service';

@Pipe({
  name: 'genderDisplay',
  standalone: true,
})
export class GenderDisplayPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(gender: number): string {
    switch (gender) {
      case 1:
        return `\u2642 ${this.i18n.instant('COMMON.MALE')}`;
      case 2:
        return `\u2640 ${this.i18n.instant('COMMON.FEMALE')}`;
      default:
        return this.i18n.instant('COMMON.ALL');
    }
  }
}
