import { byDestination } from './alarm-list-query';

describe('byDestination', () => {
  const rows = [{ destination: 'discord' as const }, { destination: 'telegram' as const }, {}];

  it('keeps everything on All', () => {
    expect(byDestination(rows, 'all')).toHaveLength(3);
  });

  it('hides the rows of the other side', () => {
    expect(byDestination(rows, 'telegram')).toEqual([{ destination: 'telegram' }]);
    expect(byDestination(rows, 'discord')).toEqual([{ destination: 'discord' }]);
  });
});
