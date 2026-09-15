/**
 * Tidies the sentence PoracleNG renders for a tracking rule into something a card can print.
 *
 * The upstream string is written for Discord, so it carries markdown emphasis and the loose spacing
 * that survives a chat client: `**Bulbasaur**  | distance: 5000m | iv: 90%-100% `. The bold always
 * wraps the species or the level, which is already the card's heading, so keeping it would double the
 * emphasis and fight the `<h3>` rather than help it. Everything here is plain-text transformation --
 * the result is interpolated, never handed to innerHTML.
 *
 * The stripping is deliberately pair-aware rather than a sweep of `[*_`]`. PoracleNG interpolates
 * user-chosen strings into this sentence without escaping them -- `rowtext` builds `**%s**` around a
 * template name, an area or a saved-place label -- and `work_gym` is a name, not italics. Discord
 * agrees: an underscore flanked by word characters emphasises nothing. So a delimiter is only removed
 * where it has a partner, and the underscore forms additionally need a non-word character on the
 * outside, which is exactly the boundary Discord applies.
 */
export function cleanRuleSummary(raw: null | string | undefined): string {
  if (!raw) return '';

  return (
    raw
      // Code spans first: inside a backtick pair Discord renders the rest literally, so unwrapping the
      // span before looking for emphasis stops a `*` in code being read as a delimiter.
      .replace(/`([^`\n]+)`/g, '$1')
      .replace(/\*\*(?=\S)([\s\S]*?\S)\*\*/g, '$1')
      .replace(/\*(?=\S)([^*\n]*?\S)\*/g, '$1')
      .replace(/(?<![\w])__(?=\S)([^_]*?\S)__(?![\w])/g, '$1')
      .replace(/(?<![\w])_(?=\S)([^_]*?\S)_(?![\w])/g, '$1')
      .replace(/\s+/g, ' ')
      .replace(/\s*\|\s*/g, ' | ')
      .replace(/^[\s|]+/, '')
      .replace(/[\s|]+$/, '')
      .trim()
  );
}

/**
 * Above this many characters the two-line clamp will engage at the grid's minimum card width, so the
 * line earns an expand control; below it, an affordance would point at nothing.
 *
 * A string length rather than a DOM measurement on purpose: the Pokemon page routinely renders several
 * hundred cards, and measuring each one costs a layout pass per card to answer a question worth one
 * comparison. Roughly 95 characters fit in two lines at the 300px minimum card width, so the threshold
 * sits just under that and errs towards offering the control on a line that only just fits.
 */
export const RULE_SUMMARY_CLAMP_THRESHOLD = 90;

export function ruleSummaryNeedsExpanding(cleaned: string): boolean {
  return cleaned.length > RULE_SUMMARY_CLAMP_THRESHOLD;
}
