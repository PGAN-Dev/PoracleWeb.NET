import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'leagueName',
  standalone: true,
})
export class LeagueNamePipe implements PipeTransform {
  transform(league: number): string {
    switch (league) {
      case 500:
        return 'Little';
      case 1500:
        return 'Great';
      case 2500:
        return 'Ultra';
      default:
        return `${league}`;
    }
  }
}
