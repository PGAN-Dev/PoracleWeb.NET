import { inject, Pipe, PipeTransform } from '@angular/core';

import { I18nService } from '../../core/services/i18n.service';

@Pipe({
  name: 'teamName',
  standalone: true,
})
export class TeamNamePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(team: number): string {
    switch (team) {
      case 0:
        return this.i18n.instant('GYMS.TEAM_NEUTRAL');
      case 1:
        return this.i18n.instant('GYMS.TEAM_MYSTIC');
      case 2:
        return this.i18n.instant('GYMS.TEAM_VALOR');
      case 3:
        return this.i18n.instant('GYMS.TEAM_INSTINCT');
      default:
        return this.i18n.instant('GYMS.TEAM_UNKNOWN', { id: team });
    }
  }
}
