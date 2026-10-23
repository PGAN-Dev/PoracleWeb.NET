import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';

import { NestEditDialogComponent } from './nest-edit-dialog.component';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { NestService } from '../../core/services/nest.service';

describe('NestEditDialogComponent', () => {
  let webhookSession = false;
  let sessionType = 'webhook';

  function setup(): void {
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
            minSpawnAvg: 0,
            ping: null,
            pokemonId: 25,
            profileNo: 1,
            template: null,
          },
        },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: NestService, useValue: { update: jest.fn() } },
        { provide: AuthService, useValue: { isImpersonating: () => webhookSession, user: () => ({ type: sessionType }) } },
      ],
      imports: [NestEditDialogComponent],
    });
    TestBed.createComponent(NestEditDialogComponent).detectChanges();
  }

  const showMention = () => TestBed.createComponent(NestEditDialogComponent).componentInstance.showMention;

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
