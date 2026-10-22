export interface ServerReasonOptions {
  /**
   * 5xx statuses whose body this call site knows to be the API's own actionable sentence rather than an
   * outage. Name the exact status (`[502]`), never a range: a 500 from the global handler and a 503 from the
   * PoracleNG-unreachable filter must keep the caller's translated message.
   */
  allow5xx?: readonly number[];
}

/**
 * The reason the API gave for refusing a request (`{ error: "..." }`), or null when it gave none.
 * A refusal like "You cannot block your own account." tells the admin what to do; the generic
 * "Failed to block/unblock" it used to be replaced with did not.
 *
 * Only a refusal (4xx) counts by default. A 5xx body is usually the server's own English ("PoracleNG could
 * not be reached. Try again shortly."), which would replace the caller's translated message with
 * untranslated text and tell the user nothing more than "try later" does. The exceptions are the few 5xx
 * answers that carry a next step (the #482 webhook half-write, Koji refusing an approve), and their call
 * sites opt in through `allow5xx`.
 */
export function serverReason(err: unknown, options?: ServerReasonOptions): string | null {
  const failure = err as { error?: { error?: unknown }; status?: unknown } | null | undefined;
  if (typeof failure?.status === 'number' && failure.status >= 500 && !options?.allow5xx?.includes(failure.status)) return null;
  const reason = failure?.error?.error;
  return typeof reason === 'string' && reason.trim() ? reason : null;
}
