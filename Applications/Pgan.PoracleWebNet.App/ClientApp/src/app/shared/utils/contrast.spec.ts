import { CHIP_DARK_TEXT, LIGHT_CARD_SURFACE, contrastRatio, readableChip, readableTextOn } from './contrast';

/** Every fill the app puts a label on today: generation chips, quick-pick alarm types, quest rewards. */
const FILLS = [
  '#4CAF50',
  '#FFD600',
  '#2196F3',
  '#9C27B0',
  '#FF5722',
  '#E91E63',
  '#00BCD4',
  '#795548',
  '#607D8B',
  '#f44336',
  '#ff9800',
  '#00bcd4',
  '#607d8b',
  '#e91e63',
  '#ff5722',
  '#4caf50',
  '#8bc34a',
  '#9c27b0',
  '#FBC02D',
  '#FFB300',
  '#9E9E9E',
  '#666',
];

describe('readableChip', () => {
  it.each(FILLS)('gives %s a label at WCAG AA or better', fill => {
    const { background, color } = readableChip(fill);

    expect(contrastRatio(background, color)).toBeGreaterThanOrEqual(4.5);
  });

  it('leaves a fill that already carries white text alone', () => {
    // Purple and brown pass as they are; they must not come back a shade darker.
    expect(readableChip('#9C27B0')).toEqual({ background: '#9C27B0', color: '#fff' });
    expect(readableChip('#795548')).toEqual({ background: '#795548', color: '#fff' });
  });

  it('keeps yellow yellow, with dark text, rather than turning it olive', () => {
    expect(readableChip('#FFD600')).toEqual({ background: '#FFD600', color: CHIP_DARK_TEXT });
    expect(readableChip('#FBC02D').background).toBe('#FBC02D');
  });

  it('keeps a green chip green and white-on', () => {
    const { background, color } = readableChip('#4caf50');
    const [r, g, b] = [1, 3, 5].map(i => parseInt(background.slice(i, i + 2), 16));

    expect(color).toBe('#fff');
    expect(g).toBeGreaterThan(r);
    expect(g).toBeGreaterThan(b);
  });

  it('passes anything that is not a hex colour straight through', () => {
    expect(readableChip('var(--x)')).toEqual({ background: 'var(--x)', color: '#fff' });
  });
});

describe('readableTextOn', () => {
  /** The six lure colours, which the lure card prints the lure's name in. */
  it.each(['#FF9800', '#03A9F4', '#4CAF50', '#9E9E9E', '#2196F3', '#FFC107'])('makes %s readable on the light card', colour => {
    expect(contrastRatio(readableTextOn(colour), LIGHT_CARD_SURFACE)).toBeGreaterThanOrEqual(4.5);
  });

  it('leaves a colour that already reads alone', () => {
    expect(readableTextOn('#1565c0')).toBe('#1565c0');
  });
});
