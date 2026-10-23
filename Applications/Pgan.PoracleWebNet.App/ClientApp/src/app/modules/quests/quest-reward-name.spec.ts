import * as fs from 'fs';
import * as path from 'path';

/**
 * The quest list and edit dialog title a mega energy or candy rule with these keys and pass the species
 * as `name`. The keys said only "Mega Energy" and "Candy", so a Charizard rule was titled "Mega Energy"
 * beside a Charizard sprite (alarms D14). Every locale has to place the name somewhere.
 */
describe('quest reward titles name the species', () => {
  const dir = path.join(__dirname, '../../../assets/i18n');
  const locales = fs.readdirSync(dir).filter(f => f.endsWith('.json'));

  for (const file of locales) {
    it(`${file} puts {{name}} in the mega energy and candy titles`, () => {
      const quests = JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8')).QUESTS as Record<string, string>;

      expect(quests['MEGA_ENERGY_SUFFIX']).toContain('{{name}}');
      expect(quests['CANDY_SUFFIX']).toContain('{{name}}');
    });
  }
});
