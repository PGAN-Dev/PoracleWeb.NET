import { inject, Pipe, PipeTransform } from '@angular/core';

import { I18nService } from '../../core/services/i18n.service';

@Pipe({
  name: 'leagueName',
  standalone: true,
})
export class LeagueNamePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(league: number): string {
    switch (league) {
      case 500:
        return this.i18n.instant('POKEMON.LEAGUE_LITTLE');
      case 1500:
        return this.i18n.instant('POKEMON.LEAGUE_GREAT');
      case 2500:
        return this.i18n.instant('POKEMON.LEAGUE_ULTRA');
      case 10000:
        return this.i18n.instant('POKEMON.LEAGUE_MASTER');
      default:
        return `${league}`;
    }
  }
}
