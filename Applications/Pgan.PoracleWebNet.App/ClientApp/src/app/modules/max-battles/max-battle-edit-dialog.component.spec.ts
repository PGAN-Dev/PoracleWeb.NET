import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { MaxBattleEditDialogComponent } from './max-battle-edit-dialog.component';
import { MaxBattle, MaxBattleUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MaxBattleService } from '../../core/services/max-battle.service';

/**
 * The edit dialog hardcoded `move: 9000` and `evolution: 9000` (the "any" sentinel) while reading
 * every adjacent field off the existing alarm. Neither dialog can set those filters -- they come from
 * the bot -- so changing the distance silently destroyed them and widened what the user got alerted
 * on, with no way to put them back from the UI. See #412.
 */
describe('MaxBattleEditDialogComponent — filter preservation (#412)', () => {
  const ANY = 9000;

  /** Charizard, filtered to a specific move and evolution. */
  const item: MaxBattle = {
    id: 'user1',
    uid: 79,
    clean: 0,
    distance: 100,
    evolution: 3,
    form: 0,
    gmax: 1,
    level: 7,
    move: 14,
    ping: '',
    pokemonId: 6,
    profileNo: 1,
    stationId: null,
    template: 'default',
  };

  const setup = (overrides: Partial<MaxBattle> = {}) => {
    const sent: MaxBattleUpdate[] = [];

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: MAT_DIALOG_DATA, useValue: { item: { ...item, ...overrides } } },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        { provide: I18nService, useValue: { instant: (k: string) => k } },
        { provide: IconService, useValue: { getPokemonUrl: () => '' } },
        { provide: MasterDataService, useValue: { getPokemonName: () => 'Charizard' } },
        { provide: AuthService, useValue: { isImpersonating: () => false } },
        {
          provide: MaxBattleService,
          useValue: {
            update: (_uid: number, payload: MaxBattleUpdate) => {
              sent.push(payload);
              return of({});
            },
          },
        },
      ],
      imports: [MaxBattleEditDialogComponent],
    });

    const component = TestBed.createComponent(MaxBattleEditDialogComponent).componentInstance;
    return { component, sent };
  };

  it('keeps the move filter when only the distance changes', () => {
    const { component, sent } = setup();
    component.form.controls.distanceMode.setValue('distance');
    component.form.controls.distanceKm.setValue(8);

    component.save();

    expect(sent[0].move).toBe(14);
    expect(sent[0].move).not.toBe(ANY);
  });

  it('keeps the evolution filter when only the distance changes', () => {
    const { component, sent } = setup();
    component.form.controls.distanceMode.setValue('distance');
    component.form.controls.distanceKm.setValue(8);

    component.save();

    expect(sent[0].evolution).toBe(3);
    expect(sent[0].evolution).not.toBe(ANY);
  });

  it('still sends the edited distance', () => {
    const { component, sent } = setup();
    component.form.controls.distanceMode.setValue('distance');
    component.form.controls.distanceKm.setValue(8);

    component.save();

    expect(sent[0].distance).toBe(8000);
  });

  it('leaves an unfiltered alarm unfiltered', () => {
    const { component, sent } = setup({ evolution: ANY, move: ANY });

    component.save();

    expect(sent[0].move).toBe(ANY);
    expect(sent[0].evolution).toBe(ANY);
  });
});
