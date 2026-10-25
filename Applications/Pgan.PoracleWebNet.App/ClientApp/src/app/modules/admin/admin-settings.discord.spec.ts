import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AdminSettingsComponent } from './admin-settings.component';
import { DiscordServerConfig } from '../../core/models';
import { SettingsService } from '../../core/services/settings.service';

/**
 * Discord is optional: a Telegram-only or SSO-only site has no Discord application. The Discord section
 * keeps its switch (and the bot token, guild and admin ids, which have nothing to do with sign-in) but
 * must say the switch does nothing until the client id and secret are set.
 */
describe('AdminSettingsComponent Discord section', () => {
  const config = (overrides: Partial<DiscordServerConfig>): DiscordServerConfig => ({
    adminIds: '',
    botToken: '',
    clientId: '1234****5678',
    clientSecret: '****abcd',
    geofenceForumChannelId: '',
    guildId: '',
    ...overrides,
  });

  const create = (discord: DiscordServerConfig) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        {
          provide: SettingsService,
          useValue: {
            getOidcConfig: () => of(null),
            getAll: () => of([{ key: 'enable_discord', value: 'True' }]),
            getDiscordConfig: () => of(discord),
            getTelegramConfig: () => of(null),
            isForcedByPoracle: () => false,
            siteSettings: () => ({}),
            update: () => of({}),
          },
        },
      ],
      imports: [AdminSettingsComponent, NoopAnimationsModule],
    });
    const fixture = TestBed.createComponent(AdminSettingsComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('says Discord sign-in is not set up when the server has no client id or secret', () => {
    const el = create(config({ clientId: '', clientSecret: '' }));

    expect(el.querySelector('[data-test="discord-not-configured"]')).toBeTruthy();
    // The rest of the section still renders: the bot token and admin ids are not about sign-in.
    expect(el.textContent).toContain('ADMIN_SETTINGS.DISCORD_CLIENT_ID_LABEL');
  });

  it('shows no hint on a site where Discord is configured', () => {
    const el = create(config({}));

    expect(el.querySelector('[data-test="discord-not-configured"]')).toBeNull();
    expect(el.textContent).toContain('ADMIN_SETTINGS.DISCORD_CLIENT_ID_LABEL');
  });
});
