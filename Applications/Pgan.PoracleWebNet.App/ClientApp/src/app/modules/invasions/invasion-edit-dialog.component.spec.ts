import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { InvasionEditDialogComponent } from './invasion-edit-dialog.component';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { InvasionService } from '../../core/services/invasion.service';

describe('InvasionEditDialogComponent', () => {
  let webhookSession = false;
  let sessionType = 'webhook';

  const update = jest.fn(() => of(undefined));

  function setup(gruntType = 'mixed'): void {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            id: 'x',
            overrideAreas: null,
            overrideLocationLabel: '',
            uid: 1,
            clean: 0,
            distance: 0,
            gender: 0,
            gruntType,
            ping: null,
            profileNo: 1,
            template: null,
          },
        },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: InvasionService, useValue: { update } },
        { provide: AuthService, useValue: { isImpersonating: () => webhookSession, user: () => ({ type: sessionType }) } },
      ],
      imports: [InvasionEditDialogComponent],
    });
    TestBed.createComponent(InvasionEditDialogComponent).detectChanges();
  }

  describe('Kind sent with a save', () => {
    beforeEach(() => update.mockClear());

    it.each([
      ['water', 'invasion'],
      ['mixed', 'invasion'],
      ['kecleon', 'event'],
      ['gold-stop', 'event'],
      ['showcase', 'event'],
    ])('sends %s as %s, because invasions and events are numbered separately', (gruntType, kind) => {
      setup(gruntType);
      const fixture = TestBed.createComponent(InvasionEditDialogComponent);
      fixture.detectChanges();

      fixture.componentInstance.save();

      expect(update).toHaveBeenCalledTimes(1);
      expect((update.mock.calls[0] as unknown[])[2]).toBe(kind);
    });
  });

  const showMention = () => TestBed.createComponent(InvasionEditDialogComponent).componentInstance.showMention;

  describe('Mention field', () => {
    it('is offered on a webhook alarm', () => {
      webhookSession = true;
      sessionType = 'webhook';
      setup();
      expect(showMention()).toBe(true);
    });

    it('is offered while managing a Discord channel', () => {
      webhookSession = true;
      sessionType = 'discord:channel';
      setup();
      expect(showMention()).toBe(true);
    });

    it('is hidden while managing a Telegram group', () => {
      webhookSession = true;
      sessionType = 'telegram:group';
      setup();
      expect(showMention()).toBe(false);
    });

    it('is hidden on a personal alarm', () => {
      webhookSession = false;
      sessionType = 'discord:user';
      setup();
      expect(showMention()).toBe(false);
    });
  });
});
