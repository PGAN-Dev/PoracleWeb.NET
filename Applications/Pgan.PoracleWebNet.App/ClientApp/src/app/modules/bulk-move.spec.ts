import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Type, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { LureListComponent } from './lures/lure-list.component';
import { RaidListComponent } from './raids/raid-list.component';
import { AlarmMoveService } from '../core/services/alarm-move.service';
import { AreaService } from '../core/services/area.service';
import { AuthService } from '../core/services/auth.service';
import { ConfigService } from '../core/services/config.service';
import { EggService } from '../core/services/egg.service';
import { I18nService } from '../core/services/i18n.service';
import { LureService } from '../core/services/lure.service';
import { RaidService } from '../core/services/raid.service';
import { ScannerService } from '../core/services/scanner.service';

const rows = (n: number) => [
  {
    id: '1',
    overrideAreas: null,
    overrideLocationLabel: null,
    uid: n,
    clean: 0,
    distance: 0,
    lureId: 501,
    ping: '',
    profileNo: 1,
    template: '1',
  },
];
const service = (list: unknown[]) => ({
  delete: jest.fn(),
  deleteAll: jest.fn(),
  getAll: () => of(list),
  update: jest.fn(),
  updateBulkDistance: jest.fn(),
});

function mount<T>(type: Type<T>, providers: [unknown, unknown][]) {
  const snackBar = { open: jest.fn() };
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideTranslateService(),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
      { provide: I18nService, useValue: { currentLang: signal('en'), instant: (k: string) => k, plural: (k: string) => k } },
      { provide: AreaService, useValue: { getSelected: () => of([]) } },
      { provide: ScannerService, useValue: { getGymById: () => of(null) } },
      { provide: AuthService, useValue: { isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
      ...providers.map(([provide, useValue]) => ({ provide, useValue })),
    ],
    imports: [type],
  });
  TestBed.overrideComponent(type, {
    add: {
      providers: [
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    },
  });
  const fixture = TestBed.createComponent(type);
  fixture.detectChanges();
  return { component: fixture.componentInstance, snackBar };
}

describe('bulk move', () => {
  it('a move with no results opens no snackbar', async () => {
    const moves = { moveMany: jest.fn().mockReturnValue(of({ results: [] })) };
    const { component, snackBar } = mount(LureListComponent, [
      [LureService, service(rows(1))],
      [AlarmMoveService, moves],
    ]);
    component.toggleSelect(1);

    await component.bulkMove('telegram');

    expect(snackBar.open).not.toHaveBeenCalled();
  });

  it('the raid-then-egg loop summarises what it has when the egg request fails', async () => {
    const moves = {
      moveMany: jest
        .fn()
        .mockReturnValueOnce(of({ results: [{ uid: 7, status: 'moved' }] }))
        .mockReturnValueOnce(throwError(() => ({ status: 502 }))),
    };
    const { component, snackBar } = mount(RaidListComponent, [
      [RaidService, service([])],
      [EggService, service([])],
      [AlarmMoveService, moves],
    ]);
    (component as unknown as { actionIds: () => string[] }).actionIds = () => ['raid:7', 'egg:9'];

    await component.bulkMove('telegram');

    const text = snackBar.open.mock.calls[0][0] as string;
    expect(text).toContain('ALARM.MOVE_DONE');
    expect(text).toContain('ALARM.MOVE_FAILED');
    expect(moves.moveMany).toHaveBeenCalledTimes(2);
  });
});
