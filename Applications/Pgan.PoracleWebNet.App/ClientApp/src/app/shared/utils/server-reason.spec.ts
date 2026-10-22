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
