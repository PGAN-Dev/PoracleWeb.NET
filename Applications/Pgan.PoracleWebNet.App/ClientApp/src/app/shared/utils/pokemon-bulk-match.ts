import type { PokemonEntry } from '../../core/services/masterdata.service';

export interface BulkMatchSuggestion {
  /** Ranked candidates (startsWith before includes), capped at 5. */
  candidates: PokemonEntry[];
  /** The original-cased text the user typed that didn't exactly match anything. */
  token: string;
}

export interface BulkMatchResult {
  /** Exactly matched but already selected before this parse (or repeated in the pasted text). */
  alreadySelected: PokemonEntry[];
  /** Newly matched, not already in the selection, deduped within this one parse. */
  matched: PokemonEntry[];
  /** No exact match, but close candidates exist -- the user picks one (or none). */
  suggestions: BulkMatchSuggestion[];
  /** No exact match and nothing close enough to suggest. */
  unrecognized: string[];
}

const MAX_SPAN_WORDS = 3;
const MAX_SUGGESTIONS = 5;

/**
 * Parses a pasted/typed block of Pokemon names -- comma, newline, or space separated, in any
 * mixture -- against the known species list. Commas and newlines are trusted segment boundaries;
 * within a segment, names are recovered by greedy longest-run word matching so multi-word species
 * ("Tapu Koko", "Mime Jr.") survive space-only input the same as single-word ones.
 */
export function matchPokemonBulkText(text: string, pokemon: PokemonEntry[], alreadySelectedIds: ReadonlySet<number>): BulkMatchResult {
  const byName = new Map<string, PokemonEntry>();
  for (const p of pokemon) {
    if (p.id === 0) continue; // "All Pokemon" is a sentinel, not a species name to type
    byName.set(p.name.toLowerCase(), p);
  }

  const matched: PokemonEntry[] = [];
  const alreadySelected: PokemonEntry[] = [];
  const unresolvedTokens: string[] = [];
  const seenIds = new Set(alreadySelectedIds);
  const seenTokens = new Set<string>();

  const segments = text
    .split(/[,\n]+/)
    .map(s => s.trim())
    .filter(Boolean);

  for (const segment of segments) {
    const words = segment.split(/\s+/).filter(Boolean);
    let i = 0;
    while (i < words.length) {
      let consumed = 0;
      for (let span = Math.min(MAX_SPAN_WORDS, words.length - i); span >= 1; span--) {
        const candidate = words
          .slice(i, i + span)
          .join(' ')
          .toLowerCase();
        const entry = byName.get(candidate);
        if (entry) {
          if (seenIds.has(entry.id)) {
            alreadySelected.push(entry);
          } else {
            matched.push(entry);
            seenIds.add(entry.id);
          }
          consumed = span;
          break;
        }
      }
      if (consumed === 0) {
        unresolvedTokens.push(words[i]);
        i += 1;
      } else {
        i += consumed;
      }
    }
  }

  const suggestions: BulkMatchSuggestion[] = [];
  const unrecognized: string[] = [];
  for (const token of unresolvedTokens) {
    const key = token.toLowerCase();
    if (seenTokens.has(key)) continue;
    seenTokens.add(key);

    const lower = key;
    const startsWith: PokemonEntry[] = [];
    const includes: PokemonEntry[] = [];
    for (const p of pokemon) {
      if (p.id === 0) continue;
      const name = p.name.toLowerCase();
      if (name.startsWith(lower)) startsWith.push(p);
      else if (name.includes(lower)) includes.push(p);
    }
    const candidates = [...startsWith, ...includes].slice(0, MAX_SUGGESTIONS);

    if (candidates.length === 0) {
      unrecognized.push(token);
    } else {
      suggestions.push({ candidates, token });
    }
  }

  return { alreadySelected, matched, suggestions, unrecognized };
}
