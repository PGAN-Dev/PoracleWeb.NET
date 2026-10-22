import { inject, Pipe, PipeTransform } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

import { translatePlural } from '../../core/i18n/plural';

/**
 * `{{ 'ALARM.ALARMS_COUNT' | translatePlural: count }}` picks the `_ONE`/`_FEW`/`_MANY`/`_OTHER` form of a
 * counted message for the display language. Extra interpolation params go in the second argument.
 * Impure for the same reason ngx-translate's own pipe is: the language can change under a fixed input.
 */
@Pipe({
  name: 'translatePlural',
  pure: false,
  standalone: true,
})
export class TranslatePluralPipe implements PipeTransform {
  private readonly translate = inject(TranslateService);

  transform(base: string, count: number, params?: Record<string, unknown>): string {
    return translatePlural(this.translate, this.translate.getCurrentLang() ?? 'en', base, count, params);
  }
}
