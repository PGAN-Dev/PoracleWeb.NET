/**
 * The reason the API gave for refusing a request (`{ error: "..." }`), or null when it gave none.
 * A refusal like "You cannot block your own account." tells the admin what to do; the generic
 * "Failed to block/unblock" it used to be replaced with did not.
 */
export function serverReason(err: unknown): string | null {
  const reason = (err as { error?: { error?: unknown } } | null | undefined)?.error?.error;
  return typeof reason === 'string' && reason.trim() ? reason : null;
}
