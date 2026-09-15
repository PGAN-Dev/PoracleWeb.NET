import {
  DEFAULT_ICON_REPOS,
  ICON_REPO_PROBES,
  MAX_ICON_REPOS,
  describeRepoBase,
  isValidRepoBase,
  normalizeRepoBase,
  parseIconRepos,
  serializeIconRepos,
} from './icon-repos';
import { ICON_SOURCE_KEYS } from '../../core/services/icon.service';

describe('parseIconRepos', () => {
  const defaults = DEFAULT_ICON_REPOS.map(r => r.base);

  it('offers the built-in list when nothing is stored', () => {
    expect(parseIconRepos(undefined).map(r => r.base)).toEqual(defaults);
    expect(parseIconRepos(null).map(r => r.base)).toEqual(defaults);
    expect(parseIconRepos('  ').map(r => r.base)).toEqual(defaults);
  });

  it('keeps an empty list empty', () => {
    // Removing every entry is a thing an operator can mean. Falling back to the defaults here would
    // make the list unclearable -- the four would silently come back on the next page load.
    expect(parseIconRepos('[]')).toEqual([]);
  });

  it('reads back what it wrote', () => {
    const repos = [{ name: 'A', base: 'https://a.test/UICONS' }];

    expect(parseIconRepos(serializeIconRepos(repos))).toEqual(repos);
  });

  it('falls back rather than throwing on a value it cannot read', () => {
    // This value reaches a computed on the admin page. Throwing here would take the whole settings
    // page down over one bad row.
    expect(parseIconRepos('not json').map(r => r.base)).toEqual(defaults);
    expect(parseIconRepos('{"not":"an array"}').map(r => r.base)).toEqual(defaults);
  });

  it('drops entries it could not render and keeps the rest', () => {
    const raw = JSON.stringify([
      { name: 'Good', base: 'https://good.test/UICONS' },
      { name: 'Script', base: 'javascript:alert(1)' },
      { name: 'Relative', base: '/relative/UICONS' },
      { name: '   ', base: 'https://noname.test/UICONS' },
      { name: 'No base at all' },
      'a bare string',
      null,
    ]);

    expect(parseIconRepos(raw)).toEqual([{ name: 'Good', base: 'https://good.test/UICONS' }]);
  });

  it('keeps one card per pack', () => {
    const raw = JSON.stringify([
      { name: 'First', base: 'https://a.test/UICONS' },
      { name: 'Same pack, trailing slash', base: 'https://a.test/UICONS/' },
    ]);

    expect(parseIconRepos(raw)).toEqual([{ name: 'First', base: 'https://a.test/UICONS' }]);
  });

  it('stops at the list length the page and the API both bound', () => {
    const raw = JSON.stringify(Array.from({ length: MAX_ICON_REPOS + 5 }, (_, i) => ({ name: `P${i}`, base: `https://a.test/${i}` })));

    expect(parseIconRepos(raw)).toHaveLength(MAX_ICON_REPOS);
  });

  it('trims a name and normalizes a base on the way in', () => {
    const raw = JSON.stringify([{ name: '  Spaced  ', base: '  https://a.test/UICONS//  ' }]);

    expect(parseIconRepos(raw)).toEqual([{ name: 'Spaced', base: 'https://a.test/UICONS' }]);
  });
});

describe('normalizeRepoBase', () => {
  it('drops trailing slashes, which would double up in every built URL', () => {
    expect(normalizeRepoBase('https://a.test/UICONS/')).toBe('https://a.test/UICONS');
    expect(normalizeRepoBase(' https://a.test/UICONS// ')).toBe('https://a.test/UICONS');
  });
});

describe('isValidRepoBase', () => {
  it('accepts plain http, which a pack served off an operator own network needs', () => {
    expect(isValidRepoBase('http://icons.lan:8080/UICONS')).toBe(true);
    expect(isValidRepoBase('https://raw.githubusercontent.com/jms412/PkmnHomeIcons/master/UICONS')).toBe(true);
  });

  it('refuses anything that is not an absolute http(s) URL', () => {
    expect(isValidRepoBase('javascript:alert(1)')).toBe(false);
    expect(isValidRepoBase('data:image/png;base64,AAAA')).toBe(false);
    expect(isValidRepoBase('/assets/UICONS')).toBe(false);
    expect(isValidRepoBase('')).toBe(false);
  });
});

describe('ICON_REPO_PROBES', () => {
  it('probes every category IconService reads', () => {
    // Derived from ICON_SOURCE_FOLDERS rather than listed, so a category added to IconService cannot
    // be left unprobed -- which would let a pack pass the check and then render nothing for it.
    expect(ICON_REPO_PROBES.map(p => p.key).sort()).toEqual([...ICON_SOURCE_KEYS].sort());
  });

  it('names a file this app actually reads, not just the folder', () => {
    // getItemUrl builds reward/item/ and getRaidEggUrl builds raid/egg/. Probing reward/1.png would
    // pass a pack whose reward/item/ is empty.
    const byKey = new Map(ICON_REPO_PROBES.map(p => [p.key, p.path]));

    expect(byKey.get('uicons_reward')).toBe('reward/item/1.png');
    expect(byKey.get('uicons_raid')).toBe('raid/egg/5.png');
  });
});

describe('describeRepoBase', () => {
  it('names an unlisted pack by its host and the end of its path', () => {
    expect(describeRepoBase('https://raw.githubusercontent.com/jms412/PkmnHomeIcons/master/UICONS')).toBe(
      'raw.githubusercontent.com/master/UICONS',
    );
  });

  it('hands back anything it cannot parse', () => {
    expect(describeRepoBase('nonsense')).toBe('nonsense');
  });
});
