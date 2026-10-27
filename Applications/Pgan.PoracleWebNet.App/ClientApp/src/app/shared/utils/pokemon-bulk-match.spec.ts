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
  describe('dex numbers and ranges (#1090)', () => {
    const dex: PokemonEntry[] = [
      { id: 0, name: 'All Pokemon' },
      ...Array.from({ length: 30 }, (_, i) => ({ id: i + 1, name: `Mon${i + 1}` })),
      { id: 233, name: 'Porygon2' },
      { id: 250, name: 'Ho-Oh' },
    ];
    const ids = (text: string, selected: number[] = [], allowed?: number[]) =>
      matchPokemonBulkText(text, dex, new Set(selected), allowed ? new Set(allowed) : null);

    it('adds a species by its dex number, with or without #', () => {
      expect(ids('25, #3').matched.map(p => p.id)).toEqual([25, 3]);
    });

    it('never suggests a name for a number', () => {
      const result = ids('2');
      expect(result.matched.map(p => p.id)).toEqual([2]);
      expect(result.suggestions).toEqual([]);
    });

    it('expands a range, a spaced range, an en dash range and a reversed range', () => {
      expect(ids('1-3').matched.map(p => p.id)).toEqual([1, 2, 3]);
      expect(ids('4 - 5').matched.map(p => p.id)).toEqual([4, 5]);
      expect(ids('6 – 7').matched.map(p => p.id)).toEqual([6, 7]);
      expect(ids('#9-#8').matched.map(p => p.id)).toEqual([8, 9]);
    });

    it('mixes numbers, ranges and names in one paste', () => {
      expect(ids('1-2, Porygon2\n25 ho-oh').matched.map(p => p.id)).toEqual([1, 2, 233, 25, 250]);
    });

    it('keeps a range to the species that exist and reports one that holds none', () => {
      expect(ids('29-40').matched.map(p => p.id)).toEqual([29, 30]);
      const result = ids('500-600');
      expect(result.matched).toEqual([]);
      expect(result.unrecognized).toEqual(['500-600']);
    });

    it('counts 0 and numbers no species has as unrecognized', () => {
      const result = ids('0, 9999');
      expect(result.matched).toEqual([]);
      expect(result.unrecognized).toEqual(['0', '9999']);
      expect(result.suggestions).toEqual([]);
    });

    it('reports numbers already picked instead of re-adding them', () => {
      const result = ids('1-3', [2]);
      expect(result.matched.map(p => p.id)).toEqual([1, 3]);
      expect(result.alreadySelected.map(p => p.id)).toEqual([2]);
    });

    it('adds only the allowed species from a range, and names what the picker does not offer', () => {
      const result = ids('1-5, 7, Mon9', [], [2, 4]);
      expect(result.matched.map(p => p.id)).toEqual([2, 4]);
      expect(result.notAllowed).toEqual(['7', 'Mon9']);
      expect(result.suggestions).toEqual([]);
    });

    it('still matches every name when nothing restricts the picker', () => {
      expect(ids('Mon9', [], []).matched.map(p => p.id)).toEqual([9]);
    });
  });
});
