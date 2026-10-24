import { readTelegramReturn } from './telegram-return';

describe('readTelegramReturn', () => {
  it('returns every Telegram field that is present', () => {
    const q = 'id=1&first_name=A&last_name=B&username=ab&photo_url=https%3A%2F%2Fx%2Fp.jpg&auth_date=1700000000&hash=abc';
    expect(readTelegramReturn(new URLSearchParams(q))).toEqual({
      auth_date: '1700000000',
      first_name: 'A',
      hash: 'abc',
      id: '1',
      last_name: 'B',
      photo_url: 'https://x/p.jpg',
      username: 'ab',
    });
  });

  it('accepts the minimum of id, auth_date and hash', () => {
    expect(readTelegramReturn(new URLSearchParams('id=1&auth_date=2&hash=h'))).toEqual({ auth_date: '2', hash: 'h', id: '1' });
  });

  it('returns null without a hash', () => {
    expect(readTelegramReturn(new URLSearchParams('id=1&auth_date=2'))).toBeNull();
  });

  it('ignores unrelated params', () => {
    expect(readTelegramReturn(new URLSearchParams('id=1&auth_date=2&hash=h&loggedout=1&foo=bar'))).toEqual({
      auth_date: '2',
      hash: 'h',
      id: '1',
    });
    expect(readTelegramReturn(new URLSearchParams('loggedout=1'))).toBeNull();
  });
});
