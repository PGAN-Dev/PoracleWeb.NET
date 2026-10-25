import { moveEditMessage, moveTargetFor, pausedCreateMessage } from './deliver-to';

const t = {
  instant: (key: string, params?: Record<string, unknown>) => `${key}${params ? ' ' + JSON.stringify(params) : ''}`,
} as never;

describe('moveTargetFor', () => {
  it('is null when nothing was chosen or the alarm has no destination (an unlinked person)', () => {
    expect(moveTargetFor(undefined, 'telegram')).toBeNull();
    expect(moveTargetFor('discord', null)).toBeNull();
  });

  it('is null when the choice is the side the alarm is already on', () => {
    expect(moveTargetFor('discord', 'discord')).toBeNull();
  });

  it('names the other side when the person picked it', () => {
    expect(moveTargetFor('discord', 'telegram')).toBe('telegram');
  });
});

describe('moveEditMessage', () => {
  it('is null for a plain save', () => {
    expect(moveEditMessage(null, t)).toBeNull();
  });

  it('says it moved', () => {
    expect(moveEditMessage({ destination: 'telegram', status: 'moved' }, t)).toContain('ALARM.MOVED_TO');
  });

  it('says the old copy is still there, naming both apps', () => {
    const text = moveEditMessage({ destination: 'telegram', status: 'source_remains' }, t)!;

    expect(text).toContain('ALARM.MOVED_SOURCE_REMAINS');
    expect(text).toContain('DELIVER_TO.TELEGRAM');
    expect(text).toContain('DELIVER_TO.DISCORD');
  });
});

describe('pausedCreateMessage', () => {
  it('says nothing will arrive and where to find out why', () => {
    const text = pausedCreateMessage('telegram', t);

    expect(text).toContain('DELIVER_TO.PAUSED');
    expect(text).toContain('DELIVER_TO.TELEGRAM');
    expect(text).toContain('DELIVER_TO.PAUSED_NEXT');
  });
});
