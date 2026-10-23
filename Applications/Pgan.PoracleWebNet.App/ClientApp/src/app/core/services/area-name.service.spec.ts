import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of, Subject, throwError } from 'rxjs';

import { AreaNameService } from './area-name.service';
import { AuthService } from './auth.service';
import { UserGeofenceService } from './user-geofence.service';
import { UserGeofence } from '../models';

describe('AreaNameService', () => {
  const user = signal<null | { id: string }>({ id: 'me' });
  let reads: jest.Mock<Observable<Partial<UserGeofence>[]>, []>;

  function setup(read: () => Observable<Partial<UserGeofence>[]>): AreaNameService {
    reads = jest.fn(read);
    TestBed.resetTestingModule();
    user.set({ id: 'me' });
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { user } },
        { provide: UserGeofenceService, useValue: { getCustomGeofenceNames: reads } },
      ],
    });
    return TestBed.inject(AreaNameService);
  }

  const loop = { displayName: 'Lake Eola Loop', kojiName: 'lake eola loop 2' };

  it('names a drawn fence by the name its owner gave it, and title-cases anything else', () => {
    const names = setup(() => of([loop]));

    expect(names.label('lake eola loop 2')).toBe('Lake Eola Loop');
    expect(names.label('terrigal')).toBe('Terrigal');
  });

  it('tells two fences with one display name apart', () => {
    const names = setup(() => of([{ displayName: 'Lake Eola Loop', kojiName: 'lake eola loop' }, loop]));

    expect(names.label('lake eola loop')).toBe('Lake Eola Loop');
    expect(names.label('lake eola loop 2')).toBe('Lake Eola Loop (2)');
  });

  it('follows a create and a rename without another read', () => {
    const names = setup(() => of([]));
    expect(names.label('lake eola')).toBe('Lake Eola');

    names.learn([{ displayName: 'Lake Eola', kojiName: 'lake eola' }]);
    expect(names.label('lake eola')).toBe('Lake Eola');

    // Renamed: the key moves with the name, and the old key is no longer a fence.
    names.forget('lake eola');
    names.learn([loop]);
    expect(names.label('lake eola loop 2')).toBe('Lake Eola Loop');
    expect(reads).toHaveBeenCalledTimes(1);
  });

  it("drops one account's names when the session changes, and reads the next account's", () => {
    const names = setup(() => of([loop]));
    TestBed.flushEffects();
    expect(names.label('lake eola loop 2')).toBe('Lake Eola Loop');

    reads.mockReturnValue(of([{ displayName: 'Their Park', kojiName: 'their park' }]));
    user.set({ id: 'them' });
    TestBed.flushEffects();

    expect(names.label('lake eola loop 2')).toBe('Lake Eola Loop 2');
    expect(names.label('their park')).toBe('Their Park');
    expect(reads).toHaveBeenCalledTimes(2);
  });

  it('ignores a read that was still in flight for the previous account', () => {
    const late = new Subject<Partial<UserGeofence>[]>();
    const names = setup(() => late);
    TestBed.flushEffects();
    names.label('x');

    user.set(null);
    TestBed.flushEffects();
    late.next([loop]);

    expect(names.label('lake eola loop 2')).toBe('Lake Eola Loop 2');
  });

  it('retries a failed read on a later use instead of caching the failure', () => {
    jest.useFakeTimers();
    try {
      jest.setSystemTime(new Date('2026-10-22T12:00:00Z'));
      const names = setup(() => throwError(() => new Error('down')));
      expect(names.label('lake eola loop 2')).toBe('Lake Eola Loop 2');
      expect(reads).toHaveBeenCalledTimes(1);

      // Too soon: no hammering the API once per chip.
      names.label('lake eola loop 2');
      expect(reads).toHaveBeenCalledTimes(1);

      jest.setSystemTime(new Date('2026-10-22T12:01:00Z'));
      reads.mockReturnValue(of([loop]));
      names.label('lake eola loop 2');
      expect(names.label('lake eola loop 2')).toBe('Lake Eola Loop');
    } finally {
      jest.useRealTimers();
    }
  });
});
