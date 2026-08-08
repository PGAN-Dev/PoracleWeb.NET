import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { InvasionAddDialogComponent } from './invasion-add-dialog.component';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { I18nService } from '../../core/services/i18n.service';
import { InvasionService } from '../../core/services/invasion.service';
import { MasterDataService } from '../../core/services/masterdata.service';

/**
 * "Track all" posted a single alarm with gruntType: null. PoracleNG rejects an empty grunt_type
 * ("Grunt type mandatory") and has no catch-all, so the toggle failed 100% of the time while the
 * save button stayed enabled. It now fans out over the Team Rocket grunts. See #416.
 */
describe('InvasionAddDialogComponent — track all (#416)', () => {
  let created: { gruntType: string | null }[];

  const setup = (): InvasionAddDialogComponent => {
    created = [];

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        { provide: I18nService, useValue: { instant: (k: string) => k } },
        { provide: AuthService, useValue: { isImpersonating: () => false } },
        { provide: MasterDataService, useValue: { getPokemon: () => of([]) } },
        { provide: AlertDefaultsService, useValue: { defaultDistanceKm: () => 1, defaultMode: () => 'areas' } },
        {
          provide: InvasionService,
          useValue: {
            create: (payload: { gruntType: string | null }) => {
              created.push(payload);
              return of({ uid: created.length });
            },
          },
        },
      ],
      imports: [InvasionAddDialogComponent],
    });

    const fixture = TestBed.createComponent(InvasionAddDialogComponent);
    const component = fixture.componentInstance;
    component.ngOnInit();
    return component;
  };

  it('never sends a null or empty gruntType', () => {
    const component = setup();
    component.trackAll.set(true);

    component.save();

    expect(created.length).toBeGreaterThan(0);
    for (const payload of created) {
      expect(payload.gruntType).toBeTruthy();
    }
  });

  it('creates one alarm per Team Rocket grunt', () => {
    const component = setup();
    const expected = component.rocketGrunts().length;
    component.trackAll.set(true);

    component.save();

    expect(created).toHaveLength(expected);
  });

  it('covers the leaders and Giovanni, as the hint promises', () => {
    const component = setup();
    component.trackAll.set(true);

    component.save();

    const types = created.map(c => c.gruntType);
    for (const boss of ['cliff', 'arlo', 'sierra', 'giovanni']) {
      expect(types).toContain(boss);
    }
  });

  it('excludes Pokestop event types, which are not Rocket invasions', () => {
    const component = setup();
    component.trackAll.set(true);

    component.save();

    const types = created.map(c => c.gruntType);
    for (const event of ['kecleon', 'gold-stop', 'showcase']) {
      expect(types).not.toContain(event);
    }
  });

  it('still creates only what was ticked when track all is off', () => {
    const component = setup();
    component.toggleGrunt('fire');
    component.toggleGrunt('water');

    component.save();

    expect(created.map(c => c.gruntType).sort()).toEqual(['fire', 'water']);
  });

  it('does nothing when nothing is selected', () => {
    const component = setup();

    component.save();

    expect(created).toHaveLength(0);
  });
});
