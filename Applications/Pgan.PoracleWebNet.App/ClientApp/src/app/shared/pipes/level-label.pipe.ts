import { inject, Pipe, PipeTransform } from '@angular/core';

import { resolveLevel } from '../../core/models/raid-level.models';
import { I18nService } from '../../core/services/i18n.service';

/**
 * Resolve a stored raid/egg level integer to its display label.
 *
 * - Standard tiers (1-5)      → "T1", "T2", ...
 * - Mega (6), Elite (7)       → "Mega", "Elite"
 * - 9000 (PoracleNG wildcard) → "Any"
 * - Anything else             → "Level {n}"
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
      return this.i18n.instant(opt.labelKey) + ' ' + opt.badge;
    }
    return this.i18n.instant(opt.labelKey);
  }
}
