import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'lureName',
  standalone: true,
})
export class LureNamePipe implements PipeTransform {
  transform(lureId: number): string {
    switch (lureId) {
      case 501:
        return 'Normal';
      case 502:
        return 'Glacial';
      case 503:
        return 'Mossy';
      case 504:
        return 'Magnetic';
      case 505:
        return 'Rainy';
      case 506:
        return 'Golden';
      default:
        return `Lure #${lureId}`;
    }
  }
}
