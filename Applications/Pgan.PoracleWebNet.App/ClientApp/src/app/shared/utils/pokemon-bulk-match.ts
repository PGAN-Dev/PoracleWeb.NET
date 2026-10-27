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
  /** Recognised, but not a species this picker offers (a max battle picker's non-bosses). */
  notAllowed: string[];
  /** No exact match, but close candidates exist -- the user picks one (or none). */
  suggestions: BulkMatchSuggestion[];
  /** No exact match and nothing close enough to suggest. */
  unrecognized: string[];
}

const MAX_SPAN_WORDS = 3;
const MAX_SUGGESTIONS = 5;

/** `25`, `#25`, `1-151`, `#1-#151`: a dex number or an inclusive range of them, once dashes are normalised. */
const DEX_TOKEN = /^#?(\d+)(?:-#?(\d+))?$/;

/**
 * Parses a pasted/typed block of Pokemon names and dex numbers -- comma, newline, or space separated, in
 * any mixture -- against the known species list. Commas and newlines are trusted segment boundaries;
 * within a segment, names are recovered by greedy longest-run word matching so multi-word species
 * ("Tapu Koko", "Mime Jr.") survive space-only input the same as single-word ones.
 *
 * A number is a dex number (`25`, `#25`) and two joined by a dash are a range (`1-151`, `650 – 721`,
 * either way round), so a list copied from a spreadsheet or a "Gen 1" post works. A number is never
 * offered name suggestions: "2" used to suggest Porygon2. `allowedIds`, when it holds any, is the
 * picker's own restriction, and a species outside it is reported rather than added.
 */
export function matchPokemonBulkText(
  text: string,
  pokemon: PokemonEntry[],
  alreadySelectedIds: ReadonlySet<number>,
  allowedIds?: null | ReadonlySet<number>,
): BulkMatchResult {
  const isAllowed = (p: PokemonEntry) => !allowedIds || allowedIds.size === 0 || allowedIds.has(p.id);
  const byName = new Map<string, PokemonEntry>();
  const byId = new Map<number, PokemonEntry>();
  let maxId = 0;
  for (const p of pokemon) {
    if (p.id === 0) continue; // "All Pokemon" is a sentinel, not a species name to type
    byName.set(p.name.toLowerCase(), p);
    byId.set(p.id, p);
    maxId = Math.max(maxId, p.id);
  }

  const matched: PokemonEntry[] = [];
  const alreadySelected: PokemonEntry[] = [];
  const notAllowed: string[] = [];
  const unrecognized: string[] = [];
  const unresolvedTokens: string[] = [];
  const seenIds = new Set(alreadySelectedIds);
  const seenTokens = new Set<string>();

  const take = (entry: PokemonEntry): void => {
    if (seenIds.has(entry.id)) {
      alreadySelected.push(entry);
    } else {
      matched.push(entry);
      seenIds.add(entry.id);
    }
  };

  /** Takes a dex number or range; false when the word is neither, so it goes on to the name match. */
  const takeNumbers = (word: string): boolean => {
    const dex = DEX_TOKEN.exec(word);
    if (!dex) return false;
    const a = Number(dex[1]);
    const b = dex[2] === undefined ? a : Number(dex[2]);
    // Clamped to the dex, so "1-99999999" walks a thousand numbers and not a hundred million.
    const lo = Math.max(1, Math.min(a, b));
    const hi = Math.min(maxId, Math.max(a, b));
    let found = false;
    let refused = false;
    for (let id = lo; id <= hi; id++) {
      const entry = byId.get(id);
      if (!entry) continue;
      if (isAllowed(entry)) {
        take(entry);
        found = true;
      } else {
        refused = true;
      }
    }
    if (!found) (refused ? notAllowed : unrecognized).push(word);
    return true;
  };

  const segments = text
    .split(/[,\n]+/)
    .map(s => s.trim())
    .filter(Boolean);

  for (const raw of segments) {
    // "650 – 721" and "#1 - #151" become one word, so the range survives the split on spaces. Only a dash
    // between two numbers is touched; Ho-Oh and Porygon-Z keep theirs.
    const segment = raw.replace(/[‐-―−]/g, '-').replace(/(\d)\s*-\s*(#?\d)/g, '$1-$2');
    const words = segment.split(/\s+/).filter(Boolean);
    let i = 0;
    while (i < words.length) {
      if (takeNumbers(words[i])) {
        i += 1;
        continue;
      }
      let consumed = 0;
      for (let span = Math.min(MAX_SPAN_WORDS, words.length - i); span >= 1; span--) {
        const phrase = words.slice(i, i + span).join(' ');
        const entry = byName.get(phrase.toLowerCase());
        if (entry) {
          if (isAllowed(entry)) take(entry);
          else notAllowed.push(phrase);
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
  for (const token of unresolvedTokens) {
    const key = token.toLowerCase();
    if (seenTokens.has(key)) continue;
    seenTokens.add(key);

    const startsWith: PokemonEntry[] = [];
    const includes: PokemonEntry[] = [];
    for (const p of pokemon) {
      if (p.id === 0 || !isAllowed(p)) continue;
      const name = p.name.toLowerCase();
      if (name.startsWith(key)) startsWith.push(p);
      else if (name.includes(key)) includes.push(p);
    }
    const candidates = [...startsWith, ...includes].slice(0, MAX_SUGGESTIONS);

    if (candidates.length === 0) {
      unrecognized.push(token);
    } else {
      suggestions.push({ candidates, token });
    }
  }

  return { alreadySelected, matched, notAllowed, suggestions, unrecognized };
}
