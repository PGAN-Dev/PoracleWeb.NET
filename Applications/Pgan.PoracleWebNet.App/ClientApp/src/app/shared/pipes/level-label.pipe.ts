import { inject, Pipe, PipeTransform } from '@angular/core';

import { resolveLevel } from '../../core/models/raid-level.models';
import { I18nService } from '../../core/services/i18n.service';

/**
 * Resolve a stored raid/egg level integer to its display label.
 *
 * - Levels 1-19              → masterfile names ("1 Star Raid", "Mega Legendary Raid", "Elite Raid", …)
 * - 9000 (wildcard sentinel) → "Any"
 * - Anything else             → "Level {n}" (custom)
 */
@Pipe({
  name: 'levelLabel',
  standalone: true,
})
export class LevelLabelPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: number): string {
    const opt = resolveLevel(value);
    if (opt.category === 'custom') {
      return this.i18n.instant(opt.labelKey) + ' ' + opt.value;
    }
    return this.i18n.instant(opt.labelKey);
  }
}
