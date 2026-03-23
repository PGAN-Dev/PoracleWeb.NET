import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'genderDisplay',
  standalone: true,
})
export class GenderDisplayPipe implements PipeTransform {
  transform(gender: number): string {
    switch (gender) {
      case 1:
        return '\u2642 Male';
      case 2:
        return '\u2640 Female';
      default:
        return 'All';
    }
  }
}
