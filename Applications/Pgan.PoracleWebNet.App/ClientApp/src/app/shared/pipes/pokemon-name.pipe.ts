import { Pipe, PipeTransform, inject } from '@angular/core';

import { MasterDataService } from '../../core/services/masterdata.service';

@Pipe({
  name: 'pokemonName',
  standalone: true,
})
export class PokemonNamePipe implements PipeTransform {
  private readonly masterData = inject(MasterDataService);

  transform(pokemonId: number): string {
    return this.masterData.getPokemonName(pokemonId);
  }
}
