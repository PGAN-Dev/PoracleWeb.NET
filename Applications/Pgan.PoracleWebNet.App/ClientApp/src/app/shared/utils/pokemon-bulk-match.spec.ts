import { matchPokemonBulkText } from './pokemon-bulk-match';
import type { PokemonEntry } from '../../core/services/masterdata.service';

describe('matchPokemonBulkText', () => {
  const pokemon: PokemonEntry[] = [
    { id: 0, name: 'All Pokemon' },
    { id: 6, name: 'Charizard' },
    { id: 25, name: 'Pikachu' },
    { id: 29, name: 'Nidoran Female' },
    { id: 32, name: 'Nidoran Male' },
    { id: 33, name: 'Nidorino' },
    { id: 150, name: 'Mewtwo' },
    { id: 785, name: 'Tapu Koko' },
    { id: 439, name: 'Mime Jr.' },
  ];

  it('matches comma-separated names', () => {
    const result = matchPokemonBulkText('Charizard, Pikachu, Mewtwo', pokemon, new Set());
    expect(result.matched.map(p => p.id)).toEqual([6, 25, 150]);
    expect(result.unrecognized).toEqual([]);
    expect(result.suggestions).toEqual([]);
  });

  it('matches newline-separated names', () => {
    const result = matchPokemonBulkText('charizard\npikachu\nmewtwo', pokemon, new Set());
    expect(result.matched.map(p => p.id)).toEqual([6, 25, 150]);
  });

  it('greedily matches multi-word names in a space-separated run', () => {
    const result = matchPokemonBulkText('pikachu tapu koko mewtwo', pokemon, new Set());
    expect(result.matched.map(p => p.id)).toEqual([25, 785, 150]);
  });

  it('matches a multi-word name with trailing punctuation', () => {
    const result = matchPokemonBulkText('mime jr.', pokemon, new Set());
    expect(result.matched.map(p => p.id)).toEqual([439]);
  });

  it('is case-insensitive', () => {
    const result = matchPokemonBulkText('CHARIZARD, pikaCHU', pokemon, new Set());
    expect(result.matched.map(p => p.id)).toEqual([6, 25]);
  });

  it('reports names already in the selection instead of re-adding them', () => {
    const result = matchPokemonBulkText('charizard, pikachu', pokemon, new Set([6]));
    expect(result.matched.map(p => p.id)).toEqual([25]);
    expect(result.alreadySelected.map(p => p.id)).toEqual([6]);
  });

  it('does not add the same species twice from one paste', () => {
    const result = matchPokemonBulkText('pikachu, pikachu', pokemon, new Set());
    expect(result.matched.map(p => p.id)).toEqual([25]);
    expect(result.alreadySelected.map(p => p.id)).toEqual([25]);
  });

  it('never matches the "All Pokemon" sentinel from typed text', () => {
    const result = matchPokemonBulkText('all pokemon', pokemon, new Set());
    expect(result.matched).toEqual([]);
  });

  it('surfaces an ambiguous prefix as suggestions instead of guessing', () => {
    const result = matchPokemonBulkText('nido', pokemon, new Set());
    expect(result.matched).toEqual([]);
    expect(result.suggestions).toHaveLength(1);
    expect(result.suggestions[0].token).toBe('nido');
    expect(result.suggestions[0].candidates.map(p => p.id)).toEqual([29, 32, 33]);
  });

  it('reports a name with no close candidates as unrecognized', () => {
    const result = matchPokemonBulkText('zzznotaspecies', pokemon, new Set());
    expect(result.matched).toEqual([]);
    expect(result.suggestions).toEqual([]);
    expect(result.unrecognized).toEqual(['zzznotaspecies']);
  });

  it('dedupes repeated unresolved tokens', () => {
    const result = matchPokemonBulkText('nido, nido', pokemon, new Set());
    expect(result.suggestions).toHaveLength(1);
  });

  it('ignores blank lines and stray whitespace', () => {
    const result = matchPokemonBulkText('  charizard ,,  \n\n pikachu  ', pokemon, new Set());
    expect(result.matched.map(p => p.id)).toEqual([6, 25]);
  });
});
