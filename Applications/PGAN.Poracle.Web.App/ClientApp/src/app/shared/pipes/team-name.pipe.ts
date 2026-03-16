import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'teamName',
  standalone: true,
})
export class TeamNamePipe implements PipeTransform {
  transform(team: number): string {
    switch (team) {
      case 0:
        return 'Neutral';
      case 1:
        return 'Mystic';
      case 2:
        return 'Valor';
      case 3:
        return 'Instinct';
      default:
        return `Team ${team}`;
    }
  }
}
