/** The fields Telegram's redirect sign-in puts on the return URL, when the person has them. */
const TELEGRAM_FIELDS = ['id', 'first_name', 'last_name', 'username', 'photo_url', 'auth_date', 'hash'] as const;

/**
 * Reads the signed fields Telegram appends to the page's `data-auth-url` after a sign-in. Null unless
 * `id`, `auth_date` and `hash` are all there, so an unrelated query string is never taken for a return.
 */
export function readTelegramReturn(params: URLSearchParams): Record<string, string> | null {
  const fields: Record<string, string> = {};
  for (const key of TELEGRAM_FIELDS) {
    const value = params.get(key);
    if (value !== null) fields[key] = value;
  }
  return 'id' in fields && 'auth_date' in fields && 'hash' in fields ? fields : null;
}

/** Drops the query (and the signed hash in it) from the address bar without adding a history entry. */
export function clearTelegramReturn(): void {
  window.history.replaceState(null, '', window.location.pathname + window.location.hash);
}
