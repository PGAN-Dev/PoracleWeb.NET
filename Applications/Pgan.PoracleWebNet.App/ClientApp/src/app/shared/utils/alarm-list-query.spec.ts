import { byText, matchesSearch, queryAlarms, searchKey, visibleSelection } from './alarm-list-query';

describe('alarm list search and sort', () => {
  it('matches without case or accents, so "pokestop" finds a Pokéstop event', () => {
    expect(searchKey('Pokéstop Évent')).toBe('pokestop event');
    expect(matchesSearch('pokestop', 'Pokéstop event')).toBe(true);
    expect(matchesSearch('GIRATINA', 'Giratina')).toBe(true);
  });

  it('matches any of the fields it is given, and numbers as text', () => {
    expect(matchesSearch('150', 'Mewtwo', 150)).toBe(true);
    expect(matchesSearch('gym', 'Mewtwo', null, undefined, 'Town gym')).toBe(true);
    expect(matchesSearch('dragonite', 'Mewtwo', 150)).toBe(false);
  });

  it('treats an empty or blank query as matching everything', () => {
    expect(matchesSearch('', 'anything')).toBe(true);
    expect(matchesSearch('   ', 'anything')).toBe(true);
  });

  it('filters and sorts without touching the list it was given', () => {
    const list = [
      { name: 'Valor', uid: 1 },
      { name: 'Any team', uid: 2 },
      { name: 'Mystic', uid: 3 },
    ];
    const shown = queryAlarms(
      list,
      'y',
      x => [x.name],
      byText(x => x.name),
    );

    expect(shown.map(x => x.uid)).toEqual([2, 3]);
    expect(list.map(x => x.uid)).toEqual([1, 2, 3]);
  });

  it('keeps the incoming order between equal keys, so an edited card does not move', () => {
    const list = [
      { name: 'Raid', uid: 9 },
      { name: 'Egg', uid: 4 },
      { name: 'Raid', uid: 2 },
    ];

    expect(
      queryAlarms(
        list,
        '',
        x => [x.name],
        byText(x => x.name),
      ).map(x => x.uid),
    ).toEqual([4, 9, 2]);
  });

  it('keeps the incoming order when there is no comparator', () => {
    const list = [{ uid: 3 }, { uid: 1 }];

    expect(queryAlarms(list, '', () => [], null).map(x => x.uid)).toEqual([3, 1]);
  });

  it('acts only on the selected rules still on screen, in screen order', () => {
    expect(visibleSelection(new Set([1, 2, 3, 4]), [4, 2, 9])).toEqual([4, 2]);
    expect(visibleSelection(new Set<number>(), [1, 2])).toEqual([]);
    expect(visibleSelection(new Set(['raid:1', 'egg:1']), ['egg:1'])).toEqual(['egg:1']);
  });
});
