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
});
