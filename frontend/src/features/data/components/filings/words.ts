/** The label of the results parser until real filings pass (ADR 0184): wherever facts or fu-1 appear. */
export const syntheticParser = 'xbrl-1-synthetic';
export const syntheticNote = 'not checked against real filings';

export const isSynthetic = (parserVersion: string) => parserVersion.endsWith('-synthetic');

/** The closed reasons a filing has no broadcast time, in words. */
export const reasonWords: Readonly<Record<string, string>> = {
  absent: 'no broadcast time in the file',
  date_only: 'date without a time',
  no_offset: 'time without an offset',
  unparsable: 'time not readable',
  before_2000: 'time before 2000',
  broadcast_in_future: 'time in the future',
};

export const reasonText = (reason: string | null | undefined) =>
  reason ? (reasonWords[reason] ?? reason) : 'reason not stated';
