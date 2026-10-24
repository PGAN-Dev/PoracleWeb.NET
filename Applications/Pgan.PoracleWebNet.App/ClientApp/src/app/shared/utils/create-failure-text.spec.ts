import { createFailureText } from './add-outcome';
import { markMoveFailure } from './move-failure';

const i18n = { instant: (key: string) => key } as never;

/** The real body PoracleWeb.NET answers a move whose create it cannot account for: 502 with the next step. */
const body = { error: 'Check both apps before trying again.' };

describe('createFailureText on a move', () => {
  it('shows the server sentence for a 502 from a move', () => {
    const err = markMoveFailure({ error: body, status: 502 });

    expect(createFailureText(err, i18n, 'ALARM.FAILED_UPDATE')).toBe(body.error);
  });

  it('keeps the fallback for the same 502 from any other write', () => {
    expect(createFailureText({ error: body, status: 502 }, i18n, 'ALARM.FAILED_UPDATE')).toBe('ALARM.FAILED_UPDATE');
  });

  it('still shows a 4xx reason for an unmarked refusal', () => {
    expect(createFailureText({ error: { error: 'No Telegram account.' }, status: 409 }, i18n, 'ALARM.FAILED_UPDATE')).toBe(
      'No Telegram account.',
    );
  });
});
