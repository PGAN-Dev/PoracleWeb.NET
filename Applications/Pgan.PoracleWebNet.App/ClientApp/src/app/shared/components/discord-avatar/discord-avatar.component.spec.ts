import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DiscordAvatarComponent } from './discord-avatar.component';
import { ConfigService } from '../../../core/services/config.service';

const DISCORD_DEFAULT = 'https://cdn.discordapp.com/embed/avatars/0.png';

describe('DiscordAvatarComponent', () => {
  let fixture: ComponentFixture<DiscordAvatarComponent>;

  function create(inputs: Record<string, string>): HTMLElement {
    fixture = TestBed.createComponent(DiscordAvatarComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: '' } }],
      imports: [DiscordAvatarComponent],
    });
    // jsdom has no IntersectionObserver; Discord ids use it to batch avatar lookups, which these cases don't exercise.
    (globalThis as unknown as { IntersectionObserver: unknown }).IntersectionObserver = class {
      disconnect(): void {}
      observe(): void {}
    };
    // The component's id-to-url cache is static and would leak between cases.
    (DiscordAvatarComponent as unknown as { cache: Map<string, string> }).cache.clear();
  });

  it('draws the supplied photo for a Telegram person', () => {
    const el = create({ alt: 'Ash', defaultUrl: 'https://t.me/i/userpic/320/a.jpg', userId: 'tg1', userType: 'telegram:user' });
    expect(el.querySelector('img')?.getAttribute('src')).toBe('https://t.me/i/userpic/320/a.jpg');
  });

  it('draws an initial, not a Discord image, for a Telegram person with no photo', () => {
    const el = create({ alt: 'ash', defaultUrl: '', userId: 'tg2', userType: 'telegram:user' });
    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('.avatar-initial')?.textContent?.trim()).toBe('A');
  });

  it('draws a placeholder glyph when there is no name either', () => {
    const el = create({ alt: '', defaultUrl: '', userId: 'tg3', userType: 'webhook' });
    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('.avatar-initial')?.textContent?.trim()).toBe('?');
  });

  it('falls back to the initial when a Telegram photo fails to load', () => {
    const el = create({ alt: 'Misty', defaultUrl: 'https://t.me/i/userpic/320/b.jpg', userId: 'tg4', userType: 'telegram:user' });
    el.querySelector('img')!.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('.avatar-initial')?.textContent?.trim()).toBe('M');
  });

  it('keeps Discord default image for a Discord user', () => {
    const el = create({ alt: 'Brock', defaultUrl: DISCORD_DEFAULT, userId: 'd1', userType: 'discord:user' });
    expect(el.querySelector('img')?.getAttribute('src')).toBe(DISCORD_DEFAULT);
  });

  it('uses the Discord default for a Discord user when none was supplied', () => {
    const el = create({ alt: 'Brock', defaultUrl: '', userId: 'd2', userType: 'discord:user' });
    expect(el.querySelector('img')?.getAttribute('src')).toBe(DISCORD_DEFAULT);
  });

  it('falls back to the Discord default when a Discord avatar fails to load', () => {
    const el = create({ alt: 'Brock', defaultUrl: 'https://cdn.discordapp.com/avatars/1/x.png', userId: 'd3', userType: 'discord:user' });
    el.querySelector('img')!.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(el.querySelector('img')?.getAttribute('src')).toBe(DISCORD_DEFAULT);
  });
});
