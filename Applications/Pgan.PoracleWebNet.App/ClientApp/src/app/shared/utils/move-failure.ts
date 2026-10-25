/**
 * Failures of a request that moves an alarm to the other side of a linked pair.
 *
 * A move that fails after PoracleNG took the copy answers 502 with a sentence that says what to check
 * ("Look in both apps before trying again"). `serverReason` hides a 5xx body by default, so the one place
 * that makes the request marks its errors here and the edit dialogs' `createFailureText` lets a marked
 * 502 through. Nothing else changes: a 502 from any other write keeps the translated message.
 */
const moveFailures = new WeakSet<object>();

export function markMoveFailure<T>(err: T): T {
  if (err && typeof err === 'object') moveFailures.add(err);
  return err;
}

export function isMoveFailure(err: unknown): boolean {
  return !!err && typeof err === 'object' && moveFailures.has(err);
}
