import { DESTINATION_KINDS, MENTION_CAPABLE, isPersonType, kindIcon, kindLabelKey, mentionCapable, takesDelegates } from './human-kind';

describe('human-kind', () => {
  it('labels every known type and falls back to Other', () => {
    expect(kindLabelKey('webhook')).toBe('ADMIN.TYPE_WEBHOOK');
    expect(kindLabelKey('discord:channel')).toBe('ADMIN.TYPE_DISCORD_CHANNEL');
    expect(kindLabelKey('discord:thread')).toBe('ADMIN.TYPE_DISCORD_THREAD');
    expect(kindLabelKey('telegram:channel')).toBe('ADMIN.TYPE_TELEGRAM_CHANNEL');
    expect(kindLabelKey('telegram:group')).toBe('ADMIN.TYPE_TELEGRAM_GROUP');
    expect(kindLabelKey('telegram:topic')).toBe('ADMIN.TYPE_TELEGRAM_TOPIC');
    expect(kindLabelKey(' Webhook ')).toBe('ADMIN.TYPE_WEBHOOK');

    expect(kindLabelKey('x:unknown')).toBe('ADMIN.TYPE_OTHER');
    expect(kindLabelKey('discord:user')).toBe('ADMIN.TYPE_OTHER');
    expect(kindLabelKey(null)).toBe('ADMIN.TYPE_OTHER');
    expect(kindLabelKey('constructor')).toBe('ADMIN.TYPE_OTHER');
    expect(kindIcon('toString')).toBe('help_outline');

    expect(kindIcon('webhook')).toBe('webhook');
    expect(kindIcon('discord:channel')).toBe('tag');
    expect(kindIcon('discord:thread')).toBe('tag');
    expect(kindIcon('telegram:channel')).toBe('send');
    expect(kindIcon('telegram:group')).toBe('send');
    expect(kindIcon('telegram:topic')).toBe('send');
    expect(kindIcon('x:unknown')).toBe('help_outline');
    expect(kindIcon(null)).toBe('help_outline');
  });

  it('mention is Discord and webhook only', () => {
    expect([...MENTION_CAPABLE].sort()).toEqual(['discord:channel', 'discord:thread', 'webhook']);
    expect(mentionCapable('webhook')).toBe(true);
    expect(mentionCapable('Discord:Channel')).toBe(true);
    expect(mentionCapable('discord:thread')).toBe(true);
    expect(mentionCapable('telegram:group')).toBe(false);
    expect(mentionCapable('discord:user')).toBe(false);
    expect(mentionCapable('x:unknown')).toBe(false);
    expect(mentionCapable(null)).toBe(false);
    expect(mentionCapable(undefined)).toBe(false);
  });

  it('recognises a person the way the server does: no type, or one ending :user', () => {
    for (const type of [null, undefined, '', '  ', 'discord:user', 'telegram:user', 'Discord:User', 'Telegram:User']) {
      expect(isPersonType(type)).toBe(true);
    }
    for (const type of ['webhook', 'discord:channel', 'telegram:group', 'x:unknown']) {
      expect(isPersonType(type)).toBe(false);
    }
  });

  it('takes delegates on the six destination kinds, in any case, and nothing else', () => {
    for (const kind of DESTINATION_KINDS) {
      expect(takesDelegates(kind)).toBe(true);
      expect(takesDelegates(kind.toUpperCase())).toBe(true);
    }
    for (const other of ['x:unknown', 'discord:user', '', null, undefined, ' webhook']) {
      expect(takesDelegates(other)).toBe(false);
    }
  });
});
