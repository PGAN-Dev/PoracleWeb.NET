import { scopeDrawing } from './scope-drawing';

describe('scopeDrawing', () => {
  const pin = { label: '', latitude: 57.15, longitude: -2.1 };
  const work = { label: 'work', latitude: 57.2, longitude: -2.2 };

  it('draws a circle around the pin at the radius typed', () => {
    expect(scopeDrawing({ distanceKm: 2, mode: 'profile' }, pin, [], ['aberdeen'])).toEqual({
      areas: [],
      circle: { latitude: 57.15, longitude: -2.1, radiusMetres: 2000 },
    });
  });

  it('draws a radius the dialog will refuse too, since that is the point of drawing it', () => {
    expect(scopeDrawing({ distanceKm: 500, mode: 'profile' }, pin, [], [])?.circle?.radiusMetres).toBe(500_000);
  });

  it('draws around a saved place by its label', () => {
    expect(scopeDrawing({ distanceKm: 1, mode: 'place', placeLabel: 'work' }, pin, [work], [])?.circle).toEqual({
      latitude: 57.2,
      longitude: -2.2,
      radiusMetres: 1000,
    });
  });

  it('draws nothing for a point it does not know: no pin, or a place since deleted', () => {
    expect(scopeDrawing({ distanceKm: 2, mode: 'profile' }, null, [], [])).toEqual({ areas: [], circle: null });
    expect(scopeDrawing({ distanceKm: 2, mode: 'place', placeLabel: 'gone' }, pin, [work], [])).toEqual({ areas: [], circle: null });
  });

  it('draws no circle for an emptied radius field', () => {
    expect(scopeDrawing({ distanceKm: 0, mode: 'profile' }, pin, [], ['aberdeen']).circle).toBeNull();
  });

  it('shades the chosen areas', () => {
    expect(scopeDrawing({ areas: ['aberdeen', 'dyce'], mode: 'areas' }, pin, [], ['x'])).toEqual({
      areas: ['aberdeen', 'dyce'],
      circle: null,
    });
  });

  it("shades the profile's areas for the inherited scope", () => {
    expect(scopeDrawing({ mode: 'profile' }, pin, [], ['aberdeen'])).toEqual({ areas: ['aberdeen'], circle: null });
  });
});
