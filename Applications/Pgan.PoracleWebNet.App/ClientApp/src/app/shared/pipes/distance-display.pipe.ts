import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'distanceDisplay',
  standalone: true,
})
export class DistanceDisplayPipe implements PipeTransform {
  transform(distance: number): string {
    if (distance === 0) {
      return 'Using Areas';
    }
    const km = distance / 1000;
    if (km >= 1) {
      return `${km % 1 === 0 ? km : km.toFixed(1)} km`;
    }
    return `${distance} m`;
  }
}
