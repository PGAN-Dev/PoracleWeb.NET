import { TestBed } from '@angular/core/testing';

import { PokemonNamePipe } from './pokemon-name.pipe';
import { MasterDataService } from '../../core/services/masterdata.service';

describe('PokemonNamePipe', () => {
  let pipe: PokemonNamePipe;
  let masterData: { getPokemonName: jest.Mock };

  beforeEach(() => {
    masterData = {
      getPokemonName: jest.fn((id: number) => {
        if (id === 0) return 'All Pokemon';
        if (id === 25) return 'Pikachu';
        return `Pokemon #${id}`;
      }),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [PokemonNamePipe, { provide: MasterDataService, useValue: masterData }],
    });
    pipe = TestBed.inject(PokemonNamePipe);
  });

  it('should return "All Pokemon" for ID 0', () => {
    expect(pipe.transform(0)).toBe('All Pokemon');
  });

  it('should return pokemon name from masterdata', () => {
    expect(pipe.transform(25)).toBe('Pikachu');
    expect(masterData.getPokemonName).toHaveBeenCalledWith(25);
  });

  it('should return fallback for unknown pokemon', () => {
    expect(pipe.transform(9999)).toBe('Pokemon #9999');
  });
});
