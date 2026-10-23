import { GENERIC_GRUNT_ICON, swapToFallbackIcon } from './icon-fallback';

describe('swapToFallbackIcon', () => {
  it('replaces a failed picture with the generic grunt icon', () => {
    const img = document.createElement('img');
    img.src = 'https://example.test/invasion/46.png';

    swapToFallbackIcon({ target: img } as unknown as Event);

    expect(img.src).toBe(GENERIC_GRUNT_ICON);
  });

  it('leaves a failed fallback alone instead of retrying it forever', () => {
    const img = document.createElement('img');
    img.src = GENERIC_GRUNT_ICON;
    const setter = jest.spyOn(img, 'src', 'set');

    swapToFallbackIcon({ target: img } as unknown as Event);

    expect(setter).not.toHaveBeenCalled();
  });
});
