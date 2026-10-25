/** The server's outcome and verdict names mapped once to words (P8-03 decision 8). Nothing here is a success or a failure. */
export interface Words {
  word: string;
  meaning: string;
}

export const outcomeWords: Record<string, Words> = {
  Better: { word: 'Better', meaning: 'Cleared every rule for a change to be called better, on replayed history.' },
  NotBetter: { word: 'Not better', meaning: 'Did not clear the rules; the stored reasons say why.' },
  Inconclusive: { word: 'Inconclusive', meaning: 'Too little evidence to say either way.' },
  CheaperAssumption: {
    word: 'Cheaper assumption',
    meaning: 'Not a gain: a cheaper cost assumption cannot win.',
  },
};

export const verdictWords: Record<string, Words> = {
  Eligible: { word: 'Eligible', meaning: 'The record allows the role champion; nothing here promotes it.' },
  CannotBeatBaselines: { word: 'Cannot beat baselines', meaning: 'It did not beat both baselines.' },
  Inconclusive: { word: 'Inconclusive', meaning: 'Too little evidence to say either way.' },
};

/** A name the map lacks is shown as sent, with a note, never hidden. */
export function wordsOf(map: Record<string, Words>, name: string): Words & { known: boolean } {
  const found = map[name];
  return found
    ? { ...found, known: true }
    : { word: name, meaning: 'Unknown outcome: this screen has no wording for it.', known: false };
}
