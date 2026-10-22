/**
 * The reason the API gave for refusing a request (`{ error: "..." }`), or null when it gave none.
 * A refusal like "You cannot block your own account." tells the admin what to do; the generic
 * "Failed to block/unblock" it used to be replaced with did not.
 *
 * Only a refusal (4xx) counts. A 5xx body is the server's own English ("PoracleNG could not be reached.
 * Try again shortly."), which would replace the caller's translated message with untranslated text and
 * tell the user nothing more than "try later" does.
 */
export function serverReason(err: unknown): string | null {
  const failure = err as { error?: { error?: unknown }; status?: unknown } | null | undefined;
  if (typeof failure?.status === 'number' && failure.status >= 500) return null;
  const reason = failure?.error?.error;
  return typeof reason === 'string' && reason.trim() ? reason : null;
}
