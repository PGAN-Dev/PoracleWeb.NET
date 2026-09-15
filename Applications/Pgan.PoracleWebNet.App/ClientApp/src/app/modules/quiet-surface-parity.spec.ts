import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Every list that names a mutable subject offers to quiet it, and no list that does not pretends to.
 *
 * There are ten alarm types across nine list components (raid-list holds both raids and eggs), and
 * adding a control to some and not the rest is the exact regression shape this codebase keeps hitting.
 * Read from the templates rather than the DOM: what matters is whether the chip is written down, and
 * the alternative is mounting nine lists with a scanner, a masterfile and a mute store behind each.
 */
describe('quiet chip appears on exactly the surfaces with a mutable subject', () => {
  const read = (relative: string) => readFileSync(join(__dirname, relative), 'utf8');

  /** Templates that must carry the chip, and the mute scope each one uses. */
  const EXPECTED: [string, string, string][] = [
    ['pokemon-list', 'pokemon/pokemon-list.component.html', 'pokemon'],
    ['nest-list', 'nests/nest-list.component.html', 'pokemon'],
    ['gym-list', 'gyms/gym-list.component.html', 'gym'],
    ['raid-list', 'raids/raid-list.component.html', 'gym'],
    ['max-battle-list', 'max-battles/max-battle-list.component.html', 'station'],
    ['area-list', 'areas/area-list.component.html', 'area'],
  ];

  /**
   * Templates that must NOT carry it, each with the reason. None of these models holds an identifier
   * that maps to a mute scope: a quest is defined by its reward, an invasion by its grunt type, a lure
   * by its lure type, and a fort change by nothing at all.
   */
  const EXCLUDED: Record<string, string> = {
    'fort-changes/fort-change-list.component.html': 'FortChange carries no fort identifier of any kind.',
    'invasions/invasion-list.component.html': 'Invasion carries no pokestop id; the rule matches on grunt type.',
    'lures/lure-list.component.html': 'Lure carries no pokestop id; the rule matches on lure type.',
    'quests/quest-list.component.html': 'Quest carries no gym, pokestop or pokemon identifier.',
  };

  it.each(EXPECTED)('%s offers a %s quiet chip', (_name, template, scope) => {
    const html = read(template);

    expect(html).toContain('<app-quiet-chip');
    expect(html).toContain(`scope="${scope}"`);
  });

  it.each(Object.entries(EXCLUDED))('%s has no quiet chip: %s', template => {
    expect(read(template)).not.toContain('app-quiet-chip');
  });

  /**
   * The raid list holds two card types and only one of them was wired the first time this shape of
   * change was made elsewhere. Both raid and egg cards name a gym.
   */
  it('raid-list wires both its raid cards and its egg cards', () => {
    const html = read('raids/raid-list.component.html');

    expect(html).toContain('@if (raid.gymId; as gymId)');
    expect(html).toContain('@if (egg.gymId; as eggGymId)');
  });

  /**
   * A gym id of "" means "any gym" and a station id can be null, so an unguarded chip would offer to
   * quiet a subject the alarm does not have. Upstream validates neither, so it would cheerfully store a
   * mute on the empty string that never fires.
   */
  it.each([
    ['gyms/gym-list.component.html', '@if (gym.gymId; as gymId) {'],
    ['raids/raid-list.component.html', '@if (raid.gymId; as gymId) {'],
    ['raids/raid-list.component.html', '@if (egg.gymId; as eggGymId) {'],
    ['max-battles/max-battle-list.component.html', '@if (mb.stationId; as stationId) {'],
  ])('%s guards the chip on the identifier being set', (template, guard) => {
    expect(read(template)).toContain(guard);
  });

  /** pokemonId 0 is the "all Pokemon" rule, which has no single species to quiet. */
  it.each([
    ['pokemon/pokemon-list.component.html', '@if (monster.pokemonId > 0) {'],
    ['nests/nest-list.component.html', '@if (nest.pokemonId > 0) {'],
  ])('%s suppresses the species chip on an all-Pokemon rule', (template, guard) => {
    expect(read(template)).toContain(guard);
  });

  /** Anyone who works from the selected-area chips rather than the checklist needs it in both places. */
  it('area-list offers it on the checklist rows and the selected-area chips', () => {
    const html = read('areas/area-list.component.html');
    const occurrences = html.split('<app-quiet-chip').length - 1;

    expect(occurrences).toBe(2);
  });
});
