import { AlertDefaultsService, MAX_DEFAULT_DISTANCE_KM, MIN_DEFAULT_DISTANCE_KM } from './alert-defaults.service';

const MODE_KEY = 'poracle-default-alert-mode';
const DISTANCE_KEY = 'poracle-default-alert-distance-km';

describe('AlertDefaultsService', () => {
  beforeEach(() => localStorage.clear());

  it('defaults to areas mode and 1 km when nothing is stored', () => {
    const service = new AlertDefaultsService();
    expect(service.defaultMode()).toBe('areas');
    expect(service.defaultDistanceKm()).toBe(1);
  });

  it('reads a previously stored preference on construction', () => {
    localStorage.setItem(MODE_KEY, 'distance');
    localStorage.setItem(DISTANCE_KEY, '2.5');
    const service = new AlertDefaultsService();
    expect(service.defaultMode()).toBe('distance');
    expect(service.defaultDistanceKm()).toBe(2.5);
  });

  it('falls back to areas for an unrecognized stored mode', () => {
    localStorage.setItem(MODE_KEY, 'nonsense');
    expect(new AlertDefaultsService().defaultMode()).toBe('areas');
  });

  it('falls back to 1 km for a non-numeric or non-positive stored distance', () => {
    localStorage.setItem(DISTANCE_KEY, 'abc');
    expect(new AlertDefaultsService().defaultDistanceKm()).toBe(1);
    localStorage.setItem(DISTANCE_KEY, '0');
    expect(new AlertDefaultsService().defaultDistanceKm()).toBe(1);
  });

  it('persists a saved preference to localStorage and updates the signals', () => {
    const service = new AlertDefaultsService();
    service.save('distance', 3);
    expect(service.defaultMode()).toBe('distance');
    expect(service.defaultDistanceKm()).toBe(3);
    expect(localStorage.getItem(MODE_KEY)).toBe('distance');
    expect(localStorage.getItem(DISTANCE_KEY)).toBe('3');
  });

  it('clamps a saved distance to the allowed range', () => {
    const service = new AlertDefaultsService();
    service.save('distance', 9999);
    expect(service.defaultDistanceKm()).toBe(MAX_DEFAULT_DISTANCE_KM);
    service.save('distance', 0.01);
    expect(service.defaultDistanceKm()).toBe(MIN_DEFAULT_DISTANCE_KM);
  });

  it('coerces an invalid saved distance to 1 km', () => {
    const service = new AlertDefaultsService();
    service.save('distance', NaN);
    expect(service.defaultDistanceKm()).toBe(1);
  });
  it('remembers a default place alongside a distance', () => {
    const service = new AlertDefaultsService();
    service.save('distance', 2, 'work');
    expect(service.defaultPlaceLabel()).toBe('work');
    expect(new AlertDefaultsService().defaultPlaceLabel()).toBe('work');
  });

  it('drops the place when the default is areas', () => {
    // A place only means anything alongside a radius; the two are mutually exclusive upstream. Keeping
    // one here would seed every new alarm with a scope PoracleNG refuses.
    const service = new AlertDefaultsService();
    service.save('distance', 2, 'work');
    service.save('areas', 2, 'work');
    expect(service.defaultPlaceLabel()).toBe('');
  });

  it('forgets a default place that no longer exists', () => {
    const service = new AlertDefaultsService();
    service.save('distance', 2, 'work');
    service.reconcilePlace(['home']);
    expect(service.defaultPlaceLabel()).toBe('');
  });

  it('keeps a default place that still exists', () => {
    const service = new AlertDefaultsService();
    service.save('distance', 2, 'work');
    service.reconcilePlace(['home', 'work']);
    expect(service.defaultPlaceLabel()).toBe('work');
  });
});
