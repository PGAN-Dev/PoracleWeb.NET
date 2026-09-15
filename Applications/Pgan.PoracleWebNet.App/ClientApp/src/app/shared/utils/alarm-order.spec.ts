import { orderAlarms } from './alarm-order';

describe('orderAlarms', () => {
  it('keeps a rule where it was after an edit gave it a new id', () => {
    // The regression this exists for. PoracleNG 5.2.0 replaces a rule rather than updating it, so the
    // edited row comes back with the highest id in the list and the card jumped to the end of the grid.
    const before = [
      { level: 5, pokemonId: 25, uid: 10 },
      { level: 5, pokemonId: 150, uid: 11 },
      { level: 5, pokemonId: 380, uid: 12 },
    ];
    const afterEditingMewtwo = [
      { level: 5, pokemonId: 25, uid: 10 },
      { level: 5, pokemonId: 380, uid: 12 },
      { level: 5, pokemonId: 150, uid: 99 },
    ];

    const key = (r: { level: number; pokemonId: number }) => [r.pokemonId, r.level];

    expect(orderAlarms(afterEditingMewtwo, key).map(r => r.pokemonId)).toEqual(orderAlarms(before, key).map(r => r.pokemonId));
    expect(orderAlarms(afterEditingMewtwo, key)[1].uid).toBe(99);
  });

  it('falls back to the id so the order is total', () => {
    const items = [{ uid: 3 }, { uid: 1 }, { uid: 2 }];

    expect(orderAlarms(items, () => []).map(i => i.uid)).toEqual([1, 2, 3]);
  });

  it('compares numbers as numbers and strings as strings', () => {
    const items = [
      { uid: 1, value: 10 },
      { uid: 2, value: 9 },
    ];

    expect(orderAlarms(items, i => [i.value]).map(i => i.uid)).toEqual([2, 1]);
    expect(
      orderAlarms(
        [
          { name: 'b', uid: 1 },
          { name: 'a', uid: 2 },
        ],
        i => [i.name],
      ).map(i => i.uid),
    ).toEqual([2, 1]);
  });

  it('treats a null or absent key as the empty string rather than throwing', () => {
    // gymId, stationId and fortType are all nullable, and "any gym" is the common case.
    const items = [
      { gymId: 'abc', uid: 1 },
      { gymId: null, uid: 2 },
    ];

    expect(orderAlarms(items, i => [i.gymId]).map(i => i.uid)).toEqual([2, 1]);
  });

  it('does not mutate the list it was given', () => {
    const items = [{ uid: 3 }, { uid: 1 }];

    orderAlarms(items, () => []);

    expect(items.map(i => i.uid)).toEqual([3, 1]);
  });
});
