import { serverReason } from './server-reason';

describe('serverReason', () => {
  it('returns the API’s error text', () => {
    expect(serverReason({ error: { error: 'userId does not name an existing user.' }, status: 400 })).toBe(
      'userId does not name an existing user.',
    );
  });

  it.each([null, undefined, { status: 0 }, { error: 'plain text' }, { error: { error: '  ' } }, { error: { error: 42 } }])(
    'returns null when there is no usable reason (%p)',
    err => {
      expect(serverReason(err)).toBeNull();
    },
  );

  it('returns the reason a 409 gave', () => {
    expect(serverReason({ error: { error: 'Geofence could not be renamed right now.' }, status: 409 })).toBe(
      'Geofence could not be renamed right now.',
    );
  });

  /**
   * A 503's body is the server's English ("PoracleNG could not be reached. Try again shortly."), and the
   * caller's own message is translated, so a server failure keeps the caller's words.
   */
  it.each([500, 502, 503, 504])('returns null for a %i, whose body is not a reason the user can act on', status => {
    expect(serverReason({ error: { error: 'PoracleNG could not be reached. Try again shortly.' }, status })).toBeNull();
  });
});

/**
 * A few 5xx answers are the API's own sentence with a next step in it, not an outage: the #482 webhook
 * half-write ("Check the webhook list before trying again") and Koji refusing an approve, reject or delete
 * ("the region may no longer exist"). Those call sites opt in for exactly the status they expect.
 */
describe('serverReason with an opted-in 5xx', () => {
  const halfWrite = {
    error: {
      error:
        'Poracle rejected the destination, and could not be asked whether part of it was saved. Check the destination list before trying again.',
    },
    status: 502,
  };

  it('returns the reason for a status the caller allows', () => {
    expect(serverReason(halfWrite, { allow5xx: [502] })).toBe(halfWrite.error.error);
  });

  it('still drops a 5xx the caller did not name, so an outage keeps the translated message', () => {
    expect(
      serverReason({ error: { error: 'PoracleNG could not be reached. Try again shortly.' }, status: 503 }, { allow5xx: [502] }),
    ).toBeNull();
    expect(serverReason({ error: { error: 'An unexpected error occurred.' }, status: 500 }, { allow5xx: [502] })).toBeNull();
  });

  it('keeps reading 4xx reasons', () => {
    expect(serverReason({ error: { error: 'Webhook already exists.' }, status: 409 }, { allow5xx: [502] })).toBe('Webhook already exists.');
  });
});
