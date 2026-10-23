/**
 * Presentation helpers for a Poracle human's `type`. Whether a row is a person or a destination is the
 * server's call (`AdminUser.kind`, from `HumanKind.IsPerson`); nothing here decides it or grants anything.
 */

/** The six types an admin can create from Destinations, in the order the Add dialog offers them. */
export const DESTINATION_KINDS: readonly string[] = [
  'webhook',
  'discord:channel',
  'discord:thread',
  'telegram:channel',
  'telegram:group',
  'telegram:topic',
];

/** The destination types a role or user mention reaches. Telegram has no equivalent. */
export const MENTION_CAPABLE: ReadonlySet<string> = new Set(['webhook', 'discord:channel', 'discord:thread']);

// A Map, not an object literal: `'constructor' in {}` is true, and a type string is operator-written.
const LABEL_KEYS: ReadonlyMap<string, string> = new Map([
  ['webhook', 'ADMIN.TYPE_WEBHOOK'],
  ['discord:channel', 'ADMIN.TYPE_DISCORD_CHANNEL'],
  ['discord:thread', 'ADMIN.TYPE_DISCORD_THREAD'],
  ['telegram:channel', 'ADMIN.TYPE_TELEGRAM_CHANNEL'],
  ['telegram:group', 'ADMIN.TYPE_TELEGRAM_GROUP'],
  ['telegram:topic', 'ADMIN.TYPE_TELEGRAM_TOPIC'],
]);

function normalise(type: string | null | undefined): string {
  return (type ?? '').trim().toLowerCase();
}

/** Whether a mention can reach this destination type. */
export function mentionCapable(type: string | null | undefined): boolean {
  return MENTION_CAPABLE.has(normalise(type));
}

/** Whether this is one of the six destination types the app knows by name. */
export function isKnownKind(type: string | null | undefined): boolean {
  return LABEL_KEYS.has(normalise(type));
}

/**
 * Mirrors the server's `HumanKind.IsPerson` (no type, or one ending `:user`) for labelling only: a 409 can name
 * a person already holding the id, and "Other" would misdescribe them. Never use it to grant anything.
 */
export function isPersonType(type: string | null | undefined): boolean {
  const t = normalise(type);
  return t === '' || t.endsWith(':user');
}

/** The translation key naming a known type; anything else is `ADMIN.TYPE_OTHER`. */
export function kindLabelKey(type: string | null): string {
  return LABEL_KEYS.get(normalise(type)) ?? 'ADMIN.TYPE_OTHER';
}

/** A Material icon for the type: the platform for a known one, a question mark for anything else. */
export function kindIcon(type: string | null): string {
  const t = normalise(type);
  if (!LABEL_KEYS.has(t)) return 'help_outline';
  if (t === 'webhook') return 'webhook';
  return t.startsWith('discord:') ? 'tag' : 'send';
}
